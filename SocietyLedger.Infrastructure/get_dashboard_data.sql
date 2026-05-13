CREATE OR REPLACE FUNCTION public.get_dashboard_data(
    p_society_id  bigint,
    p_start_date  timestamp without time zone DEFAULT NULL,
    p_end_date    timestamp without time zone DEFAULT NULL
)
RETURNS jsonb
LANGUAGE plpgsql
STABLE
AS $func$
DECLARE
    v_start date;
    v_end   date;
BEGIN
    v_start := COALESCE(p_start_date::date, date_trunc('month', now())::date);
    v_end   := COALESCE(p_end_date::date,  (date_trunc('month', now()) + interval '1 month - 1 day')::date);

    RETURN (
        WITH
        -- ── Base filtered sets ────────────────────────────────────────────────
        filtered_bills AS (
            SELECT *
            FROM   bills
            WHERE  society_id = p_society_id
              AND  is_deleted  = false
        ),
        filtered_payments AS (
            SELECT *
            FROM   maintenance_payments
            WHERE  society_id = p_society_id
              AND  is_deleted  = false
        ),
        filtered_expenses AS (
            SELECT *
            FROM   expenses
            WHERE  society_id = p_society_id
              AND  is_deleted  = false
        ),

        -- ── Snapshot metrics ─────────────────────────────────────────────────
        base AS (
            SELECT COUNT(*) AS total_flats
            FROM   flats
            WHERE  society_id = p_society_id
              AND  is_deleted  = false
        ),

        -- Total billed for bills whose period falls within the selected range.
        -- Excludes cancelled bills so voided charges don't inflate the figure.
        billed AS (
            SELECT COALESCE(SUM(amount), 0) AS total_billed
            FROM   filtered_bills
            WHERE  status_code != 'cancelled'
              AND  to_date(period || '-01', 'YYYY-MM-DD')
                       BETWEEN date_trunc('month', v_start)::date
                           AND date_trunc('month', v_end)::date
        ),

        -- Collection is period-consistent with billed:
        -- sums payments allocated to bills whose period falls in the selected range.
        -- This makes collection_rate meaningful — both sides refer to the same bills.
        collection AS (
            SELECT COALESCE(SUM(mp.amount), 0) AS total_collected
            FROM   filtered_payments mp
            JOIN   bills b ON b.id = mp.bill_id
            WHERE  b.is_deleted    = false
              AND  b.status_code  != 'cancelled'
              AND  to_date(b.period || '-01', 'YYYY-MM-DD')
                       BETWEEN date_trunc('month', v_start)::date
                           AND date_trunc('month', v_end)::date
        ),

        expense AS (
            SELECT COALESCE(SUM(amount), 0) AS total_expense
            FROM   filtered_expenses
            WHERE  date_incurred BETWEEN v_start AND v_end
        ),

        -- Cancelled bills excluded from all outstanding calculations.
        all_time_bill_outstanding AS (
            SELECT COALESCE(SUM(amount - COALESCE(paid_amount, 0)), 0) AS amt
            FROM   filtered_bills
            WHERE  status_code != 'cancelled'
        ),

        period_bill_outstanding AS (
            SELECT COALESCE(SUM(amount - COALESCE(paid_amount, 0)), 0) AS amt
            FROM   filtered_bills
            WHERE  status_code != 'cancelled'
              AND  to_date(period || '-01', 'YYYY-MM-DD')
                       BETWEEN date_trunc('month', v_start)::date
                           AND date_trunc('month', v_end)::date
        ),

        -- Total opening balance dues still remaining across all flats.
        opening_dues AS (
            SELECT COALESCE(SUM(remaining_amount), 0) AS amt
            FROM   adjustments
            WHERE  society_id      = p_society_id
              AND  entry_type      = 'opening_balance'
              AND  remaining_amount > 0
              AND  is_deleted      = false
        ),

        -- ── Fund balance ─────────────────────────────────────────────────────
        fund_entries AS (
            -- Opening fund ledger entries are positive inflow.
            SELECT sfl.transaction_date::date AS entry_date,
                   sfl.amount                 AS signed_amount
            FROM   society_fund_ledger sfl
            WHERE  sfl.society_id  = p_society_id
              AND  sfl.entry_type  = 'opening_fund'
              AND  COALESCE(sfl.is_deleted, false) = false

            UNION ALL

            -- Maintenance payments increase available fund.
            SELECT mp.payment_date::date AS entry_date,
                   mp.amount             AS signed_amount
            FROM   filtered_payments mp

            UNION ALL

            -- Expenses decrease available fund.
            SELECT fe.date_incurred::date AS entry_date,
                   -fe.amount             AS signed_amount
            FROM   filtered_expenses fe
        ),

        opening_fund_balance AS (
            SELECT COALESCE(SUM(signed_amount), 0) AS amt
            FROM   fund_entries
            WHERE  entry_date < v_start
        ),

        period_fund_inflow AS (
            SELECT COALESCE(SUM(signed_amount), 0) AS amt
            FROM   fund_entries
            WHERE  entry_date    BETWEEN v_start AND v_end
              AND  signed_amount > 0
        ),

        period_fund_outflow AS (
            SELECT COALESCE(SUM(ABS(signed_amount)), 0) AS amt
            FROM   fund_entries
            WHERE  entry_date    BETWEEN v_start AND v_end
              AND  signed_amount < 0
        ),

        closing_fund_balance AS (
            SELECT COALESCE(SUM(signed_amount), 0) AS amt
            FROM   fund_entries
            WHERE  entry_date <= v_end
        ),

        -- ── 6-month trend (anchored to the end month) ────────────────────────
        months AS (
            SELECT generate_series(
                       date_trunc('month', v_end) - interval '5 months',
                       date_trunc('month', v_end),
                       interval '1 month'
                   )::date AS m
        ),

        monthly_income AS (
            SELECT date_trunc('month', payment_date)::date AS m,
                   SUM(amount)                             AS income
            FROM   filtered_payments
            GROUP  BY 1
        ),

        monthly_expense AS (
            SELECT date_trunc('month', date_incurred)::date AS m,
                   SUM(amount)                              AS expense
            FROM   filtered_expenses
            GROUP  BY 1
        ),

        monthly AS (
            SELECT to_char(m.m, 'Mon YYYY')  AS label,
                   COALESCE(i.income,  0)    AS income,
                   COALESCE(e.expense, 0)    AS expense,
                   m.m                       AS month_ymd
            FROM   months m
            LEFT   JOIN monthly_income  i ON i.m = m.m
            LEFT   JOIN monthly_expense e ON e.m = m.m
        ),

        -- ── Expense breakdown for the selected period ─────────────────────────
        expense_breakdown AS (
            SELECT COALESCE(ec.display_name, 'Other') AS category,
                   SUM(fe.amount)                     AS amt
            FROM   filtered_expenses fe
            LEFT   JOIN expense_categories ec ON ec.code = fe.category_code
            WHERE  fe.date_incurred BETWEEN v_start AND v_end
            GROUP  BY ec.display_name
        ),

        total_exp_cat AS (
            SELECT COALESCE(SUM(amt), 0) AS total_amt
            FROM   expense_breakdown
        ),

        -- ── Defaulters ───────────────────────────────────────────────────────
        -- Combines monthly bill outstanding AND opening balance remaining_amount
        -- per flat so that flats with only opening dues (no monthly bills yet)
        -- are correctly included in the defaulters list and pending_flats_count.
        all_defaulters AS (
            SELECT f.flat_no,
                   COALESCE(bill_dues.amt, 0) +
                   COALESCE(ob_dues.amt,   0) AS outstanding
            FROM   flats f
            -- unpaid bill dues per flat (cancelled bills excluded)
            LEFT JOIN (
                SELECT flat_id,
                       SUM(amount - COALESCE(paid_amount, 0)) AS amt
                FROM   filtered_bills
                WHERE  status_code != 'cancelled'
                GROUP  BY flat_id
            ) bill_dues ON bill_dues.flat_id = f.id
            -- opening balance remaining dues per flat
            LEFT JOIN (
                SELECT flat_id,
                       SUM(remaining_amount) AS amt
                FROM   adjustments
                WHERE  society_id       = p_society_id
                  AND  entry_type       = 'opening_balance'
                  AND  remaining_amount > 0
                  AND  is_deleted       = false
                GROUP  BY flat_id
            ) ob_dues ON ob_dues.flat_id = f.id
            WHERE  f.society_id = p_society_id
              AND  f.is_deleted = false
              AND  (COALESCE(bill_dues.amt, 0) + COALESCE(ob_dues.amt, 0)) > 0
        ),

        pending_flats AS (
            SELECT COUNT(*) AS cnt
            FROM   all_defaulters
        ),

        defaulters AS (
            SELECT flat_no, outstanding
            FROM   all_defaulters
            ORDER  BY outstanding DESC
            LIMIT  5
        ),

        -- ── Recent activity ──────────────────────────────────────────────────
        -- Expense amounts are positive; the 'type' field signals direction.
        recent AS (
            SELECT 'payment'::text                            AS type,
                   p.amount,
                   p.payment_date                            AS dt,
                   COALESCE(f.flat_no::text, 'Unknown Flat') AS description
            FROM   filtered_payments p
            LEFT   JOIN flats f ON f.id = p.flat_id

            UNION ALL

            SELECT 'expense'::text              AS type,
                   e.amount,
                   e.date_incurred,
                   COALESCE(e.description, 'Expense')
            FROM   filtered_expenses e
        ),

        -- ── Insights ─────────────────────────────────────────────────────────
        insights_cte AS (
            WITH last_two AS (
                SELECT *
                FROM   monthly
                ORDER  BY month_ymd DESC
                LIMIT  2
            ),
            calc AS (
                SELECT MAX(CASE WHEN rn = 1 THEN expense END) AS current_expense,
                       MAX(CASE WHEN rn = 2 THEN expense END) AS previous_expense,
                       MAX(CASE WHEN rn = 1 THEN income  END) AS current_income
                FROM  (
                    SELECT *, ROW_NUMBER() OVER (ORDER BY month_ymd DESC) AS rn
                    FROM   last_two
                ) t
            ),
            top_cat AS (
                SELECT category, amt
                FROM   expense_breakdown
                ORDER  BY amt DESC
                LIMIT  1
            )
            SELECT ARRAY_REMOVE(ARRAY[
                CASE
                    WHEN previous_expense > 0 AND current_expense > previous_expense
                    THEN 'Expenses increased ' ||
                         ROUND(((current_expense - previous_expense) * 100.0 / previous_expense), 1) ||
                         '% vs last month'
                END,
                CASE
                    WHEN total_exp_cat.total_amt > 0
                    THEN (SELECT category FROM top_cat) || ' is ' ||
                         ROUND((SELECT amt FROM top_cat) * 100.0 / total_exp_cat.total_amt, 0) ||
                         '% of total expenses'
                END,
                CASE
                    WHEN collection.total_collected < expense.total_expense
                    THEN 'Expenses exceeded collections by Rs.' ||
                         (expense.total_expense - collection.total_collected)
                END
            ], NULL) AS insights
            FROM calc, total_exp_cat, collection, expense
        )

        -- ── Final JSON assembly ───────────────────────────────────────────────
        SELECT jsonb_build_object(
            'period', jsonb_build_object(
                'start', v_start,
                'end',   v_end
            ),
            'snapshot', jsonb_build_object(
                'total_flats',               base.total_flats,
                'total_billed',              billed.total_billed,
                'total_collected',           collection.total_collected,
                'collection_rate',
                    CASE
                        WHEN billed.total_billed = 0 THEN 0
                        ELSE ROUND((collection.total_collected / billed.total_billed) * 100, 2)
                    END,
                'pending_flats_count',       pending_flats.cnt,
                'period_bill_outstanding',   period_bill_outstanding.amt,
                'all_time_bill_outstanding', all_time_bill_outstanding.amt,
                'opening_dues_remaining',    opening_dues.amt,
                'all_time_member_outstanding',
                    all_time_bill_outstanding.amt + opening_dues.amt,
                'total_expense',             expense.total_expense,
                'net_cash_flow',             collection.total_collected - expense.total_expense,
                'opening_fund_balance',      opening_fund_balance.amt,
                'period_fund_inflow',        period_fund_inflow.amt,
                'period_fund_outflow',       period_fund_outflow.amt,
                'closing_fund_balance',      closing_fund_balance.amt,
                'present_balance',           closing_fund_balance.amt,
                -- backward-compatible aliases
                'bill_outstanding',          all_time_bill_outstanding.amt,
                'total_member_outstanding',  all_time_bill_outstanding.amt + opening_dues.amt,
                'bank_balance',              closing_fund_balance.amt
            ),
            'trend_meta', jsonb_build_object(
                'window_months', 6,
                'end_month',     to_char(date_trunc('month', v_end), 'YYYY-MM')
            ),
            'trends', (
                SELECT COALESCE(
                    jsonb_agg(
                        jsonb_build_object('label', label, 'income', income, 'expense', expense)
                        ORDER BY month_ymd
                    ),
                    '[]'
                )
                FROM monthly
            ),
            'expense_breakdown', (
                SELECT COALESCE(
                    jsonb_agg(
                        jsonb_build_object(
                            'category',   category,
                            'amount',     amt,
                            'percentage',
                                CASE
                                    WHEN total_exp_cat.total_amt = 0 THEN 0
                                    ELSE ROUND((amt / total_exp_cat.total_amt) * 100, 2)
                                END
                        )
                    ),
                    '[]'
                )
                FROM expense_breakdown, total_exp_cat
            ),
            'top_defaulters', (
                SELECT COALESCE(jsonb_agg(d), '[]')
                FROM   defaulters d
            ),
            'recent_activity', (
                SELECT COALESCE(
                    jsonb_agg(
                        jsonb_build_object(
                            'type',        type,
                            'amount',      amount,
                            'date',        dt,
                            'description', description
                        )
                    ),
                    '[]'
                )
                FROM (
                    SELECT * FROM recent
                    ORDER  BY dt DESC
                    LIMIT  10
                ) x
            ),
            'insights', (
                SELECT insights FROM insights_cte
            )
        )
        FROM base, billed, collection, expense,
             period_bill_outstanding, all_time_bill_outstanding,
             opening_dues,
             opening_fund_balance, period_fund_inflow, period_fund_outflow, closing_fund_balance,
             pending_flats
    );
END;
$func$;
