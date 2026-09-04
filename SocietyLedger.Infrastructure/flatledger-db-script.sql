--
-- PostgreSQL database dump
--

-- Dumped from database version 17.6
-- Dumped by pg_dump version 17.5

-- Started on 2026-07-02 13:30:57

SET statement_timeout = 0;
SET lock_timeout = 0;
SET idle_in_transaction_session_timeout = 0;
SET transaction_timeout = 0;
SET client_encoding = 'UTF8';
SET standard_conforming_strings = on;
SELECT pg_catalog.set_config('search_path', '', false);
SET check_function_bodies = false;
SET xmloption = content;
SET client_min_messages = warning;
SET row_security = off;

--
-- TOC entry 93 (class 2615 OID 2200)
-- Name: public; Type: SCHEMA; Schema: -; Owner: pg_database_owner
--

CREATE SCHEMA public;


ALTER SCHEMA public OWNER TO pg_database_owner;

--
-- TOC entry 4379 (class 0 OID 0)
-- Dependencies: 93
-- Name: SCHEMA public; Type: COMMENT; Schema: -; Owner: pg_database_owner
--

COMMENT ON SCHEMA public IS 'standard public schema';


--
-- TOC entry 524 (class 1255 OID 17498)
-- Name: get_collection_summary(bigint, text, text); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_collection_summary(p_society_id bigint, p_start_period text DEFAULT NULL::text, p_end_period text DEFAULT NULL::text) RETURNS json
    LANGUAGE plpgsql STABLE
    AS $$DECLARE v_result json;
BEGIN

WITH filtered_bills AS (
    SELECT *
    FROM bills
    WHERE society_id = p_society_id
      AND NOT is_deleted
      AND (p_start_period IS NULL OR period >= p_start_period)
      AND (p_end_period   IS NULL OR period <= p_end_period)
),

period_summary AS (
    SELECT
        period,
        SUM(amount)                                                        AS total_billed,
        SUM(COALESCE(paid_amount, 0))                                      AS total_collected,
        SUM(amount - COALESCE(paid_amount, 0))                             AS total_outstanding,
        COUNT(*)                                                            AS flats_billed,
        COUNT(*) FILTER (WHERE status_code = 'paid')                       AS flats_paid,
        COUNT(*) FILTER (WHERE status_code = 'partial')                    AS flats_partial,
        COUNT(*) FILTER (WHERE status_code IN ('unpaid', 'overdue'))       AS flats_unpaid,
        COUNT(*) FILTER (WHERE status_code = 'overdue')                    AS flats_overdue
    FROM filtered_bills
    GROUP BY period
),

overall_summary AS (
    SELECT
        SUM(amount)                              AS total_billed,
        SUM(COALESCE(paid_amount, 0))            AS total_collected,
        SUM(amount - COALESCE(paid_amount, 0))   AS total_outstanding,
        COUNT(DISTINCT flat_id)                  AS total_flats
    FROM filtered_bills
)

SELECT json_build_object(
    'total_billed',      COALESCE(o.total_billed, 0),
    'total_collected',   COALESCE(o.total_collected, 0),
    'total_outstanding', COALESCE(o.total_outstanding, 0),
    'total_flats',       COALESCE(o.total_flats, 0),
    'collection_percentage',
        CASE
            WHEN COALESCE(o.total_billed, 0) = 0 THEN 0.00
            ELSE ROUND((COALESCE(o.total_collected, 0) / o.total_billed) * 100, 2)
        END,
    'periods',
        COALESCE(
            (SELECT json_agg(
                json_build_object(
                    'period',            p.period,
                    'total_billed',      p.total_billed,
                    'total_collected',   p.total_collected,
                    'total_outstanding', p.total_outstanding,
                    'flats_billed',      p.flats_billed,
                    'flats_paid',        p.flats_paid,
                    'flats_partial',     p.flats_partial,
                    'flats_unpaid',      p.flats_unpaid,
                    'flats_overdue',     p.flats_overdue
                )
                ORDER BY p.period DESC
            )
            FROM period_summary p),
        '[]'::json
        )
)
INTO v_result
FROM overall_summary o;

RETURN COALESCE(v_result, '{}'::json);

END;$$;


ALTER FUNCTION public.get_collection_summary(p_society_id bigint, p_start_period text, p_end_period text) OWNER TO postgres;

--
-- TOC entry 525 (class 1255 OID 17499)
-- Name: get_dashboard_data(bigint, timestamp without time zone, timestamp without time zone); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone DEFAULT NULL::timestamp without time zone, p_end_date timestamp without time zone DEFAULT NULL::timestamp without time zone) RETURNS jsonb
    LANGUAGE plpgsql STABLE
    AS $$
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

        -- Collections for the selected date window (accounting date = payment_date).
        -- This keeps dashboard filters aligned with cash movement month views.
        collection AS (
            SELECT COALESCE(SUM(mp.amount), 0) AS total_collected
            FROM   filtered_payments mp
            WHERE  mp.payment_date::date BETWEEN v_start AND v_end
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
$$;


ALTER FUNCTION public.get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone, p_end_date timestamp without time zone) OWNER TO postgres;

--
-- TOC entry 526 (class 1255 OID 17501)
-- Name: get_defaulters_report(bigint, numeric); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_defaulters_report(p_society_id bigint, p_min_outstanding numeric DEFAULT 0) RETURNS json
    LANGUAGE plpgsql STABLE
    AS $$DECLARE v_result json;
BEGIN
    SELECT COALESCE(json_agg(
        json_build_object(
            'flat_no',           f.flat_no,
            'owner_name',        COALESCE(f.owner_name, 'Unknown'),
            'contact_mobile',    f.contact_mobile,
            'total_billed',      COALESCE(d.total_billed, 0),
            'total_paid',        COALESCE(d.total_paid, 0),
            'total_outstanding', COALESCE(d.total_outstanding, 0),
            'pending_months',    COALESCE(d.pending_months, 0),
            'total_months',      COALESCE(d.total_months, 0),
            'oldest_due_period', d.oldest_due_period,
            'latest_due_period', d.latest_due_period
        ) ORDER BY d.total_outstanding DESC
    ), '[]'::json)
    INTO v_result
    FROM flats f
    JOIN (
        SELECT
            flat_id,
            SUM(amount)                                                               AS total_billed,
            SUM(COALESCE(paid_amount, 0))                                             AS total_paid,
            SUM(amount - COALESCE(paid_amount, 0))                                    AS total_outstanding,
            COUNT(*)                                                                   AS total_months,
            COUNT(*) FILTER (WHERE status_code IN ('unpaid','partial','overdue'))     AS pending_months,
            MIN(period) FILTER (WHERE status_code IN ('unpaid','partial','overdue'))  AS oldest_due_period,
            MAX(period) FILTER (WHERE status_code IN ('unpaid','partial','overdue'))  AS latest_due_period
        FROM bills
        WHERE society_id = p_society_id AND NOT is_deleted
        GROUP BY flat_id
        HAVING SUM(amount - COALESCE(paid_amount, 0)) > COALESCE(p_min_outstanding, 0)
    ) d ON f.id = d.flat_id
    WHERE f.society_id = p_society_id AND NOT f.is_deleted;

    RETURN COALESCE(v_result, '[]'::json);
END;$$;


ALTER FUNCTION public.get_defaulters_report(p_society_id bigint, p_min_outstanding numeric) OWNER TO postgres;

--
-- TOC entry 527 (class 1255 OID 17502)
-- Name: get_expense_by_category(bigint, date, date); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_expense_by_category(p_society_id bigint, p_start_date date DEFAULT NULL::date, p_end_date date DEFAULT NULL::date) RETURNS json
    LANGUAGE plpgsql STABLE
    AS $$
DECLARE
    v_result json;
BEGIN
    WITH filtered_expenses AS (
        SELECT
            e.amount,
            e.category_code,
            e.date_incurred
        FROM expenses e
        WHERE e.society_id = p_society_id
          AND NOT e.is_deleted
          AND (p_start_date IS NULL OR e.date_incurred >= p_start_date)
          AND (p_end_date   IS NULL OR e.date_incurred <= p_end_date)
    ),
    category_summary AS (
        SELECT
            category_code,
            SUM(amount)        AS total_amount,
            COUNT(*)           AS total_entries,
            MIN(date_incurred) AS first_date,
            MAX(date_incurred) AS last_date
        FROM filtered_expenses
        GROUP BY category_code
    )
    SELECT json_build_object(
        'total_expense', COALESCE((SELECT SUM(amount) FROM filtered_expenses), 0),
        'categories', COALESCE((
            SELECT json_agg(
                json_build_object(
                    'category',           ec.display_name,
                    'category_code',      ec.code,
                    'total_amount',       cs.total_amount,
                    'total_entries',      cs.total_entries,
                    'first_expense_date', TO_CHAR(cs.first_date, 'YYYY-MM-DD'),
                    'last_expense_date',  TO_CHAR(cs.last_date,  'YYYY-MM-DD')
                )
                ORDER BY cs.total_amount DESC
            )
            FROM category_summary cs
            JOIN expense_categories ec
                ON ec.code = cs.category_code
        ), '[]'::json)
    )
    INTO v_result;

    RETURN COALESCE(v_result, '{}'::json);
END;
$$;


ALTER FUNCTION public.get_expense_by_category(p_society_id bigint, p_start_date date, p_end_date date) OWNER TO postgres;

--
-- TOC entry 528 (class 1255 OID 17503)
-- Name: get_fund_ledger_report(bigint, date, date); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_fund_ledger_report(p_society_id bigint, p_start_date date DEFAULT NULL::date, p_end_date date DEFAULT NULL::date) RETURNS jsonb
    LANGUAGE plpgsql STABLE
    AS $$
DECLARE
    v_result jsonb;
BEGIN

    WITH all_entries AS (

        -- Opening fund (Business date)
        SELECT
            sfl.id,
            sfl.transaction_date AS entry_date,
            'opening_fund' AS entry_type,
            sfl.amount,
            sfl.reference,
            sfl.notes
        FROM society_fund_ledger sfl
        WHERE sfl.society_id = p_society_id
          AND sfl.entry_type = 'opening_fund'
          AND NOT sfl.is_deleted

        UNION ALL

        -- Maintenance collections (credits)
        SELECT
            mp.id,
            mp.payment_date AS entry_date,
            'credit' AS entry_type,
            mp.amount,
            f.flat_no || ' - ' || COALESCE(f.owner_name, 'Unknown') AS reference,
            mp.notes
        FROM maintenance_payments mp
        JOIN flats f ON f.id = mp.flat_id
        WHERE mp.society_id = p_society_id
          AND NOT mp.is_deleted

        UNION ALL

        -- Expenses (debits)
        SELECT
            e.id,
            e.date_incurred AS entry_date,
            'debit' AS entry_type,
            e.amount,
            COALESCE(e.vendor, e.category_code) AS reference,
            e.description AS notes
        FROM expenses e
        WHERE e.society_id = p_society_id
          AND NOT e.is_deleted
    ),

    -- Opening balance before requested window
    opening_balance AS (
        SELECT COALESCE(SUM(
            CASE entry_type
                WHEN 'opening_fund' THEN amount
                WHEN 'credit'       THEN amount
                WHEN 'debit'        THEN -amount
                ELSE 0
            END
        ), 0) AS value
        FROM all_entries
        WHERE p_start_date IS NOT NULL
          AND entry_date < p_start_date
    ),

    -- Filtered window entries
    filtered AS (
        SELECT
            id,
            entry_date,
            entry_type,
            CASE WHEN entry_type IN ('credit', 'opening_fund') THEN amount ELSE 0 END AS credit,
            CASE WHEN entry_type = 'debit' THEN amount ELSE 0 END AS debit,
            reference,
            notes
        FROM all_entries
        WHERE (p_start_date IS NULL OR entry_date >= p_start_date)
          AND (p_end_date IS NULL OR entry_date < p_end_date + INTERVAL '1 day')
    ),

    -- Attach opening balance once (avoid repeated subqueries)
    with_balance AS (
        SELECT
            f.*,
            ob.value
            + SUM(f.credit - f.debit) OVER (
                ORDER BY
                    f.entry_date,
                    CASE f.entry_type
                        WHEN 'opening_fund' THEN 0
                        WHEN 'credit' THEN 1
                        WHEN 'debit' THEN 2
                    END,
                    f.id
                ROWS BETWEEN UNBOUNDED PRECEDING AND CURRENT ROW
            ) AS running_balance
        FROM filtered f
        CROSS JOIN opening_balance ob
    )

    SELECT jsonb_build_object(

        'opening_balance', COALESCE((SELECT value FROM opening_balance), 0),

        'total_collections',
            COALESCE((SELECT SUM(credit) FROM filtered WHERE entry_type = 'credit'), 0),

        'total_expenses',
            COALESCE((SELECT SUM(debit) FROM filtered WHERE entry_type = 'debit'), 0),

        'total_opening_fund',
            COALESCE((SELECT SUM(credit) FROM filtered WHERE entry_type = 'opening_fund'), 0),

        'closing_balance',
            COALESCE((SELECT value FROM opening_balance), 0)
            + COALESCE((SELECT SUM(credit - debit) FROM filtered), 0),

        'entries',
            COALESCE(
                (
                    SELECT jsonb_agg(
                        jsonb_build_object(
                            'date', to_char(wb.entry_date, 'YYYY-MM-DD'),
                            'entry_type', wb.entry_type,
                            'credit', wb.credit,
                            'debit', wb.debit,
                            'running_balance', wb.running_balance,
                            'reference', wb.reference,
                            'notes', wb.notes
                        )
                        ORDER BY
                            wb.entry_date,
                            CASE wb.entry_type
                                WHEN 'opening_fund' THEN 0
                                WHEN 'credit' THEN 1
                                WHEN 'debit' THEN 2
                            END,
                            wb.id
                    )
                    FROM with_balance wb
                ),
                '[]'::jsonb
            )

    )
    INTO v_result;

    RETURN COALESCE(v_result, '{}'::jsonb);

END;
$$;


ALTER FUNCTION public.get_fund_ledger_report(p_society_id bigint, p_start_date date, p_end_date date) OWNER TO postgres;

--
-- TOC entry 529 (class 1255 OID 17504)
-- Name: get_income_vs_expense(bigint, date, date); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_income_vs_expense(p_society_id bigint, p_start_date date DEFAULT NULL::date, p_end_date date DEFAULT NULL::date) RETURNS json
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_result json;
BEGIN
    WITH monthly_income AS (
        SELECT
            TO_CHAR(payment_date, 'YYYY-MM') AS month,
            SUM(amount) AS income
        FROM maintenance_payments
        WHERE society_id = p_society_id
          AND NOT is_deleted
          AND payment_date IS NOT NULL
          AND (p_start_date IS NULL OR DATE(payment_date) >= p_start_date)
          AND (p_end_date   IS NULL OR DATE(payment_date) <= p_end_date)
        GROUP BY TO_CHAR(payment_date, 'YYYY-MM')
    ),
    monthly_expense AS (
        SELECT
            TO_CHAR(date_incurred, 'YYYY-MM') AS month,
            SUM(amount) AS expense
        FROM expenses
        WHERE society_id = p_society_id
          AND NOT is_deleted
          AND (p_start_date IS NULL OR date_incurred >= p_start_date)
          AND (p_end_date   IS NULL OR date_incurred <= p_end_date)
        GROUP BY TO_CHAR(date_incurred, 'YYYY-MM')
    ),
    all_months AS (
        SELECT month FROM monthly_income
        UNION
        SELECT month FROM monthly_expense
    ),
    filtered_expenses AS (
        SELECT
            e.amount,
            e.category_code,
            e.date_incurred
        FROM expenses e
        WHERE e.society_id = p_society_id
          AND NOT e.is_deleted
          AND (p_start_date IS NULL OR e.date_incurred >= p_start_date)
          AND (p_end_date   IS NULL OR e.date_incurred <= p_end_date)
    ),
    category_summary AS (
        SELECT
            category_code,
            SUM(amount)        AS total_amount,
            COUNT(*)           AS total_entries,
            MIN(date_incurred) AS first_date,
            MAX(date_incurred) AS last_date
        FROM filtered_expenses
        GROUP BY category_code
    )
    SELECT json_build_object(
        'total_income',  COALESCE((SELECT SUM(income)  FROM monthly_income),  0),
        'total_expense', COALESCE((SELECT SUM(expense) FROM monthly_expense), 0),
        'net_balance',   COALESCE((SELECT SUM(income)  FROM monthly_income),  0)
                       - COALESCE((SELECT SUM(expense) FROM monthly_expense), 0),
        'months', COALESCE((
            SELECT json_agg(
                json_build_object(
                    'month',   m.month,
                    'income',  COALESCE(i.income,  0),
                    'expense', COALESCE(e.expense, 0),
                    'net',     COALESCE(i.income,  0) - COALESCE(e.expense, 0)
                ) ORDER BY m.month
            )
            FROM all_months m
            LEFT JOIN monthly_income  i ON i.month = m.month
            LEFT JOIN monthly_expense e ON e.month = m.month
        ), '[]'::json),
        'categories', COALESCE((
            SELECT json_agg(
                json_build_object(
                    'category',           ec.display_name,
                    'category_code',      ec.code,
                    'color',              ec.color,
                    'total_amount',       cs.total_amount,
                    'total_entries',      cs.total_entries,
                    'first_expense_date', TO_CHAR(cs.first_date, 'YYYY-MM-DD'),
                    'last_expense_date',  TO_CHAR(cs.last_date,  'YYYY-MM-DD')
                ) ORDER BY cs.total_amount DESC
            )
            FROM category_summary cs
            JOIN expense_categories ec ON ec.code = cs.category_code
        ), '[]'::json)
    ) INTO v_result;

    RETURN v_result;
END;
$$;


ALTER FUNCTION public.get_income_vs_expense(p_society_id bigint, p_start_date date, p_end_date date) OWNER TO postgres;

--
-- TOC entry 530 (class 1255 OID 17505)
-- Name: get_maintenance_payment_register(bigint, date, date, integer, integer); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_maintenance_payment_register(p_society_id bigint, p_start_date date DEFAULT NULL::date, p_end_date date DEFAULT NULL::date, p_limit integer DEFAULT 50, p_offset integer DEFAULT 0) RETURNS TABLE(date_paid date, flat_no text, owner_name text, amount numeric, payment_mode text, reference text, notes text, period text, period_label text, recorded_by text, total_count bigint)
    LANGUAGE sql STABLE
    AS $$
WITH filtered_payments AS (
    SELECT mp.*
    FROM maintenance_payments mp
    WHERE mp.society_id = p_society_id
      AND mp.is_deleted = false
      AND (p_start_date IS NULL OR mp.payment_date >= p_start_date)
      AND (p_end_date IS NULL OR mp.payment_date <= p_end_date)
),

count_cte AS (
    SELECT COUNT(*) AS total_count
    FROM filtered_payments
),

paged AS (
    SELECT *
    FROM filtered_payments
    ORDER BY payment_date DESC, id DESC
    LIMIT p_limit
    OFFSET p_offset
)

SELECT
    p.payment_date::date AS date_paid,
    f.flat_no,
    COALESCE(f.owner_name, 'Unknown') AS owner_name,
    p.amount,
    COALESCE(pm.display_name, 'Unknown') AS payment_mode,
    p.reference_number AS reference,
    p.notes,
    b.period,

    CASE
        WHEN b.period IS NULL THEN NULL

        WHEN make_date(
                split_part(b.period, '-', 1)::int,
                split_part(b.period, '-', 2)::int,
                1
             ) < date_trunc('month', p.payment_date)::date
             THEN 'Arrear'

        WHEN make_date(
                split_part(b.period, '-', 1)::int,
                split_part(b.period, '-', 2)::int,
                1
             ) > date_trunc('month', p.payment_date)::date
             THEN 'Advance'

        ELSE 'Current'
    END AS period_label,

    u.name AS recorded_by,
    c.total_count

FROM paged p
JOIN flats f ON f.id = p.flat_id
LEFT JOIN payment_modes pm ON pm.id = p.payment_mode_id
LEFT JOIN bills b ON b.id = p.bill_id
LEFT JOIN users u ON u.id = p.recorded_by
CROSS JOIN count_cte c;
$$;


ALTER FUNCTION public.get_maintenance_payment_register(p_society_id bigint, p_start_date date, p_end_date date, p_limit integer, p_offset integer) OWNER TO postgres;

--
-- TOC entry 531 (class 1255 OID 17506)
-- Name: get_maintenance_payment_register_count(bigint, date, date); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_maintenance_payment_register_count(p_society_id bigint, p_start_date date DEFAULT NULL::date, p_end_date date DEFAULT NULL::date) RETURNS bigint
    LANGUAGE sql STABLE
    AS $$
SELECT COUNT(*)
FROM maintenance_payments mp
WHERE mp.society_id = p_society_id
  AND NOT mp.is_deleted
  AND (p_start_date IS NULL OR mp.payment_date >= p_start_date)
  AND (p_end_date IS NULL OR mp.payment_date < p_end_date + INTERVAL '1 day');
$$;


ALTER FUNCTION public.get_maintenance_payment_register_count(p_society_id bigint, p_start_date date, p_end_date date) OWNER TO postgres;

--
-- TOC entry 533 (class 1255 OID 32096)
-- Name: get_monthly_report(bigint, integer, integer); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) RETURNS json
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_period         text;
    v_start_date     date;
    v_end_date       date;
    v_end_exclusive  date;
    v_society_name   text;

    -- Society-level fund position
    v_opening_bal    numeric := 0;
    v_collected      numeric := 0;
    v_expenses       numeric := 0;
    v_closing_bal    numeric := 0;

    -- Payment summary counters
    v_total_flats    int     := 0;
    v_paid_count     int     := 0;
    v_pending_count  int     := 0;
    v_total_billed   numeric := 0;
    v_pending_amount numeric := 0;
    v_collection_eff numeric := 0;

    -- JSON fragments
    v_flat_rows    json;
    v_expense_rows json;
    v_summary      text;
    v_alerts       json;

BEGIN
    -- ── 1. Period boundaries ──────────────────────────────────────────────────
    v_period     := to_char(p_year, 'FM0000') || '-' || to_char(p_month, 'FM00');
    v_start_date := make_date(p_year, p_month, 1);
    v_end_date   := (v_start_date + interval '1 month - 1 day')::date;
    v_end_exclusive := v_end_date + 1;

    -- ── 2. Society name ───────────────────────────────────────────────────────
    SELECT s.name
    INTO   v_society_name
    FROM   societies s
    WHERE  s.id = p_society_id
      AND  s.is_deleted = false;

    -- Return empty JSON immediately if society not found
    IF v_society_name IS NULL THEN
        RETURN '{}'::json;
    END IF;

    -- ── 3. Society fund opening balance (single CTE, avoids NULL chain) ───────
    --  = opening_fund seed  +  all prior maintenance receipts  −  all prior expenses
    SELECT
        COALESCE(seed.opening_fund, 0)
        + COALESCE(prior_pay.collected, 0)
        - COALESCE(prior_exp.spent, 0)
    INTO v_opening_bal
    FROM (
        SELECT COALESCE(SUM(sfl.amount), 0) AS opening_fund
        FROM   society_fund_ledger sfl
        WHERE  sfl.society_id = p_society_id
          AND  sfl.is_deleted = false
          AND  sfl.entry_type = 'opening_fund'
    ) seed
    CROSS JOIN (
        SELECT COALESCE(SUM(mp.amount), 0) AS collected
        FROM   maintenance_payments mp
        WHERE  mp.society_id = p_society_id
          AND  mp.is_deleted = false
            AND  mp.payment_date < v_start_date::timestamp
    ) prior_pay
    CROSS JOIN (
        SELECT COALESCE(SUM(e.amount), 0) AS spent
        FROM   expenses e
        WHERE  e.society_id    = p_society_id
          AND  e.is_deleted    = false
          AND  e.date_incurred < v_start_date
    ) prior_exp;

    -- ── 4. Current-month society totals ──────────────────────────────────────
    SELECT COALESCE(SUM(mp.amount), 0)
    INTO   v_collected
    FROM   maintenance_payments mp
    WHERE  mp.society_id = p_society_id
      AND  mp.is_deleted = false
            AND  mp.payment_date >= v_start_date::timestamp
            AND  mp.payment_date <  v_end_exclusive::timestamp;

    SELECT COALESCE(SUM(e.amount), 0)
    INTO   v_expenses
    FROM   expenses e
    WHERE  e.society_id    = p_society_id
      AND  e.is_deleted    = false
      AND  e.date_incurred BETWEEN v_start_date AND v_end_date;

    v_closing_bal := v_opening_bal + v_collected - v_expenses;

    -- ── 5. Total flat count ───────────────────────────────────────────────────
    SELECT COUNT(*)
    INTO   v_total_flats
    FROM   flats f
    WHERE  f.society_id = p_society_id
      AND  f.is_deleted = false;

    -- ── 6. Per-flat aggregation ───────────────────────────────────────────────
    --
    --  opening_agg  — original adjustment amounts (pre-system arrear seed).
    --                 Uses adjustments.amount so adj + billed − paid stays self-consistent.
    --
    --  bill_agg     — bill amounts split by period bucket.
    --
    --  payment_agg  — ALL maintenance_payment rows split by payment_date.
    --
    --  Status values:
    --   'paid'         : net balance_amount ≤ 0 (fully cleared or advance credit)
    --   'current_paid' : paid this month's bill in full but prior arrears remain
    --   'partial'      : some payment this period but less than current bill
    --   'unpaid'       : no payment this period and outstanding > 0
    --
    SELECT
        json_agg(row_to_json(fd) ORDER BY fd.flat_no),
        COUNT(*)           FILTER (WHERE fd.status = 'paid' OR fd.status = 'current_paid'),
        COUNT(*)           FILTER (WHERE fd.status IN ('partial','unpaid')),
        COALESCE(SUM(fd.current_bill), 0),
        COALESCE(SUM(CASE WHEN fd.status IN ('partial','unpaid') THEN fd.balance_amount END), 0)
    INTO
        v_flat_rows,
        v_paid_count,
        v_pending_count,
        v_total_billed,
        v_pending_amount
    FROM (
        WITH opening_agg AS (
            SELECT
                a.flat_id,
                COALESCE(SUM(a.amount), 0) AS adj_original
            FROM   adjustments a
            WHERE  a.society_id = p_society_id
              AND  a.entry_type = 'opening_balance'
              AND  a.is_deleted = false
            GROUP  BY a.flat_id
        ),
        bill_agg AS (
            SELECT
                b.flat_id,
                COALESCE(SUM(CASE WHEN b.period <  v_period THEN b.amount END), 0) AS prior_billed,
                COALESCE(SUM(CASE WHEN b.period =  v_period THEN b.amount END), 0) AS current_billed,
                COALESCE(SUM(CASE WHEN b.period <= v_period THEN b.amount END), 0) AS total_billed
            FROM   bills b
            WHERE  b.society_id = p_society_id
              AND  b.is_deleted  = false
            GROUP  BY b.flat_id
        ),
        payment_agg AS (
            SELECT
                mp.flat_id,
                COALESCE(SUM(CASE WHEN mp.payment_date <  v_start_date::timestamp   THEN mp.amount END), 0) AS prior_paid,
                COALESCE(SUM(CASE WHEN mp.payment_date >= v_start_date::timestamp
                                   AND mp.payment_date <  v_end_exclusive::timestamp THEN mp.amount END), 0) AS current_paid,
                COALESCE(SUM(CASE WHEN mp.payment_date <  v_end_exclusive::timestamp THEN mp.amount END), 0) AS total_paid
            FROM   maintenance_payments mp
            WHERE  mp.society_id = p_society_id
              AND  mp.is_deleted  = false
            GROUP  BY mp.flat_id
        )
        SELECT
            f.flat_no,
            f.owner_name,

            -- Opening balance = original pre-system arrear + any prior-period bills
            --                   − all money received before this period
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.prior_billed, 0)
              - COALESCE(pa.prior_paid,   0)
            )                                      AS opening_balance,

            COALESCE(ba.current_billed, 0)         AS current_bill,
            COALESCE(pa.current_paid,   0)         AS current_paid,

            -- Total due = opening balance + current bill (full liability before payment)
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.prior_billed, 0)
              - COALESCE(pa.prior_paid,   0)
              + COALESCE(ba.current_billed, 0)
            )                                      AS total_due,

            -- Closing balance = grand net outstanding
            -- (original arrear + everything ever billed − everything ever paid)
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.total_billed,  0)
              - COALESCE(pa.total_paid,    0)
            )                                      AS balance_amount,

            -- Status logic:
            --   'paid'         : net balance ≤ 0  (fully cleared or advance)
            --   'current_paid' : paid this month in full but prior arrears remain
            --                    (e.g. current_paid >= current_billed AND net > 0)
            --   'partial'      : some payment received but less than current bill
            --   'unpaid'       : no payment received this period, outstanding > 0
            CASE
              -- Fully cleared or advance credit
              WHEN (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) <= 0
                THEN 'paid'

              -- No current bill yet still has outstanding (arrear-only flat)
              WHEN COALESCE(ba.current_billed, 0) = 0
               AND (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) > 0
                THEN 'unpaid'

              -- Paid this month's bill in full but prior arrears remain
              WHEN COALESCE(pa.current_paid, 0) >= COALESCE(ba.current_billed, 0)
               AND COALESCE(ba.current_billed, 0) > 0
               AND (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) > 0
                THEN 'current_paid'

              -- Some payment this period but less than current bill
              WHEN COALESCE(pa.current_paid, 0) > 0
                THEN 'partial'

              ELSE 'unpaid'
            END                                    AS status

        FROM   flats f
        LEFT   JOIN opening_agg oa ON oa.flat_id = f.id
        LEFT   JOIN bill_agg    ba ON ba.flat_id = f.id
        LEFT   JOIN payment_agg pa ON pa.flat_id = f.id
        WHERE  f.society_id = p_society_id
          AND  f.is_deleted  = false
    ) fd;

    -- ── 7. Collection efficiency ──────────────────────────────────────────────
    v_collection_eff :=
        CASE WHEN v_total_billed > 0
             THEN ROUND((v_collected / v_total_billed) * 100, 2)
             ELSE 0
        END;

    -- ── 8. Detailed expenses (ungrouped) ─────────────────────────────────────
    SELECT json_agg(row_to_json(d))
    INTO   v_expense_rows
    FROM (
        SELECT
            e.date_incurred                            AS date_incurred,
            COALESCE(ec.display_name, e.category_code) AS category_name,
            NULLIF(trim(e.description), '')            AS description,
            e.amount                                   AS total_amount
        FROM   expenses e
        LEFT   JOIN expense_categories ec ON ec.code = e.category_code
        WHERE  e.society_id    = p_society_id
          AND  e.is_deleted    = false
          AND  e.date_incurred BETWEEN v_start_date AND v_end_date
        ORDER  BY e.date_incurred ASC, category_name, COALESCE(NULLIF(trim(e.description), ''), ''), e.id ASC
    ) d;

    -- ── 9. Summary text & alerts ──────────────────────────────────────────────
    v_summary :=
        'Total collection ₹' || v_collected ||
        ', expenses ₹'       || v_expenses  ||
        '. '                 || v_pending_count || ' flat(s) have pending dues.';

    v_alerts :=
        CASE WHEN v_pending_count > 0
             THEN json_build_array(v_pending_count || ' flat(s) have pending payments')
             ELSE json_build_array('All flats have cleared dues')
        END;

    -- ── 10. Return final JSON ─────────────────────────────────────────────────
    RETURN json_build_object(
        'society_name',  v_society_name,
        'period_label',  trim(to_char(v_start_date, 'Month')) || ' ' || p_year::text,

        'fund_position', json_build_object(
            'opening_balance', v_opening_bal,
            'collected',       v_collected,
            'expenses',        v_expenses,
            'closing_balance', v_closing_bal
        ),

        'payment_summary', json_build_object(
            'total_flats',           v_total_flats,
            'paid',                  v_paid_count,
            'pending',               v_pending_count,
            'total_billed',          v_total_billed,
            'total_collected',       v_collected,
            'pending_amount',        v_pending_amount,
            'collection_efficiency', v_collection_eff
        ),

        'flat_details',  COALESCE(v_flat_rows,   '[]'::json),
        'expenses',      COALESCE(v_expense_rows, '[]'::json),
        'summary',       v_summary,
        'alerts',        v_alerts
    );

END;
$$;


ALTER FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) OWNER TO postgres;

--
-- TOC entry 534 (class 1255 OID 32097)
-- Name: get_yearly_report(bigint, integer, text); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.get_yearly_report(p_society_id bigint, p_year integer, p_year_type text DEFAULT 'calendar'::text) RETURNS json
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_start_date   date;
    v_end_date     date;
    v_end_exclusive date;
    v_year_label   text;
    v_society_name text;

    v_opening_bal  numeric := 0;
    v_collected    numeric := 0;
    v_expenses     numeric := 0;
    v_total_billed numeric := 0;
    v_closing_bal  numeric := 0;

    v_month_rows   json;
    v_expense_rows json;
    v_summary      text;
    v_alerts       json;

BEGIN
    -- ── 1. Year boundaries ────────────────────────────────────────────────────
    IF lower(COALESCE(p_year_type, 'calendar')) = 'financial' THEN
        v_start_date := make_date(p_year - 1, 4, 1);
        v_end_date   := make_date(p_year,     3, 31);
        v_year_label := 'FY ' || (p_year - 1)::text || '-' || right(p_year::text, 2);
    ELSE
        v_start_date := make_date(p_year,  1,  1);
        v_end_date   := make_date(p_year, 12, 31);
        v_year_label := p_year::text;
    END IF;

    v_end_exclusive := v_end_date + 1;

    -- ── 2. Society name ───────────────────────────────────────────────────────
    SELECT name
    INTO   v_society_name
    FROM   societies
    WHERE  id = p_society_id
      AND  is_deleted = false;

    -- Return empty JSON immediately if society not found
    IF v_society_name IS NULL THEN
        RETURN '{}'::json;
    END IF;

    -- ── 3. Opening balance (single query, avoids NULL chain) ──────────────────
    --  = opening_fund seed  +  all prior maintenance receipts  −  all prior expenses
    SELECT
        COALESCE(seed.opening_fund, 0)
        + COALESCE(prior_pay.collected, 0)
        - COALESCE(prior_exp.spent,     0)
    INTO v_opening_bal
    FROM (
        SELECT COALESCE(SUM(amount), 0) AS opening_fund
        FROM   society_fund_ledger
        WHERE  society_id = p_society_id
          AND  is_deleted  = false
          AND  entry_type  = 'opening_fund'
    ) seed
    CROSS JOIN (
        SELECT COALESCE(SUM(amount), 0) AS collected
        FROM   maintenance_payments
        WHERE  society_id = p_society_id
          AND  is_deleted  = false
            AND  payment_date < v_start_date::timestamp
    ) prior_pay
    CROSS JOIN (
        SELECT COALESCE(SUM(amount), 0) AS spent
        FROM   expenses
        WHERE  society_id    = p_society_id
          AND  is_deleted     = false
          AND  date_incurred  < v_start_date
    ) prior_exp;

    -- ── 4. Year totals ────────────────────────────────────────────────────────
    SELECT COALESCE(SUM(amount), 0)
    INTO   v_collected
    FROM   maintenance_payments
    WHERE  society_id = p_society_id
      AND  is_deleted  = false
            AND  payment_date >= v_start_date::timestamp
            AND  payment_date <  v_end_exclusive::timestamp;

    SELECT COALESCE(SUM(amount), 0)
    INTO   v_expenses
    FROM   expenses
    WHERE  society_id    = p_society_id
      AND  is_deleted     = false
      AND  date_incurred  BETWEEN v_start_date AND v_end_date;

    SELECT COALESCE(SUM(amount), 0)
    INTO   v_total_billed
    FROM   bills
    WHERE  society_id = p_society_id
      AND  is_deleted  = false
      AND  period >= to_char(v_start_date, 'YYYY-MM')
      AND  period <= to_char(v_end_date,   'YYYY-MM');

    v_closing_bal := v_opening_bal + v_collected - v_expenses;

    -- ── 5. Monthly breakdown ──────────────────────────────────────────────────
    --  All months in the range are included (zeros shown) so the Excel
    --  always presents a complete grid regardless of activity.
    SELECT json_agg(row_to_json(d) ORDER BY d.month_start)
    INTO   v_month_rows
    FROM (
        SELECT
            m.month_start,
            trim(to_char(m.month_start, 'Month')) || ' ' || to_char(m.month_start, 'YYYY') AS month_label,

            COALESCE(billed.total_billed,    0) AS billed,
            COALESCE(col.total_collected,    0) AS collected,
            COALESCE(exp.total_expenses,     0) AS expenses,

            -- net = collected − expenses (monthly net position)
            COALESCE(col.total_collected, 0) - COALESCE(exp.total_expenses, 0) AS net,

            CASE
                WHEN COALESCE(col.total_collected, 0) - COALESCE(exp.total_expenses, 0) >= 0
                THEN 'surplus'
                ELSE 'deficit'
            END AS month_status

        FROM (
            SELECT generate_series(
                       date_trunc('month', v_start_date)::date,
                       date_trunc('month', v_end_date)::date,
                       '1 month'
                   )::date AS month_start
        ) m

        LEFT JOIN (
            SELECT period, SUM(amount) AS total_billed
            FROM   bills
            WHERE  society_id = p_society_id AND is_deleted = false
            GROUP  BY period
        ) billed ON billed.period = to_char(m.month_start, 'YYYY-MM')

        LEFT JOIN (
            SELECT to_char(DATE(payment_date), 'YYYY-MM') AS period,
                   SUM(amount)                             AS total_collected
            FROM   maintenance_payments
            WHERE  society_id = p_society_id AND is_deleted = false
            GROUP  BY 1
        ) col ON col.period = to_char(m.month_start, 'YYYY-MM')

        LEFT JOIN (
            SELECT to_char(date_incurred, 'YYYY-MM') AS period,
                   SUM(amount)                        AS total_expenses
            FROM   expenses
            WHERE  society_id = p_society_id AND is_deleted = false
            GROUP  BY 1
        ) exp ON exp.period = to_char(m.month_start, 'YYYY-MM')
    ) d;

    -- ── 6. Detailed expenses (ungrouped) ─────────────────────────────────────
    SELECT json_agg(row_to_json(d))
    INTO   v_expense_rows
    FROM (
        SELECT
            e.date_incurred                            AS date_incurred,
            COALESCE(ec.display_name, e.category_code) AS category_name,
            NULLIF(trim(e.description), '')            AS description,
            e.amount                                   AS total_amount
        FROM   expenses e
        LEFT   JOIN expense_categories ec ON ec.code = e.category_code
        WHERE  e.society_id    = p_society_id
          AND  e.is_deleted     = false
          AND  e.date_incurred  BETWEEN v_start_date AND v_end_date
        ORDER  BY e.date_incurred ASC, category_name, COALESCE(NULLIF(trim(e.description), ''), ''), e.id ASC
    ) d;

    -- ── 7. Summary text & alerts ──────────────────────────────────────────────
    v_summary :=
        'Total collected ₹' || v_collected   ||
        ', total expenses ₹' || v_expenses   ||
        '. Closing balance ₹' || v_closing_bal || '.';

    v_alerts :=
        CASE WHEN v_closing_bal >= 0
             THEN json_build_array('Funds are sufficient for maintenance')
             ELSE json_build_array('Attention: Negative balance — review expenses')
        END;

    -- ── 8. Return final JSON ──────────────────────────────────────────────────
    RETURN json_build_object(
        'society_name', v_society_name,
        'year_label',   v_year_label,

        'fund_position', json_build_object(
            'opening_balance', v_opening_bal,
            'total_billed',    v_total_billed,
            'total_collected', v_collected,
            'total_expenses',  v_expenses,
            'closing_balance', v_closing_bal
        ),

        'month_summary', COALESCE(v_month_rows,   '[]'::json),
        'expenses',      COALESCE(v_expense_rows,  '[]'::json),
        'summary',       v_summary,
        'alerts',        v_alerts
    );

END;
$$;


ALTER FUNCTION public.get_yearly_report(p_society_id bigint, p_year integer, p_year_type text) OWNER TO postgres;

--
-- TOC entry 532 (class 1255 OID 17507)
-- Name: prevent_hard_delete_payments(); Type: FUNCTION; Schema: public; Owner: postgres
--

CREATE FUNCTION public.prevent_hard_delete_payments() RETURNS trigger
    LANGUAGE plpgsql
    AS $$
BEGIN
  RAISE EXCEPTION 'Hard delete is not allowed. Use soft delete.';
END;
$$;


ALTER FUNCTION public.prevent_hard_delete_payments() OWNER TO postgres;

SET default_tablespace = '';

SET default_table_access_method = heap;

--
-- TOC entry 346 (class 1259 OID 17508)
-- Name: adjustments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.adjustments (
    id bigint NOT NULL,
    society_id bigint NOT NULL,
    flat_id bigint,
    amount numeric(13,2) NOT NULL,
    reason text,
    created_by bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    entry_type text DEFAULT 'manual'::text NOT NULL,
    period text,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    remaining_amount numeric(13,2) NOT NULL
);


ALTER TABLE public.adjustments OWNER TO postgres;

--
-- TOC entry 347 (class 1259 OID 17517)
-- Name: adjustments_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.adjustments ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.adjustments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 405 (class 1259 OID 24937)
-- Name: admin_users; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.admin_users (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    email character varying(255) NOT NULL,
    password_hash text NOT NULL,
    name character varying(100) NOT NULL,
    is_active boolean DEFAULT true NOT NULL,
    last_login timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.admin_users OWNER TO postgres;

--
-- TOC entry 404 (class 1259 OID 24936)
-- Name: admin_users_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.admin_users ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.admin_users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 348 (class 1259 OID 17518)
-- Name: attachments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.attachments (
    id bigint NOT NULL,
    society_id bigint NOT NULL,
    object_key text NOT NULL,
    file_name text,
    mime_type text,
    file_size bigint,
    checksum text,
    uploaded_by bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone
);


ALTER TABLE public.attachments OWNER TO postgres;

--
-- TOC entry 349 (class 1259 OID 17526)
-- Name: attachments_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.attachments ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.attachments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 350 (class 1259 OID 17527)
-- Name: audit_logs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.audit_logs (
    id bigint NOT NULL,
    society_id bigint,
    table_name text NOT NULL,
    record_id bigint,
    record_public_id uuid,
    action text NOT NULL,
    changed_by bigint,
    changed_at timestamp with time zone DEFAULT now() NOT NULL,
    diff jsonb,
    metadata jsonb
);


ALTER TABLE public.audit_logs OWNER TO postgres;

--
-- TOC entry 351 (class 1259 OID 17533)
-- Name: audit_logs_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.audit_logs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.audit_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 352 (class 1259 OID 17534)
-- Name: bill_items; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bill_items (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    bill_id bigint NOT NULL,
    component_name text NOT NULL,
    calculation_type text NOT NULL,
    rate numeric(13,4),
    quantity numeric(13,2),
    amount numeric(13,2) NOT NULL,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.bill_items OWNER TO postgres;

--
-- TOC entry 353 (class 1259 OID 17541)
-- Name: bill_items_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.bill_items_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bill_items_id_seq OWNER TO postgres;

--
-- TOC entry 4401 (class 0 OID 0)
-- Dependencies: 353
-- Name: bill_items_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.bill_items_id_seq OWNED BY public.bill_items.id;


--
-- TOC entry 354 (class 1259 OID 17542)
-- Name: bill_payment_allocations; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bill_payment_allocations (
    id bigint NOT NULL,
    payment_id bigint NOT NULL,
    bill_id bigint NOT NULL,
    allocated_amount numeric(13,2) NOT NULL
);


ALTER TABLE public.bill_payment_allocations OWNER TO postgres;

--
-- TOC entry 355 (class 1259 OID 17545)
-- Name: bill_payment_allocations_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.bill_payment_allocations_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.bill_payment_allocations_id_seq OWNER TO postgres;

--
-- TOC entry 4404 (class 0 OID 0)
-- Dependencies: 355
-- Name: bill_payment_allocations_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.bill_payment_allocations_id_seq OWNED BY public.bill_payment_allocations.id;


--
-- TOC entry 356 (class 1259 OID 17546)
-- Name: bill_statuses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bill_statuses (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL
);


ALTER TABLE public.bill_statuses OWNER TO postgres;

--
-- TOC entry 357 (class 1259 OID 17551)
-- Name: bill_statuses_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.bill_statuses ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.bill_statuses_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 358 (class 1259 OID 17552)
-- Name: bills; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.bills (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    flat_id bigint NOT NULL,
    period text NOT NULL,
    amount numeric(13,2) NOT NULL,
    due_date date,
    status_code text DEFAULT 'unpaid'::text NOT NULL,
    generated_by bigint,
    generated_at timestamp with time zone DEFAULT now() NOT NULL,
    note text,
    source text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    maintenance_plan_id bigint,
    paid_amount numeric(13,2) DEFAULT 0,
    balance_amount numeric(13,2) GENERATED ALWAYS AS ((amount - paid_amount)) STORED,
    updated_at timestamp with time zone DEFAULT now(),
    CONSTRAINT bills_amount_check CHECK ((amount >= (0)::numeric)),
    CONSTRAINT chk_bill_status_valid CHECK ((status_code = ANY (ARRAY['unpaid'::text, 'partial'::text, 'paid'::text, 'overdue'::text]))),
    CONSTRAINT chk_period_not_empty CHECK (((period IS NOT NULL) AND (length(TRIM(BOTH FROM period)) > 0)))
);


ALTER TABLE public.bills OWNER TO postgres;

--
-- TOC entry 359 (class 1259 OID 17568)
-- Name: bills_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.bills ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.bills_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 418 (class 1259 OID 42276)
-- Name: email_notification_logs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.email_notification_logs (
    id bigint NOT NULL,
    notification_type text NOT NULL,
    recipient_email text NOT NULL,
    recipient_name text,
    subject text NOT NULL,
    sent_at timestamp with time zone DEFAULT now() NOT NULL,
    sent_by_system boolean DEFAULT true NOT NULL,
    status text DEFAULT 'sent'::text NOT NULL,
    error_message text,
    society_id bigint,
    user_id bigint,
    metadata text
);


ALTER TABLE public.email_notification_logs OWNER TO postgres;

--
-- TOC entry 417 (class 1259 OID 42275)
-- Name: email_notification_logs_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.email_notification_logs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.email_notification_logs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 360 (class 1259 OID 17569)
-- Name: expense_categories; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.expense_categories (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL,
    color character varying(10)
);


ALTER TABLE public.expense_categories OWNER TO postgres;

--
-- TOC entry 361 (class 1259 OID 17574)
-- Name: expense_categories_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.expense_categories ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.expense_categories_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 362 (class 1259 OID 17575)
-- Name: expenses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.expenses (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    date_incurred date NOT NULL,
    category_code text DEFAULT 'others'::text NOT NULL,
    vendor text,
    description text,
    amount numeric(13,2) NOT NULL,
    attachment_id bigint,
    approved_by bigint,
    status text DEFAULT 'recorded'::text NOT NULL,
    created_by bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT expenses_amount_check CHECK ((amount >= (0)::numeric))
);


ALTER TABLE public.expenses OWNER TO postgres;

--
-- TOC entry 363 (class 1259 OID 17586)
-- Name: expenses_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.expenses ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.expenses_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 407 (class 1259 OID 24951)
-- Name: feature_flags; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.feature_flags (
    id bigint NOT NULL,
    key character varying(100) NOT NULL,
    description text,
    is_enabled boolean DEFAULT false NOT NULL,
    society_id bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.feature_flags OWNER TO postgres;

--
-- TOC entry 406 (class 1259 OID 24950)
-- Name: feature_flags_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.feature_flags ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.feature_flags_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 364 (class 1259 OID 17587)
-- Name: flat_statuses; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.flat_statuses (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL
);


ALTER TABLE public.flat_statuses OWNER TO postgres;

--
-- TOC entry 365 (class 1259 OID 17592)
-- Name: flat_statuses_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.flat_statuses ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.flat_statuses_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 366 (class 1259 OID 17593)
-- Name: flats; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.flats (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    flat_no text NOT NULL,
    owner_name text,
    contact_mobile text,
    contact_email text,
    tenant_name text,
    tenant_mobile text,
    tenant_email text,
    maintenance_amount numeric(13,2) DEFAULT 0.00 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    status_id smallint,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    area_sqft numeric(10,2),
    advance_balance numeric(13,2) DEFAULT 0 NOT NULL,
    opening_balance numeric(13,2) DEFAULT 0 NOT NULL
);


ALTER TABLE public.flats OWNER TO postgres;

--
-- TOC entry 367 (class 1259 OID 17605)
-- Name: flats_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.flats ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.flats_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 368 (class 1259 OID 17606)
-- Name: invoices; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.invoices (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id bigint NOT NULL,
    subscription_id uuid,
    invoice_number character varying(50) NOT NULL,
    invoice_type character varying(30) DEFAULT 'subscription'::character varying NOT NULL,
    amount numeric(10,2) NOT NULL,
    tax_amount numeric(10,2) DEFAULT 0.00,
    total_amount numeric(10,2) NOT NULL,
    currency character varying(3) DEFAULT 'INR'::character varying,
    status character varying(20) DEFAULT 'pending'::character varying NOT NULL,
    period_start date,
    period_end date,
    due_date date NOT NULL,
    paid_date timestamp with time zone,
    payment_method character varying(50),
    payment_reference character varying(255),
    description text,
    created_at timestamp with time zone DEFAULT now(),
    updated_at timestamp with time zone DEFAULT now(),
    society_id bigint NOT NULL,
    CONSTRAINT invoices_invoice_type_check CHECK (((invoice_type)::text = ANY (ARRAY[('subscription'::character varying)::text, ('renewal'::character varying)::text, ('addon'::character varying)::text, ('manual'::character varying)::text, ('penalty'::character varying)::text]))),
    CONSTRAINT invoices_status_check CHECK (((status)::text = ANY (ARRAY[('draft'::character varying)::text, ('pending'::character varying)::text, ('paid'::character varying)::text, ('failed'::character varying)::text, ('cancelled'::character varying)::text, ('refunded'::character varying)::text])))
);


ALTER TABLE public.invoices OWNER TO postgres;

--
-- TOC entry 369 (class 1259 OID 17620)
-- Name: jobs; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.jobs (
    id bigint NOT NULL,
    society_id bigint,
    job_type text NOT NULL,
    payload jsonb,
    status text DEFAULT 'queued'::text NOT NULL,
    result jsonb,
    attempts integer DEFAULT 0 NOT NULL,
    last_error text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL
);


ALTER TABLE public.jobs OWNER TO postgres;

--
-- TOC entry 370 (class 1259 OID 17630)
-- Name: jobs_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.jobs ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.jobs_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 371 (class 1259 OID 17631)
-- Name: maintenance_components; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_components (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    name text NOT NULL,
    component_type text NOT NULL,
    default_amount numeric(13,2),
    default_rate_per_sqft numeric(13,4),
    is_mandatory boolean DEFAULT true,
    is_deleted boolean DEFAULT false,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.maintenance_components OWNER TO postgres;

--
-- TOC entry 372 (class 1259 OID 17640)
-- Name: maintenance_components_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.maintenance_components_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.maintenance_components_id_seq OWNER TO postgres;

--
-- TOC entry 4426 (class 0 OID 0)
-- Dependencies: 372
-- Name: maintenance_components_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.maintenance_components_id_seq OWNED BY public.maintenance_components.id;


--
-- TOC entry 373 (class 1259 OID 17641)
-- Name: maintenance_config; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_config (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    default_monthly_charge numeric(13,2) DEFAULT 0 NOT NULL,
    due_day_of_month integer DEFAULT 1 NOT NULL,
    late_fee_per_month numeric(13,2) DEFAULT 0 NOT NULL,
    grace_period_days integer DEFAULT 0 NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by bigint,
    updated_by bigint,
    CONSTRAINT maintenance_config_due_day_of_month_check CHECK (((due_day_of_month >= 1) AND (due_day_of_month <= 28)))
);


ALTER TABLE public.maintenance_config OWNER TO postgres;

--
-- TOC entry 374 (class 1259 OID 17652)
-- Name: maintenance_config_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.maintenance_config ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.maintenance_config_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 375 (class 1259 OID 17653)
-- Name: maintenance_cycles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_cycles (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL,
    description text,
    is_active boolean DEFAULT true NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.maintenance_cycles OWNER TO postgres;

--
-- TOC entry 376 (class 1259 OID 17660)
-- Name: maintenance_cycles_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.maintenance_cycles ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.maintenance_cycles_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 377 (class 1259 OID 17661)
-- Name: maintenance_payments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_payments (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    flat_id bigint NOT NULL,
    bill_id bigint,
    amount numeric(13,2) NOT NULL,
    payment_date timestamp with time zone NOT NULL,
    payment_mode_id smallint NOT NULL,
    reference_number text,
    receipt_url text,
    notes text,
    recorded_by bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    idempotency_key text,
    adjustment_id bigint,
    outstanding_after_payment numeric(13,2),
    CONSTRAINT chk_bill_id_valid CHECK (((bill_id IS NULL) OR (bill_id > 0))),
    CONSTRAINT chk_only_one_target CHECK ((((bill_id IS NOT NULL) AND (adjustment_id IS NULL)) OR ((bill_id IS NULL) AND (adjustment_id IS NOT NULL)) OR ((bill_id IS NULL) AND (adjustment_id IS NULL)))),
    CONSTRAINT chk_payment_positive CHECK ((amount > (0)::numeric))
);


ALTER TABLE public.maintenance_payments OWNER TO postgres;

--
-- TOC entry 378 (class 1259 OID 17672)
-- Name: maintenance_payments_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.maintenance_payments ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.maintenance_payments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 379 (class 1259 OID 17673)
-- Name: maintenance_plans; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_plans (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    name text NOT NULL,
    calculation_type text NOT NULL,
    fixed_amount numeric(13,2) DEFAULT 0,
    rate_per_sqft numeric(13,4) DEFAULT 0,
    effective_from date NOT NULL,
    effective_to date,
    is_active boolean DEFAULT true,
    is_deleted boolean DEFAULT false,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.maintenance_plans OWNER TO postgres;

--
-- TOC entry 380 (class 1259 OID 17684)
-- Name: maintenance_plans_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.maintenance_plans_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.maintenance_plans_id_seq OWNER TO postgres;

--
-- TOC entry 4435 (class 0 OID 0)
-- Dependencies: 380
-- Name: maintenance_plans_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.maintenance_plans_id_seq OWNED BY public.maintenance_plans.id;


--
-- TOC entry 381 (class 1259 OID 17685)
-- Name: maintenance_rate_history; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.maintenance_rate_history (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    maintenance_plan_id bigint NOT NULL,
    old_fixed_amount numeric(13,2),
    old_rate_per_sqft numeric(13,4),
    changed_by bigint,
    changed_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.maintenance_rate_history OWNER TO postgres;

--
-- TOC entry 382 (class 1259 OID 17690)
-- Name: maintenance_rate_history_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.maintenance_rate_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.maintenance_rate_history_id_seq OWNER TO postgres;

--
-- TOC entry 4438 (class 0 OID 0)
-- Dependencies: 382
-- Name: maintenance_rate_history_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.maintenance_rate_history_id_seq OWNED BY public.maintenance_rate_history.id;


--
-- TOC entry 383 (class 1259 OID 17691)
-- Name: notification_preferences; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.notification_preferences (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id bigint NOT NULL,
    payment_reminders boolean DEFAULT true NOT NULL,
    bill_generated boolean DEFAULT true NOT NULL,
    expense_updates boolean DEFAULT true NOT NULL,
    monthly_reports boolean DEFAULT true NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.notification_preferences OWNER TO postgres;

--
-- TOC entry 384 (class 1259 OID 17700)
-- Name: notification_preferences_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.notification_preferences ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.notification_preferences_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 420 (class 1259 OID 42300)
-- Name: password_reset_tokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.password_reset_tokens (
    id bigint NOT NULL,
    user_id bigint NOT NULL,
    token_hash text NOT NULL,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by_ip text,
    is_used boolean DEFAULT false NOT NULL,
    used_at timestamp with time zone
);


ALTER TABLE public.password_reset_tokens OWNER TO postgres;

--
-- TOC entry 419 (class 1259 OID 42299)
-- Name: password_reset_tokens_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.password_reset_tokens ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.password_reset_tokens_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 385 (class 1259 OID 17701)
-- Name: payment_modes; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.payment_modes (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL
);


ALTER TABLE public.payment_modes OWNER TO postgres;

--
-- TOC entry 386 (class 1259 OID 17706)
-- Name: payment_modes_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.payment_modes ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.payment_modes_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 387 (class 1259 OID 17707)
-- Name: payments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.payments (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    bill_id bigint,
    flat_id bigint,
    amount numeric(13,2) NOT NULL,
    date_paid timestamp with time zone,
    mode_code text,
    reference text,
    receipt_url text,
    recorded_by bigint,
    idempotency_key text,
    reversed_by_payment_id bigint,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    razorpay_order_id text,
    razorpay_payment_id text,
    razorpay_signature text,
    payment_type text,
    verified_at timestamp with time zone,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    CONSTRAINT chk_payments_payment_type CHECK ((payment_type = ANY (ARRAY['bill'::text, 'subscription'::text])))
);


ALTER TABLE public.payments OWNER TO postgres;

--
-- TOC entry 388 (class 1259 OID 17716)
-- Name: payments_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.payments ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.payments_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 389 (class 1259 OID 17717)
-- Name: plan_components; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.plan_components (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    maintenance_plan_id bigint NOT NULL,
    maintenance_component_id bigint NOT NULL,
    amount numeric(13,2),
    rate_per_sqft numeric(13,4),
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.plan_components OWNER TO postgres;

--
-- TOC entry 390 (class 1259 OID 17722)
-- Name: plan_components_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.plan_components_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.plan_components_id_seq OWNER TO postgres;

--
-- TOC entry 4449 (class 0 OID 0)
-- Dependencies: 390
-- Name: plan_components_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.plan_components_id_seq OWNED BY public.plan_components.id;


--
-- TOC entry 414 (class 1259 OID 39515)
-- Name: plan_price_history; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.plan_price_history (
    id bigint NOT NULL,
    plan_id uuid NOT NULL,
    price numeric NOT NULL,
    effective_from timestamp with time zone DEFAULT now() NOT NULL,
    effective_to timestamp with time zone,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.plan_price_history OWNER TO postgres;

--
-- TOC entry 413 (class 1259 OID 39514)
-- Name: plan_price_history_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.plan_price_history ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.plan_price_history_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 391 (class 1259 OID 17723)
-- Name: plans; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.plans (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    name character varying(100) NOT NULL,
    price numeric(10,2) NOT NULL,
    currency character varying(3) DEFAULT 'INR'::character varying NOT NULL,
    is_active boolean DEFAULT true,
    created_at timestamp with time zone DEFAULT now(),
    duration_months integer DEFAULT 1 NOT NULL,
    max_flats integer NOT NULL,
    display_order integer DEFAULT 0,
    is_popular boolean DEFAULT false,
    description text,
    discount_percentage integer,
    plan_group character varying(50),
    updated_at timestamp with time zone DEFAULT now(),
    monthly_amount numeric DEFAULT 0 NOT NULL,
    CONSTRAINT check_duration_valid CHECK ((duration_months = ANY (ARRAY[1, 12]))),
    CONSTRAINT check_max_flats_positive CHECK ((max_flats > 0)),
    CONSTRAINT check_price_positive CHECK ((price > (0)::numeric))
);


ALTER TABLE public.plans OWNER TO postgres;

--
-- TOC entry 409 (class 1259 OID 24969)
-- Name: platform_settings; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.platform_settings (
    id bigint NOT NULL,
    key character varying(100) NOT NULL,
    value text,
    description text,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL
);


ALTER TABLE public.platform_settings OWNER TO postgres;

--
-- TOC entry 408 (class 1259 OID 24968)
-- Name: platform_settings_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.platform_settings ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.platform_settings_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 392 (class 1259 OID 17731)
-- Name: refresh_tokens; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.refresh_tokens (
    id bigint NOT NULL,
    user_id bigint NOT NULL,
    token_hash text NOT NULL,
    jwt_id text,
    expires_at timestamp with time zone NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    created_by_ip text,
    is_revoked boolean DEFAULT false NOT NULL,
    revoked_at timestamp with time zone,
    replaced_by_token_hash text
);


ALTER TABLE public.refresh_tokens OWNER TO postgres;

--
-- TOC entry 393 (class 1259 OID 17738)
-- Name: refresh_tokens_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.refresh_tokens ALTER COLUMN id ADD GENERATED BY DEFAULT AS IDENTITY (
    SEQUENCE NAME public.refresh_tokens_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 394 (class 1259 OID 17739)
-- Name: roles; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.roles (
    id smallint NOT NULL,
    code text NOT NULL,
    display_name text NOT NULL
);


ALTER TABLE public.roles OWNER TO postgres;

--
-- TOC entry 395 (class 1259 OID 17744)
-- Name: roles_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.roles ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.roles_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 396 (class 1259 OID 17745)
-- Name: societies; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.societies (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    name text NOT NULL,
    address text,
    city text,
    state text,
    pincode text,
    currency text DEFAULT 'INR'::text NOT NULL,
    default_maintenance_cycle text DEFAULT 'monthly'::text NOT NULL,
    billing_plan_id integer,
    settings jsonb DEFAULT '{}'::jsonb,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    maintenance_cycle_id smallint,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    onboarding_date date DEFAULT CURRENT_DATE NOT NULL,
    subscription_id uuid
);


ALTER TABLE public.societies OWNER TO postgres;

--
-- TOC entry 397 (class 1259 OID 17758)
-- Name: societies_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.societies ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.societies_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 398 (class 1259 OID 17759)
-- Name: society_fund_ledger; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.society_fund_ledger (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    amount numeric(13,2) NOT NULL,
    entry_type text NOT NULL,
    reference text,
    notes text,
    created_by bigint NOT NULL,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    is_deleted boolean DEFAULT false,
    transaction_date date DEFAULT CURRENT_DATE
);


ALTER TABLE public.society_fund_ledger OWNER TO postgres;

--
-- TOC entry 399 (class 1259 OID 17768)
-- Name: society_fund_ledger_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

CREATE SEQUENCE public.society_fund_ledger_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1;


ALTER SEQUENCE public.society_fund_ledger_id_seq OWNER TO postgres;

--
-- TOC entry 4463 (class 0 OID 0)
-- Dependencies: 399
-- Name: society_fund_ledger_id_seq; Type: SEQUENCE OWNED BY; Schema: public; Owner: postgres
--

ALTER SEQUENCE public.society_fund_ledger_id_seq OWNED BY public.society_fund_ledger.id;


--
-- TOC entry 400 (class 1259 OID 17769)
-- Name: subscription_events; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.subscription_events (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id bigint NOT NULL,
    subscription_id uuid,
    event_type character varying(50) NOT NULL,
    old_status character varying(20),
    new_status character varying(20),
    amount numeric(10,2),
    metadata text,
    created_at timestamp with time zone DEFAULT now(),
    society_id bigint NOT NULL
);


ALTER TABLE public.subscription_events OWNER TO postgres;

--
-- TOC entry 416 (class 1259 OID 39543)
-- Name: subscription_payments; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.subscription_payments (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    subscription_id uuid NOT NULL,
    amount numeric NOT NULL,
    payment_date timestamp with time zone DEFAULT now(),
    payment_gateway text,
    gateway_payment_id text,
    status text,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.subscription_payments OWNER TO postgres;

--
-- TOC entry 415 (class 1259 OID 39529)
-- Name: subscription_snapshots; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.subscription_snapshots (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    subscription_id uuid NOT NULL,
    plan_name text,
    max_flats integer,
    duration_months integer,
    created_at timestamp with time zone DEFAULT now()
);


ALTER TABLE public.subscription_snapshots OWNER TO postgres;

--
-- TOC entry 401 (class 1259 OID 17776)
-- Name: subscriptions; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.subscriptions (
    id uuid DEFAULT gen_random_uuid() NOT NULL,
    user_id bigint NOT NULL,
    plan_id uuid NOT NULL,
    status character varying(20) DEFAULT 'trial'::character varying NOT NULL,
    subscribed_amount numeric(10,2) NOT NULL,
    currency character varying(3) DEFAULT 'INR'::character varying,
    current_period_start timestamp with time zone,
    current_period_end timestamp with time zone,
    trial_start timestamp with time zone DEFAULT now(),
    trial_end timestamp with time zone DEFAULT (now() + '30 days'::interval),
    cancelled_at timestamp with time zone,
    created_at timestamp with time zone DEFAULT now(),
    updated_at timestamp with time zone DEFAULT now(),
    society_id bigint NOT NULL,
    CONSTRAINT subscriptions_status_check CHECK (((status)::text = ANY (ARRAY[('trial'::character varying)::text, ('active'::character varying)::text, ('expired'::character varying)::text, ('past_due'::character varying)::text, ('cancelled'::character varying)::text])))
);


ALTER TABLE public.subscriptions OWNER TO postgres;

--
-- TOC entry 402 (class 1259 OID 17787)
-- Name: users; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.users (
    id bigint NOT NULL,
    public_id uuid DEFAULT gen_random_uuid() NOT NULL,
    society_id bigint NOT NULL,
    name text NOT NULL,
    email text,
    mobile text,
    role_id smallint NOT NULL,
    password_hash text,
    is_active boolean DEFAULT true NOT NULL,
    last_login timestamp with time zone,
    created_at timestamp with time zone DEFAULT now() NOT NULL,
    updated_at timestamp with time zone DEFAULT now() NOT NULL,
    force_password_change boolean DEFAULT false NOT NULL,
    created_by uuid,
    updated_by uuid,
    trial_start_date timestamp without time zone,
    subscription_status character varying(20) DEFAULT 'trial'::character varying,
    subscription_start_date timestamp without time zone,
    next_billing_date timestamp without time zone,
    trial_ends_date timestamp with time zone DEFAULT (now() + '30 days'::interval),
    last_payment_date timestamp with time zone,
    is_deleted boolean DEFAULT false NOT NULL,
    deleted_at timestamp with time zone,
    username character varying(100),
    password_reset_token_hash character varying(255),
    password_reset_expires_at timestamp without time zone,
    monthly_amount numeric DEFAULT 299.00,
    subscription_plan character varying DEFAULT 'pro'::character varying
);


ALTER TABLE public.users OWNER TO postgres;

--
-- TOC entry 403 (class 1259 OID 17802)
-- Name: users_id_seq; Type: SEQUENCE; Schema: public; Owner: postgres
--

ALTER TABLE public.users ALTER COLUMN id ADD GENERATED ALWAYS AS IDENTITY (
    SEQUENCE NAME public.users_id_seq
    START WITH 1
    INCREMENT BY 1
    NO MINVALUE
    NO MAXVALUE
    CACHE 1
);


--
-- TOC entry 412 (class 1259 OID 32099)
-- Name: v_opening_bal; Type: TABLE; Schema: public; Owner: postgres
--

CREATE TABLE public.v_opening_bal (
    "coalesce" numeric
);


ALTER TABLE public.v_opening_bal OWNER TO postgres;

--
-- TOC entry 3828 (class 2604 OID 17803)
-- Name: bill_items id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_items ALTER COLUMN id SET DEFAULT nextval('public.bill_items_id_seq'::regclass);


--
-- TOC entry 3831 (class 2604 OID 17804)
-- Name: bill_payment_allocations id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_payment_allocations ALTER COLUMN id SET DEFAULT nextval('public.bill_payment_allocations_id_seq'::regclass);


--
-- TOC entry 3864 (class 2604 OID 17805)
-- Name: maintenance_components id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_components ALTER COLUMN id SET DEFAULT nextval('public.maintenance_components_id_seq'::regclass);


--
-- TOC entry 3881 (class 2604 OID 17806)
-- Name: maintenance_plans id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_plans ALTER COLUMN id SET DEFAULT nextval('public.maintenance_plans_id_seq'::regclass);


--
-- TOC entry 3888 (class 2604 OID 17807)
-- Name: maintenance_rate_history id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_rate_history ALTER COLUMN id SET DEFAULT nextval('public.maintenance_rate_history_id_seq'::regclass);


--
-- TOC entry 3900 (class 2604 OID 17808)
-- Name: plan_components id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_components ALTER COLUMN id SET DEFAULT nextval('public.plan_components_id_seq'::regclass);


--
-- TOC entry 3922 (class 2604 OID 17809)
-- Name: society_fund_ledger id; Type: DEFAULT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.society_fund_ledger ALTER COLUMN id SET DEFAULT nextval('public.society_fund_ledger_id_seq'::regclass);


--
-- TOC entry 3983 (class 2606 OID 17811)
-- Name: adjustments adjustments_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.adjustments
    ADD CONSTRAINT adjustments_pkey PRIMARY KEY (id);


--
-- TOC entry 4142 (class 2606 OID 24947)
-- Name: admin_users admin_users_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.admin_users
    ADD CONSTRAINT admin_users_pkey PRIMARY KEY (id);


--
-- TOC entry 3991 (class 2606 OID 17813)
-- Name: attachments attachments_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT attachments_pkey PRIMARY KEY (id);


--
-- TOC entry 3995 (class 2606 OID 17815)
-- Name: audit_logs audit_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.audit_logs
    ADD CONSTRAINT audit_logs_pkey PRIMARY KEY (id);


--
-- TOC entry 3999 (class 2606 OID 17817)
-- Name: bill_items bill_items_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_items
    ADD CONSTRAINT bill_items_pkey PRIMARY KEY (id);


--
-- TOC entry 4001 (class 2606 OID 17819)
-- Name: bill_items bill_items_public_id_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_items
    ADD CONSTRAINT bill_items_public_id_key UNIQUE (public_id);


--
-- TOC entry 4004 (class 2606 OID 17821)
-- Name: bill_payment_allocations bill_payment_allocations_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_payment_allocations
    ADD CONSTRAINT bill_payment_allocations_pkey PRIMARY KEY (id);


--
-- TOC entry 4008 (class 2606 OID 17823)
-- Name: bill_statuses bill_statuses_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_statuses
    ADD CONSTRAINT bill_statuses_pkey PRIMARY KEY (id);


--
-- TOC entry 4010 (class 2606 OID 17825)
-- Name: bills bills_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bills
    ADD CONSTRAINT bills_pkey PRIMARY KEY (id);


--
-- TOC entry 4159 (class 2606 OID 42285)
-- Name: email_notification_logs email_notification_logs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.email_notification_logs
    ADD CONSTRAINT email_notification_logs_pkey PRIMARY KEY (id);


--
-- TOC entry 4022 (class 2606 OID 17827)
-- Name: expense_categories expense_categories_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.expense_categories
    ADD CONSTRAINT expense_categories_pkey PRIMARY KEY (id);


--
-- TOC entry 4024 (class 2606 OID 17829)
-- Name: expenses expenses_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT expenses_pkey PRIMARY KEY (id);


--
-- TOC entry 4146 (class 2606 OID 24960)
-- Name: feature_flags feature_flags_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feature_flags
    ADD CONSTRAINT feature_flags_pkey PRIMARY KEY (id);


--
-- TOC entry 4030 (class 2606 OID 17831)
-- Name: flat_statuses flat_statuses_code_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.flat_statuses
    ADD CONSTRAINT flat_statuses_code_key UNIQUE (code);


--
-- TOC entry 4032 (class 2606 OID 17833)
-- Name: flat_statuses flat_statuses_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.flat_statuses
    ADD CONSTRAINT flat_statuses_pkey PRIMARY KEY (id);


--
-- TOC entry 4034 (class 2606 OID 17835)
-- Name: flats flats_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.flats
    ADD CONSTRAINT flats_pkey PRIMARY KEY (id);


--
-- TOC entry 4044 (class 2606 OID 17841)
-- Name: invoices invoices_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_pkey PRIMARY KEY (id);


--
-- TOC entry 4049 (class 2606 OID 17843)
-- Name: jobs jobs_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.jobs
    ADD CONSTRAINT jobs_pkey PRIMARY KEY (id);


--
-- TOC entry 4053 (class 2606 OID 17845)
-- Name: maintenance_components maintenance_components_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_components
    ADD CONSTRAINT maintenance_components_pkey PRIMARY KEY (id);


--
-- TOC entry 4055 (class 2606 OID 17847)
-- Name: maintenance_components maintenance_components_public_id_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_components
    ADD CONSTRAINT maintenance_components_public_id_key UNIQUE (public_id);


--
-- TOC entry 4057 (class 2606 OID 17849)
-- Name: maintenance_config maintenance_config_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_config
    ADD CONSTRAINT maintenance_config_pkey PRIMARY KEY (id);


--
-- TOC entry 4061 (class 2606 OID 17851)
-- Name: maintenance_cycles maintenance_cycles_code_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_cycles
    ADD CONSTRAINT maintenance_cycles_code_key UNIQUE (code);


--
-- TOC entry 4063 (class 2606 OID 17853)
-- Name: maintenance_cycles maintenance_cycles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_cycles
    ADD CONSTRAINT maintenance_cycles_pkey PRIMARY KEY (id);


--
-- TOC entry 4071 (class 2606 OID 17855)
-- Name: maintenance_payments maintenance_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT maintenance_payments_pkey PRIMARY KEY (id);


--
-- TOC entry 4075 (class 2606 OID 17857)
-- Name: maintenance_plans maintenance_plans_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_plans
    ADD CONSTRAINT maintenance_plans_pkey PRIMARY KEY (id);


--
-- TOC entry 4077 (class 2606 OID 17859)
-- Name: maintenance_plans maintenance_plans_public_id_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_plans
    ADD CONSTRAINT maintenance_plans_public_id_key UNIQUE (public_id);


--
-- TOC entry 4079 (class 2606 OID 17861)
-- Name: maintenance_rate_history maintenance_rate_history_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_rate_history
    ADD CONSTRAINT maintenance_rate_history_pkey PRIMARY KEY (id);


--
-- TOC entry 4081 (class 2606 OID 17863)
-- Name: maintenance_rate_history maintenance_rate_history_public_id_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_rate_history
    ADD CONSTRAINT maintenance_rate_history_public_id_key UNIQUE (public_id);


--
-- TOC entry 4083 (class 2606 OID 17865)
-- Name: notification_preferences notification_preferences_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.notification_preferences
    ADD CONSTRAINT notification_preferences_pkey PRIMARY KEY (id);


--
-- TOC entry 4161 (class 2606 OID 42308)
-- Name: password_reset_tokens password_reset_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_pkey PRIMARY KEY (id);


--
-- TOC entry 4087 (class 2606 OID 17867)
-- Name: payment_modes payment_modes_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payment_modes
    ADD CONSTRAINT payment_modes_pkey PRIMARY KEY (id);


--
-- TOC entry 4094 (class 2606 OID 17869)
-- Name: payments payments_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_pkey PRIMARY KEY (id);


--
-- TOC entry 4099 (class 2606 OID 17871)
-- Name: plan_components plan_components_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_components
    ADD CONSTRAINT plan_components_pkey PRIMARY KEY (id);


--
-- TOC entry 4101 (class 2606 OID 17873)
-- Name: plan_components plan_components_public_id_key; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_components
    ADD CONSTRAINT plan_components_public_id_key UNIQUE (public_id);


--
-- TOC entry 4153 (class 2606 OID 39523)
-- Name: plan_price_history plan_price_history_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_price_history
    ADD CONSTRAINT plan_price_history_pkey PRIMARY KEY (id);


--
-- TOC entry 4104 (class 2606 OID 17875)
-- Name: plans plans_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plans
    ADD CONSTRAINT plans_pkey PRIMARY KEY (id);


--
-- TOC entry 4150 (class 2606 OID 24977)
-- Name: platform_settings platform_settings_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.platform_settings
    ADD CONSTRAINT platform_settings_pkey PRIMARY KEY (id);


--
-- TOC entry 4110 (class 2606 OID 17877)
-- Name: refresh_tokens refresh_tokens_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id);


--
-- TOC entry 4115 (class 2606 OID 17879)
-- Name: roles roles_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.roles
    ADD CONSTRAINT roles_pkey PRIMARY KEY (id);


--
-- TOC entry 4118 (class 2606 OID 17881)
-- Name: societies societies_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.societies
    ADD CONSTRAINT societies_pkey PRIMARY KEY (id);


--
-- TOC entry 4123 (class 2606 OID 17883)
-- Name: society_fund_ledger society_fund_ledger_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.society_fund_ledger
    ADD CONSTRAINT society_fund_ledger_pkey PRIMARY KEY (id);


--
-- TOC entry 4126 (class 2606 OID 17885)
-- Name: subscription_events subscription_events_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_events
    ADD CONSTRAINT subscription_events_pkey PRIMARY KEY (id);


--
-- TOC entry 4157 (class 2606 OID 39552)
-- Name: subscription_payments subscription_payments_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_payments
    ADD CONSTRAINT subscription_payments_pkey PRIMARY KEY (id);


--
-- TOC entry 4155 (class 2606 OID 39537)
-- Name: subscription_snapshots subscription_snapshots_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_snapshots
    ADD CONSTRAINT subscription_snapshots_pkey PRIMARY KEY (id);


--
-- TOC entry 4133 (class 2606 OID 17887)
-- Name: subscriptions subscriptions_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_pkey PRIMARY KEY (id);


--
-- TOC entry 4107 (class 2606 OID 38059)
-- Name: plans unique_plan_name; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plans
    ADD CONSTRAINT unique_plan_name UNIQUE (name);


--
-- TOC entry 4138 (class 2606 OID 17889)
-- Name: users users_pkey; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_pkey PRIMARY KEY (id);


--
-- TOC entry 4112 (class 2606 OID 17893)
-- Name: refresh_tokens ux_refresh_token_user_tokenhash; Type: CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT ux_refresh_token_user_tokenhash UNIQUE (user_id, token_hash);


--
-- TOC entry 4035 (class 1259 OID 42175)
-- Name: flats_society_id_flat_no_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX flats_society_id_flat_no_key ON public.flats USING btree (society_id, flat_no) WHERE (is_deleted = false);


--
-- TOC entry 3984 (class 1259 OID 41488)
-- Name: idx_adj_flat_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_adj_flat_date ON public.adjustments USING btree (flat_id, created_at);


--
-- TOC entry 3985 (class 1259 OID 17894)
-- Name: idx_adjustments_active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_adjustments_active ON public.adjustments USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 3986 (class 1259 OID 17895)
-- Name: idx_adjustments_fifo; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_adjustments_fifo ON public.adjustments USING btree (flat_id, society_id, entry_type, remaining_amount) WHERE ((remaining_amount > (0)::numeric) AND (is_deleted = false));


--
-- TOC entry 3987 (class 1259 OID 17896)
-- Name: idx_adjustments_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_adjustments_society ON public.adjustments USING btree (society_id);


--
-- TOC entry 3988 (class 1259 OID 17897)
-- Name: idx_adjustments_society_type_period; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_adjustments_society_type_period ON public.adjustments USING btree (society_id, entry_type, period);


--
-- TOC entry 4005 (class 1259 OID 17898)
-- Name: idx_allocations_bill; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_allocations_bill ON public.bill_payment_allocations USING btree (bill_id);


--
-- TOC entry 4006 (class 1259 OID 17899)
-- Name: idx_allocations_payment; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_allocations_payment ON public.bill_payment_allocations USING btree (payment_id);


--
-- TOC entry 3992 (class 1259 OID 17900)
-- Name: idx_attachments_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_attachments_society ON public.attachments USING btree (society_id);


--
-- TOC entry 3996 (class 1259 OID 17901)
-- Name: idx_audit_logs_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_audit_logs_society ON public.audit_logs USING btree (society_id);


--
-- TOC entry 3997 (class 1259 OID 17902)
-- Name: idx_audit_logs_table; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_audit_logs_table ON public.audit_logs USING btree (table_name);


--
-- TOC entry 4002 (class 1259 OID 17903)
-- Name: idx_bill_items_bill; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bill_items_bill ON public.bill_items USING btree (bill_id);


--
-- TOC entry 4011 (class 1259 OID 17904)
-- Name: idx_bills_active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_active ON public.bills USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 4012 (class 1259 OID 17905)
-- Name: idx_bills_flat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_flat ON public.bills USING btree (flat_id);


--
-- TOC entry 4013 (class 1259 OID 41485)
-- Name: idx_bills_flat_period; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_flat_period ON public.bills USING btree (flat_id, period);


--
-- TOC entry 4014 (class 1259 OID 17906)
-- Name: idx_bills_society_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_society_date ON public.bills USING btree (society_id, created_at);


--
-- TOC entry 4015 (class 1259 OID 17907)
-- Name: idx_bills_society_period; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_society_period ON public.bills USING btree (society_id, period);


--
-- TOC entry 4016 (class 1259 OID 41486)
-- Name: idx_bills_society_period_deleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_society_period_deleted ON public.bills USING btree (society_id, period, is_deleted);


--
-- TOC entry 4017 (class 1259 OID 17908)
-- Name: idx_bills_society_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_society_status ON public.bills USING btree (society_id, status_code);


--
-- TOC entry 4018 (class 1259 OID 17909)
-- Name: idx_bills_unpaid_lookup; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_bills_unpaid_lookup ON public.bills USING btree (flat_id, society_id, status_code, period) WHERE (is_deleted = false);


--
-- TOC entry 4025 (class 1259 OID 17910)
-- Name: idx_expenses_active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_expenses_active ON public.expenses USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 4026 (class 1259 OID 17911)
-- Name: idx_expenses_society_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_expenses_society_date ON public.expenses USING btree (society_id, created_at);


--
-- TOC entry 4027 (class 1259 OID 17912)
-- Name: idx_expenses_society_month; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_expenses_society_month ON public.expenses USING btree (society_id, date_incurred);


--
-- TOC entry 4147 (class 1259 OID 24967)
-- Name: idx_feature_flags_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_feature_flags_society ON public.feature_flags USING btree (society_id) WHERE (society_id IS NOT NULL);


--
-- TOC entry 4036 (class 1259 OID 17913)
-- Name: idx_flats_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_flats_society_id ON public.flats USING btree (society_id);


--
-- TOC entry 4038 (class 1259 OID 17914)
-- Name: idx_invoices_due_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_invoices_due_date ON public.invoices USING btree (due_date);


--
-- TOC entry 4039 (class 1259 OID 17915)
-- Name: idx_invoices_invoice_number; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_invoices_invoice_number ON public.invoices USING btree (invoice_number);


--
-- TOC entry 4040 (class 1259 OID 39561)
-- Name: idx_invoices_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_invoices_society_id ON public.invoices USING btree (society_id);


--
-- TOC entry 4041 (class 1259 OID 17916)
-- Name: idx_invoices_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_invoices_status ON public.invoices USING btree (status);


--
-- TOC entry 4042 (class 1259 OID 17917)
-- Name: idx_invoices_user_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_invoices_user_id ON public.invoices USING btree (user_id);


--
-- TOC entry 4046 (class 1259 OID 17918)
-- Name: idx_jobs_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_jobs_society ON public.jobs USING btree (society_id);


--
-- TOC entry 4047 (class 1259 OID 17919)
-- Name: idx_jobs_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_jobs_status ON public.jobs USING btree (status);


--
-- TOC entry 4064 (class 1259 OID 17920)
-- Name: idx_maintenance_bill; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_bill ON public.maintenance_payments USING btree (bill_id);


--
-- TOC entry 4051 (class 1259 OID 17921)
-- Name: idx_maintenance_components_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_components_society ON public.maintenance_components USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 4065 (class 1259 OID 17922)
-- Name: idx_maintenance_flat; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_flat ON public.maintenance_payments USING btree (flat_id);


--
-- TOC entry 4066 (class 1259 OID 17923)
-- Name: idx_maintenance_payment_mode; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_payment_mode ON public.maintenance_payments USING btree (payment_mode_id);


--
-- TOC entry 4073 (class 1259 OID 17924)
-- Name: idx_maintenance_plans_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_plans_society ON public.maintenance_plans USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 4067 (class 1259 OID 17925)
-- Name: idx_maintenance_society_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_maintenance_society_date ON public.maintenance_payments USING btree (society_id, payment_date);


--
-- TOC entry 4068 (class 1259 OID 41487)
-- Name: idx_mp_flat_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_mp_flat_date ON public.maintenance_payments USING btree (flat_id, payment_date);


--
-- TOC entry 4069 (class 1259 OID 17926)
-- Name: idx_mp_society_date_not_deleted; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_mp_society_date_not_deleted ON public.maintenance_payments USING btree (society_id, payment_date DESC) WHERE (NOT is_deleted);


--
-- TOC entry 4088 (class 1259 OID 17927)
-- Name: idx_payments_active; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_payments_active ON public.payments USING btree (society_id) WHERE (is_deleted = false);


--
-- TOC entry 4089 (class 1259 OID 17928)
-- Name: idx_payments_bill_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_payments_bill_id ON public.payments USING btree (bill_id);


--
-- TOC entry 4090 (class 1259 OID 17929)
-- Name: idx_payments_idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX idx_payments_idempotency ON public.payments USING btree (society_id, idempotency_key) WHERE (idempotency_key IS NOT NULL);


--
-- TOC entry 4091 (class 1259 OID 17930)
-- Name: idx_payments_razorpay_order; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_payments_razorpay_order ON public.payments USING btree (razorpay_order_id);


--
-- TOC entry 4092 (class 1259 OID 17931)
-- Name: idx_payments_society_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_payments_society_date ON public.payments USING btree (society_id, date_paid);


--
-- TOC entry 4097 (class 1259 OID 17932)
-- Name: idx_plan_components_plan; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_plan_components_plan ON public.plan_components USING btree (maintenance_plan_id);


--
-- TOC entry 4102 (class 1259 OID 38063)
-- Name: idx_plans_lookup; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_plans_lookup ON public.plans USING btree (max_flats, duration_months);


--
-- TOC entry 4116 (class 1259 OID 17933)
-- Name: idx_societies_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_societies_public_id ON public.societies USING btree (public_id);


--
-- TOC entry 4120 (class 1259 OID 42215)
-- Name: idx_society_fund_ledger_society_date; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_society_fund_ledger_society_date ON public.society_fund_ledger USING btree (society_id, transaction_date DESC) WHERE (is_deleted IS DISTINCT FROM true);


--
-- TOC entry 4121 (class 1259 OID 42214)
-- Name: idx_society_fund_ledger_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_society_fund_ledger_society_id ON public.society_fund_ledger USING btree (society_id) WHERE (is_deleted IS DISTINCT FROM true);


--
-- TOC entry 4127 (class 1259 OID 39560)
-- Name: idx_subscriptions_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_subscriptions_society_id ON public.subscriptions USING btree (society_id);


--
-- TOC entry 4128 (class 1259 OID 17934)
-- Name: idx_subscriptions_status; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_subscriptions_status ON public.subscriptions USING btree (status);


--
-- TOC entry 4129 (class 1259 OID 17935)
-- Name: idx_subscriptions_trial_end; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_subscriptions_trial_end ON public.subscriptions USING btree (trial_end);


--
-- TOC entry 4130 (class 1259 OID 17936)
-- Name: idx_subscriptions_user_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_subscriptions_user_id ON public.subscriptions USING btree (user_id);


--
-- TOC entry 4134 (class 1259 OID 41484)
-- Name: idx_users_email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_users_email ON public.users USING btree (email);


--
-- TOC entry 4135 (class 1259 OID 39893)
-- Name: idx_users_password_reset_token_hash; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_users_password_reset_token_hash ON public.users USING btree (password_reset_token_hash) WHERE (password_reset_token_hash IS NOT NULL);


--
-- TOC entry 4136 (class 1259 OID 17937)
-- Name: idx_users_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE INDEX idx_users_society_id ON public.users USING btree (society_id);


--
-- TOC entry 4045 (class 1259 OID 42176)
-- Name: invoices_user_invoice_number_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX invoices_user_invoice_number_key ON public.invoices USING btree (user_id, invoice_number);


--
-- TOC entry 4131 (class 1259 OID 39558)
-- Name: one_active_subscription_per_society; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX one_active_subscription_per_society ON public.subscriptions USING btree (society_id) WHERE ((status)::text = ANY ((ARRAY['active'::character varying, 'trial'::character varying])::text[]));


--
-- TOC entry 4113 (class 1259 OID 17938)
-- Name: roles_code_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX roles_code_key ON public.roles USING btree (code);


--
-- TOC entry 4105 (class 1259 OID 39559)
-- Name: unique_plan_group_duration; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX unique_plan_group_duration ON public.plans USING btree (plan_group, duration_months) WHERE (is_active = true);


--
-- TOC entry 4124 (class 1259 OID 17939)
-- Name: uq_society_single_opening; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX uq_society_single_opening ON public.society_fund_ledger USING btree (society_id) WHERE ((entry_type = 'opening_fund'::text) AND (COALESCE(is_deleted, false) = false));


--
-- TOC entry 4139 (class 1259 OID 42174)
-- Name: users_society_username_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX users_society_username_key ON public.users USING btree (society_id, username) WHERE ((username IS NOT NULL) AND (is_deleted = false));


--
-- TOC entry 3989 (class 1259 OID 17940)
-- Name: ux_adjustments_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_adjustments_public_id ON public.adjustments USING btree (public_id);


--
-- TOC entry 4143 (class 1259 OID 24948)
-- Name: ux_admin_users_email; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_admin_users_email ON public.admin_users USING btree (email);


--
-- TOC entry 4144 (class 1259 OID 24949)
-- Name: ux_admin_users_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_admin_users_public_id ON public.admin_users USING btree (public_id);


--
-- TOC entry 3993 (class 1259 OID 17941)
-- Name: ux_attachments_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_attachments_public_id ON public.attachments USING btree (public_id);


--
-- TOC entry 4019 (class 1259 OID 17942)
-- Name: ux_bill_unique_period; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_bill_unique_period ON public.bills USING btree (society_id, flat_id, period) WHERE (is_deleted = false);


--
-- TOC entry 4020 (class 1259 OID 17943)
-- Name: ux_bills_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_bills_public_id ON public.bills USING btree (public_id);


--
-- TOC entry 4028 (class 1259 OID 17944)
-- Name: ux_expenses_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_expenses_public_id ON public.expenses USING btree (public_id);


--
-- TOC entry 4148 (class 1259 OID 24966)
-- Name: ux_feature_flags_key_scope; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_feature_flags_key_scope ON public.feature_flags USING btree (key, COALESCE(society_id, ('-1'::integer)::bigint));


--
-- TOC entry 4037 (class 1259 OID 17945)
-- Name: ux_flats_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_flats_public_id ON public.flats USING btree (public_id);


--
-- TOC entry 4050 (class 1259 OID 17946)
-- Name: ux_jobs_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_jobs_public_id ON public.jobs USING btree (public_id);


--
-- TOC entry 4058 (class 1259 OID 17947)
-- Name: ux_maintenance_config_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_maintenance_config_public_id ON public.maintenance_config USING btree (public_id);


--
-- TOC entry 4059 (class 1259 OID 17948)
-- Name: ux_maintenance_config_society_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_maintenance_config_society_id ON public.maintenance_config USING btree (society_id);


--
-- TOC entry 4072 (class 1259 OID 17949)
-- Name: ux_maintenance_idempotency; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_maintenance_idempotency ON public.maintenance_payments USING btree (society_id, idempotency_key, bill_id) WHERE (idempotency_key IS NOT NULL);


--
-- TOC entry 4084 (class 1259 OID 17950)
-- Name: ux_notification_preferences_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_notification_preferences_public_id ON public.notification_preferences USING btree (public_id);


--
-- TOC entry 4085 (class 1259 OID 17951)
-- Name: ux_notification_preferences_user_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_notification_preferences_user_id ON public.notification_preferences USING btree (user_id);


--
-- TOC entry 4095 (class 1259 OID 17952)
-- Name: ux_payments_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_payments_public_id ON public.payments USING btree (public_id);


--
-- TOC entry 4096 (class 1259 OID 17953)
-- Name: ux_payments_razorpay_payment; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_payments_razorpay_payment ON public.payments USING btree (razorpay_payment_id) WHERE (razorpay_payment_id IS NOT NULL);


--
-- TOC entry 4108 (class 1259 OID 17954)
-- Name: ux_plans_name; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_plans_name ON public.plans USING btree (name);


--
-- TOC entry 4151 (class 1259 OID 24978)
-- Name: ux_platform_settings_key; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_platform_settings_key ON public.platform_settings USING btree (key);


--
-- TOC entry 4119 (class 1259 OID 17955)
-- Name: ux_societies_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_societies_public_id ON public.societies USING btree (public_id);


--
-- TOC entry 4140 (class 1259 OID 17956)
-- Name: ux_users_public_id; Type: INDEX; Schema: public; Owner: postgres
--

CREATE UNIQUE INDEX ux_users_public_id ON public.users USING btree (public_id);


--
-- TOC entry 4219 (class 2620 OID 17957)
-- Name: payments trg_prevent_delete_payments; Type: TRIGGER; Schema: public; Owner: postgres
--

CREATE TRIGGER trg_prevent_delete_payments BEFORE DELETE ON public.payments FOR EACH ROW EXECUTE FUNCTION public.prevent_hard_delete_payments();


--
-- TOC entry 4162 (class 2606 OID 17958)
-- Name: adjustments adjustments_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.adjustments
    ADD CONSTRAINT adjustments_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id);


--
-- TOC entry 4163 (class 2606 OID 17963)
-- Name: adjustments adjustments_flat_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.adjustments
    ADD CONSTRAINT adjustments_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id);


--
-- TOC entry 4164 (class 2606 OID 17968)
-- Name: adjustments adjustments_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.adjustments
    ADD CONSTRAINT adjustments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4165 (class 2606 OID 17973)
-- Name: attachments attachments_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT attachments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4166 (class 2606 OID 17978)
-- Name: attachments attachments_uploaded_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.attachments
    ADD CONSTRAINT attachments_uploaded_by_fkey FOREIGN KEY (uploaded_by) REFERENCES public.users(id);


--
-- TOC entry 4167 (class 2606 OID 17983)
-- Name: bill_items bill_items_bill_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_items
    ADD CONSTRAINT bill_items_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id) ON DELETE CASCADE;


--
-- TOC entry 4168 (class 2606 OID 17988)
-- Name: bill_payment_allocations bill_payment_allocations_bill_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_payment_allocations
    ADD CONSTRAINT bill_payment_allocations_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id);


--
-- TOC entry 4169 (class 2606 OID 17993)
-- Name: bill_payment_allocations bill_payment_allocations_payment_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bill_payment_allocations
    ADD CONSTRAINT bill_payment_allocations_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.maintenance_payments(id);


--
-- TOC entry 4170 (class 2606 OID 17998)
-- Name: bills bills_flat_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bills
    ADD CONSTRAINT bills_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id) ON DELETE CASCADE;


--
-- TOC entry 4171 (class 2606 OID 18003)
-- Name: bills bills_generated_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bills
    ADD CONSTRAINT bills_generated_by_fkey FOREIGN KEY (generated_by) REFERENCES public.users(id);


--
-- TOC entry 4172 (class 2606 OID 18008)
-- Name: bills bills_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bills
    ADD CONSTRAINT bills_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4216 (class 2606 OID 42286)
-- Name: email_notification_logs email_notification_logs_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.email_notification_logs
    ADD CONSTRAINT email_notification_logs_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id);


--
-- TOC entry 4217 (class 2606 OID 42291)
-- Name: email_notification_logs email_notification_logs_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.email_notification_logs
    ADD CONSTRAINT email_notification_logs_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- TOC entry 4174 (class 2606 OID 18013)
-- Name: expenses expenses_approved_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT expenses_approved_by_fkey FOREIGN KEY (approved_by) REFERENCES public.users(id);


--
-- TOC entry 4175 (class 2606 OID 18018)
-- Name: expenses expenses_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT expenses_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id);


--
-- TOC entry 4176 (class 2606 OID 18023)
-- Name: expenses expenses_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.expenses
    ADD CONSTRAINT expenses_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4212 (class 2606 OID 24961)
-- Name: feature_flags feature_flags_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.feature_flags
    ADD CONSTRAINT feature_flags_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4173 (class 2606 OID 18028)
-- Name: bills fk_bill_plan; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.bills
    ADD CONSTRAINT fk_bill_plan FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id);


--
-- TOC entry 4186 (class 2606 OID 18033)
-- Name: maintenance_payments fk_maintenance_adjustment; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_adjustment FOREIGN KEY (adjustment_id) REFERENCES public.adjustments(id) ON DELETE SET NULL;


--
-- TOC entry 4187 (class 2606 OID 18038)
-- Name: maintenance_payments fk_maintenance_bill; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_bill FOREIGN KEY (bill_id) REFERENCES public.bills(id) ON DELETE SET NULL;


--
-- TOC entry 4188 (class 2606 OID 18043)
-- Name: maintenance_payments fk_maintenance_flat; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_flat FOREIGN KEY (flat_id) REFERENCES public.flats(id) ON DELETE CASCADE;


--
-- TOC entry 4189 (class 2606 OID 18048)
-- Name: maintenance_payments fk_maintenance_payment_mode; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_payment_mode FOREIGN KEY (payment_mode_id) REFERENCES public.payment_modes(id);


--
-- TOC entry 4192 (class 2606 OID 18053)
-- Name: maintenance_plans fk_maintenance_plan_society; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_plans
    ADD CONSTRAINT fk_maintenance_plan_society FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4190 (class 2606 OID 18058)
-- Name: maintenance_payments fk_maintenance_recorded_by; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_recorded_by FOREIGN KEY (recorded_by) REFERENCES public.users(id);


--
-- TOC entry 4191 (class 2606 OID 18063)
-- Name: maintenance_payments fk_maintenance_society; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_payments
    ADD CONSTRAINT fk_maintenance_society FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4213 (class 2606 OID 39524)
-- Name: plan_price_history fk_plan_price_history_plan; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_price_history
    ADD CONSTRAINT fk_plan_price_history_plan FOREIGN KEY (plan_id) REFERENCES public.plans(id);


--
-- TOC entry 4177 (class 2606 OID 18068)
-- Name: flats flats_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.flats
    ADD CONSTRAINT flats_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4178 (class 2606 OID 18073)
-- Name: flats flats_status_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.flats
    ADD CONSTRAINT flats_status_id_fkey FOREIGN KEY (status_id) REFERENCES public.flat_statuses(id) ON UPDATE CASCADE ON DELETE RESTRICT;


--
-- TOC entry 4179 (class 2606 OID 39504)
-- Name: invoices invoices_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id);


--
-- TOC entry 4180 (class 2606 OID 18078)
-- Name: invoices invoices_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id) ON DELETE SET NULL;


--
-- TOC entry 4181 (class 2606 OID 18083)
-- Name: invoices invoices_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.invoices
    ADD CONSTRAINT invoices_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4182 (class 2606 OID 18088)
-- Name: maintenance_components maintenance_components_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_components
    ADD CONSTRAINT maintenance_components_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4183 (class 2606 OID 18093)
-- Name: maintenance_config maintenance_config_created_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_config
    ADD CONSTRAINT maintenance_config_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id);


--
-- TOC entry 4184 (class 2606 OID 18098)
-- Name: maintenance_config maintenance_config_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_config
    ADD CONSTRAINT maintenance_config_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4185 (class 2606 OID 18103)
-- Name: maintenance_config maintenance_config_updated_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_config
    ADD CONSTRAINT maintenance_config_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES public.users(id);


--
-- TOC entry 4193 (class 2606 OID 18108)
-- Name: maintenance_rate_history maintenance_rate_history_maintenance_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.maintenance_rate_history
    ADD CONSTRAINT maintenance_rate_history_maintenance_plan_id_fkey FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id) ON DELETE CASCADE;


--
-- TOC entry 4194 (class 2606 OID 18113)
-- Name: notification_preferences notification_preferences_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.notification_preferences
    ADD CONSTRAINT notification_preferences_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4218 (class 2606 OID 42309)
-- Name: password_reset_tokens password_reset_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.password_reset_tokens
    ADD CONSTRAINT password_reset_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id);


--
-- TOC entry 4195 (class 2606 OID 18118)
-- Name: payments payments_bill_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id) ON DELETE SET NULL;


--
-- TOC entry 4196 (class 2606 OID 18123)
-- Name: payments payments_flat_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id) ON DELETE CASCADE;


--
-- TOC entry 4197 (class 2606 OID 18128)
-- Name: payments payments_recorded_by_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_recorded_by_fkey FOREIGN KEY (recorded_by) REFERENCES public.users(id);


--
-- TOC entry 4198 (class 2606 OID 18133)
-- Name: payments payments_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.payments
    ADD CONSTRAINT payments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4199 (class 2606 OID 18138)
-- Name: plan_components plan_components_maintenance_component_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_components
    ADD CONSTRAINT plan_components_maintenance_component_id_fkey FOREIGN KEY (maintenance_component_id) REFERENCES public.maintenance_components(id) ON DELETE CASCADE;


--
-- TOC entry 4200 (class 2606 OID 18143)
-- Name: plan_components plan_components_maintenance_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.plan_components
    ADD CONSTRAINT plan_components_maintenance_plan_id_fkey FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id) ON DELETE CASCADE;


--
-- TOC entry 4201 (class 2606 OID 18148)
-- Name: refresh_tokens refresh_tokens_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.refresh_tokens
    ADD CONSTRAINT refresh_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4202 (class 2606 OID 18153)
-- Name: societies societies_maintenance_cycle_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.societies
    ADD CONSTRAINT societies_maintenance_cycle_id_fkey FOREIGN KEY (maintenance_cycle_id) REFERENCES public.maintenance_cycles(id);


--
-- TOC entry 4203 (class 2606 OID 39509)
-- Name: societies societies_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.societies
    ADD CONSTRAINT societies_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id);


--
-- TOC entry 4204 (class 2606 OID 40216)
-- Name: subscription_events subscription_events_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_events
    ADD CONSTRAINT subscription_events_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id);


--
-- TOC entry 4205 (class 2606 OID 18158)
-- Name: subscription_events subscription_events_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_events
    ADD CONSTRAINT subscription_events_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id) ON DELETE SET NULL;


--
-- TOC entry 4206 (class 2606 OID 18163)
-- Name: subscription_events subscription_events_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_events
    ADD CONSTRAINT subscription_events_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4215 (class 2606 OID 39553)
-- Name: subscription_payments subscription_payments_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_payments
    ADD CONSTRAINT subscription_payments_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id);


--
-- TOC entry 4214 (class 2606 OID 39538)
-- Name: subscription_snapshots subscription_snapshots_subscription_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscription_snapshots
    ADD CONSTRAINT subscription_snapshots_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id);


--
-- TOC entry 4207 (class 2606 OID 18168)
-- Name: subscriptions subscriptions_plan_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.plans(id);


--
-- TOC entry 4208 (class 2606 OID 39499)
-- Name: subscriptions subscriptions_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id);


--
-- TOC entry 4209 (class 2606 OID 18173)
-- Name: subscriptions subscriptions_user_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.subscriptions
    ADD CONSTRAINT subscriptions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id) ON DELETE CASCADE;


--
-- TOC entry 4210 (class 2606 OID 18178)
-- Name: users users_role_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id);


--
-- TOC entry 4211 (class 2606 OID 18183)
-- Name: users users_society_id_fkey; Type: FK CONSTRAINT; Schema: public; Owner: postgres
--

ALTER TABLE ONLY public.users
    ADD CONSTRAINT users_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id) ON DELETE CASCADE;


--
-- TOC entry 4371 (class 0 OID 42276)
-- Dependencies: 418
-- Name: email_notification_logs; Type: ROW SECURITY; Schema: public; Owner: postgres
--

ALTER TABLE public.email_notification_logs ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4372 (class 0 OID 42300)
-- Dependencies: 420
-- Name: password_reset_tokens; Type: ROW SECURITY; Schema: public; Owner: postgres
--

ALTER TABLE public.password_reset_tokens ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4368 (class 0 OID 39515)
-- Dependencies: 414
-- Name: plan_price_history; Type: ROW SECURITY; Schema: public; Owner: postgres
--

ALTER TABLE public.plan_price_history ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4370 (class 0 OID 39543)
-- Dependencies: 416
-- Name: subscription_payments; Type: ROW SECURITY; Schema: public; Owner: postgres
--

ALTER TABLE public.subscription_payments ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4369 (class 0 OID 39529)
-- Dependencies: 415
-- Name: subscription_snapshots; Type: ROW SECURITY; Schema: public; Owner: postgres
--

ALTER TABLE public.subscription_snapshots ENABLE ROW LEVEL SECURITY;

--
-- TOC entry 4380 (class 0 OID 0)
-- Dependencies: 93
-- Name: SCHEMA public; Type: ACL; Schema: -; Owner: pg_database_owner
--

GRANT USAGE ON SCHEMA public TO postgres;
GRANT USAGE ON SCHEMA public TO anon;
GRANT USAGE ON SCHEMA public TO authenticated;
GRANT USAGE ON SCHEMA public TO service_role;


--
-- TOC entry 4381 (class 0 OID 0)
-- Dependencies: 524
-- Name: FUNCTION get_collection_summary(p_society_id bigint, p_start_period text, p_end_period text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_collection_summary(p_society_id bigint, p_start_period text, p_end_period text) TO anon;
GRANT ALL ON FUNCTION public.get_collection_summary(p_society_id bigint, p_start_period text, p_end_period text) TO authenticated;
GRANT ALL ON FUNCTION public.get_collection_summary(p_society_id bigint, p_start_period text, p_end_period text) TO service_role;


--
-- TOC entry 4382 (class 0 OID 0)
-- Dependencies: 525
-- Name: FUNCTION get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone, p_end_date timestamp without time zone); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone, p_end_date timestamp without time zone) TO anon;
GRANT ALL ON FUNCTION public.get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone, p_end_date timestamp without time zone) TO authenticated;
GRANT ALL ON FUNCTION public.get_dashboard_data(p_society_id bigint, p_start_date timestamp without time zone, p_end_date timestamp without time zone) TO service_role;


--
-- TOC entry 4383 (class 0 OID 0)
-- Dependencies: 526
-- Name: FUNCTION get_defaulters_report(p_society_id bigint, p_min_outstanding numeric); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_defaulters_report(p_society_id bigint, p_min_outstanding numeric) TO anon;
GRANT ALL ON FUNCTION public.get_defaulters_report(p_society_id bigint, p_min_outstanding numeric) TO authenticated;
GRANT ALL ON FUNCTION public.get_defaulters_report(p_society_id bigint, p_min_outstanding numeric) TO service_role;


--
-- TOC entry 4384 (class 0 OID 0)
-- Dependencies: 527
-- Name: FUNCTION get_expense_by_category(p_society_id bigint, p_start_date date, p_end_date date); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_expense_by_category(p_society_id bigint, p_start_date date, p_end_date date) TO anon;
GRANT ALL ON FUNCTION public.get_expense_by_category(p_society_id bigint, p_start_date date, p_end_date date) TO authenticated;
GRANT ALL ON FUNCTION public.get_expense_by_category(p_society_id bigint, p_start_date date, p_end_date date) TO service_role;


--
-- TOC entry 4385 (class 0 OID 0)
-- Dependencies: 528
-- Name: FUNCTION get_fund_ledger_report(p_society_id bigint, p_start_date date, p_end_date date); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_fund_ledger_report(p_society_id bigint, p_start_date date, p_end_date date) TO anon;
GRANT ALL ON FUNCTION public.get_fund_ledger_report(p_society_id bigint, p_start_date date, p_end_date date) TO authenticated;
GRANT ALL ON FUNCTION public.get_fund_ledger_report(p_society_id bigint, p_start_date date, p_end_date date) TO service_role;


--
-- TOC entry 4386 (class 0 OID 0)
-- Dependencies: 529
-- Name: FUNCTION get_income_vs_expense(p_society_id bigint, p_start_date date, p_end_date date); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_income_vs_expense(p_society_id bigint, p_start_date date, p_end_date date) TO anon;
GRANT ALL ON FUNCTION public.get_income_vs_expense(p_society_id bigint, p_start_date date, p_end_date date) TO authenticated;
GRANT ALL ON FUNCTION public.get_income_vs_expense(p_society_id bigint, p_start_date date, p_end_date date) TO service_role;


--
-- TOC entry 4387 (class 0 OID 0)
-- Dependencies: 530
-- Name: FUNCTION get_maintenance_payment_register(p_society_id bigint, p_start_date date, p_end_date date, p_limit integer, p_offset integer); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_maintenance_payment_register(p_society_id bigint, p_start_date date, p_end_date date, p_limit integer, p_offset integer) TO anon;
GRANT ALL ON FUNCTION public.get_maintenance_payment_register(p_society_id bigint, p_start_date date, p_end_date date, p_limit integer, p_offset integer) TO authenticated;
GRANT ALL ON FUNCTION public.get_maintenance_payment_register(p_society_id bigint, p_start_date date, p_end_date date, p_limit integer, p_offset integer) TO service_role;


--
-- TOC entry 4388 (class 0 OID 0)
-- Dependencies: 531
-- Name: FUNCTION get_maintenance_payment_register_count(p_society_id bigint, p_start_date date, p_end_date date); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_maintenance_payment_register_count(p_society_id bigint, p_start_date date, p_end_date date) TO anon;
GRANT ALL ON FUNCTION public.get_maintenance_payment_register_count(p_society_id bigint, p_start_date date, p_end_date date) TO authenticated;
GRANT ALL ON FUNCTION public.get_maintenance_payment_register_count(p_society_id bigint, p_start_date date, p_end_date date) TO service_role;


--
-- TOC entry 4389 (class 0 OID 0)
-- Dependencies: 533
-- Name: FUNCTION get_monthly_report(p_society_id bigint, p_year integer, p_month integer); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) TO anon;
GRANT ALL ON FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) TO authenticated;
GRANT ALL ON FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) TO service_role;


--
-- TOC entry 4390 (class 0 OID 0)
-- Dependencies: 534
-- Name: FUNCTION get_yearly_report(p_society_id bigint, p_year integer, p_year_type text); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.get_yearly_report(p_society_id bigint, p_year integer, p_year_type text) TO anon;
GRANT ALL ON FUNCTION public.get_yearly_report(p_society_id bigint, p_year integer, p_year_type text) TO authenticated;
GRANT ALL ON FUNCTION public.get_yearly_report(p_society_id bigint, p_year integer, p_year_type text) TO service_role;


--
-- TOC entry 4391 (class 0 OID 0)
-- Dependencies: 532
-- Name: FUNCTION prevent_hard_delete_payments(); Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON FUNCTION public.prevent_hard_delete_payments() TO anon;
GRANT ALL ON FUNCTION public.prevent_hard_delete_payments() TO authenticated;
GRANT ALL ON FUNCTION public.prevent_hard_delete_payments() TO service_role;


--
-- TOC entry 4392 (class 0 OID 0)
-- Dependencies: 346
-- Name: TABLE adjustments; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.adjustments TO anon;
GRANT ALL ON TABLE public.adjustments TO authenticated;
GRANT ALL ON TABLE public.adjustments TO service_role;


--
-- TOC entry 4393 (class 0 OID 0)
-- Dependencies: 347
-- Name: SEQUENCE adjustments_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.adjustments_id_seq TO anon;
GRANT ALL ON SEQUENCE public.adjustments_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.adjustments_id_seq TO service_role;


--
-- TOC entry 4394 (class 0 OID 0)
-- Dependencies: 405
-- Name: TABLE admin_users; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.admin_users TO anon;
GRANT ALL ON TABLE public.admin_users TO authenticated;
GRANT ALL ON TABLE public.admin_users TO service_role;


--
-- TOC entry 4395 (class 0 OID 0)
-- Dependencies: 404
-- Name: SEQUENCE admin_users_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.admin_users_id_seq TO anon;
GRANT ALL ON SEQUENCE public.admin_users_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.admin_users_id_seq TO service_role;


--
-- TOC entry 4396 (class 0 OID 0)
-- Dependencies: 348
-- Name: TABLE attachments; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.attachments TO anon;
GRANT ALL ON TABLE public.attachments TO authenticated;
GRANT ALL ON TABLE public.attachments TO service_role;


--
-- TOC entry 4397 (class 0 OID 0)
-- Dependencies: 349
-- Name: SEQUENCE attachments_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.attachments_id_seq TO anon;
GRANT ALL ON SEQUENCE public.attachments_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.attachments_id_seq TO service_role;


--
-- TOC entry 4398 (class 0 OID 0)
-- Dependencies: 350
-- Name: TABLE audit_logs; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.audit_logs TO anon;
GRANT ALL ON TABLE public.audit_logs TO authenticated;
GRANT ALL ON TABLE public.audit_logs TO service_role;


--
-- TOC entry 4399 (class 0 OID 0)
-- Dependencies: 351
-- Name: SEQUENCE audit_logs_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.audit_logs_id_seq TO anon;
GRANT ALL ON SEQUENCE public.audit_logs_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.audit_logs_id_seq TO service_role;


--
-- TOC entry 4400 (class 0 OID 0)
-- Dependencies: 352
-- Name: TABLE bill_items; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.bill_items TO anon;
GRANT ALL ON TABLE public.bill_items TO authenticated;
GRANT ALL ON TABLE public.bill_items TO service_role;


--
-- TOC entry 4402 (class 0 OID 0)
-- Dependencies: 353
-- Name: SEQUENCE bill_items_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.bill_items_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bill_items_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bill_items_id_seq TO service_role;


--
-- TOC entry 4403 (class 0 OID 0)
-- Dependencies: 354
-- Name: TABLE bill_payment_allocations; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.bill_payment_allocations TO anon;
GRANT ALL ON TABLE public.bill_payment_allocations TO authenticated;
GRANT ALL ON TABLE public.bill_payment_allocations TO service_role;


--
-- TOC entry 4405 (class 0 OID 0)
-- Dependencies: 355
-- Name: SEQUENCE bill_payment_allocations_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.bill_payment_allocations_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bill_payment_allocations_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bill_payment_allocations_id_seq TO service_role;


--
-- TOC entry 4406 (class 0 OID 0)
-- Dependencies: 356
-- Name: TABLE bill_statuses; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.bill_statuses TO anon;
GRANT ALL ON TABLE public.bill_statuses TO authenticated;
GRANT ALL ON TABLE public.bill_statuses TO service_role;


--
-- TOC entry 4407 (class 0 OID 0)
-- Dependencies: 357
-- Name: SEQUENCE bill_statuses_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.bill_statuses_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bill_statuses_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bill_statuses_id_seq TO service_role;


--
-- TOC entry 4408 (class 0 OID 0)
-- Dependencies: 358
-- Name: TABLE bills; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.bills TO anon;
GRANT ALL ON TABLE public.bills TO authenticated;
GRANT ALL ON TABLE public.bills TO service_role;


--
-- TOC entry 4409 (class 0 OID 0)
-- Dependencies: 359
-- Name: SEQUENCE bills_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.bills_id_seq TO anon;
GRANT ALL ON SEQUENCE public.bills_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.bills_id_seq TO service_role;


--
-- TOC entry 4410 (class 0 OID 0)
-- Dependencies: 418
-- Name: TABLE email_notification_logs; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.email_notification_logs TO anon;
GRANT ALL ON TABLE public.email_notification_logs TO authenticated;
GRANT ALL ON TABLE public.email_notification_logs TO service_role;


--
-- TOC entry 4411 (class 0 OID 0)
-- Dependencies: 417
-- Name: SEQUENCE email_notification_logs_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.email_notification_logs_id_seq TO anon;
GRANT ALL ON SEQUENCE public.email_notification_logs_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.email_notification_logs_id_seq TO service_role;


--
-- TOC entry 4412 (class 0 OID 0)
-- Dependencies: 360
-- Name: TABLE expense_categories; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.expense_categories TO anon;
GRANT ALL ON TABLE public.expense_categories TO authenticated;
GRANT ALL ON TABLE public.expense_categories TO service_role;


--
-- TOC entry 4413 (class 0 OID 0)
-- Dependencies: 361
-- Name: SEQUENCE expense_categories_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.expense_categories_id_seq TO anon;
GRANT ALL ON SEQUENCE public.expense_categories_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.expense_categories_id_seq TO service_role;


--
-- TOC entry 4414 (class 0 OID 0)
-- Dependencies: 362
-- Name: TABLE expenses; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.expenses TO anon;
GRANT ALL ON TABLE public.expenses TO authenticated;
GRANT ALL ON TABLE public.expenses TO service_role;


--
-- TOC entry 4415 (class 0 OID 0)
-- Dependencies: 363
-- Name: SEQUENCE expenses_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.expenses_id_seq TO anon;
GRANT ALL ON SEQUENCE public.expenses_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.expenses_id_seq TO service_role;


--
-- TOC entry 4416 (class 0 OID 0)
-- Dependencies: 407
-- Name: TABLE feature_flags; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.feature_flags TO anon;
GRANT ALL ON TABLE public.feature_flags TO authenticated;
GRANT ALL ON TABLE public.feature_flags TO service_role;


--
-- TOC entry 4417 (class 0 OID 0)
-- Dependencies: 406
-- Name: SEQUENCE feature_flags_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.feature_flags_id_seq TO anon;
GRANT ALL ON SEQUENCE public.feature_flags_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.feature_flags_id_seq TO service_role;


--
-- TOC entry 4418 (class 0 OID 0)
-- Dependencies: 364
-- Name: TABLE flat_statuses; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.flat_statuses TO anon;
GRANT ALL ON TABLE public.flat_statuses TO authenticated;
GRANT ALL ON TABLE public.flat_statuses TO service_role;


--
-- TOC entry 4419 (class 0 OID 0)
-- Dependencies: 365
-- Name: SEQUENCE flat_statuses_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.flat_statuses_id_seq TO anon;
GRANT ALL ON SEQUENCE public.flat_statuses_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.flat_statuses_id_seq TO service_role;


--
-- TOC entry 4420 (class 0 OID 0)
-- Dependencies: 366
-- Name: TABLE flats; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.flats TO anon;
GRANT ALL ON TABLE public.flats TO authenticated;
GRANT ALL ON TABLE public.flats TO service_role;


--
-- TOC entry 4421 (class 0 OID 0)
-- Dependencies: 367
-- Name: SEQUENCE flats_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.flats_id_seq TO anon;
GRANT ALL ON SEQUENCE public.flats_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.flats_id_seq TO service_role;


--
-- TOC entry 4422 (class 0 OID 0)
-- Dependencies: 368
-- Name: TABLE invoices; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.invoices TO anon;
GRANT ALL ON TABLE public.invoices TO authenticated;
GRANT ALL ON TABLE public.invoices TO service_role;


--
-- TOC entry 4423 (class 0 OID 0)
-- Dependencies: 369
-- Name: TABLE jobs; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.jobs TO anon;
GRANT ALL ON TABLE public.jobs TO authenticated;
GRANT ALL ON TABLE public.jobs TO service_role;


--
-- TOC entry 4424 (class 0 OID 0)
-- Dependencies: 370
-- Name: SEQUENCE jobs_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.jobs_id_seq TO anon;
GRANT ALL ON SEQUENCE public.jobs_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.jobs_id_seq TO service_role;


--
-- TOC entry 4425 (class 0 OID 0)
-- Dependencies: 371
-- Name: TABLE maintenance_components; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_components TO anon;
GRANT ALL ON TABLE public.maintenance_components TO authenticated;
GRANT ALL ON TABLE public.maintenance_components TO service_role;


--
-- TOC entry 4427 (class 0 OID 0)
-- Dependencies: 372
-- Name: SEQUENCE maintenance_components_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_components_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_components_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_components_id_seq TO service_role;


--
-- TOC entry 4428 (class 0 OID 0)
-- Dependencies: 373
-- Name: TABLE maintenance_config; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_config TO anon;
GRANT ALL ON TABLE public.maintenance_config TO authenticated;
GRANT ALL ON TABLE public.maintenance_config TO service_role;


--
-- TOC entry 4429 (class 0 OID 0)
-- Dependencies: 374
-- Name: SEQUENCE maintenance_config_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_config_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_config_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_config_id_seq TO service_role;


--
-- TOC entry 4430 (class 0 OID 0)
-- Dependencies: 375
-- Name: TABLE maintenance_cycles; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_cycles TO anon;
GRANT ALL ON TABLE public.maintenance_cycles TO authenticated;
GRANT ALL ON TABLE public.maintenance_cycles TO service_role;


--
-- TOC entry 4431 (class 0 OID 0)
-- Dependencies: 376
-- Name: SEQUENCE maintenance_cycles_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_cycles_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_cycles_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_cycles_id_seq TO service_role;


--
-- TOC entry 4432 (class 0 OID 0)
-- Dependencies: 377
-- Name: TABLE maintenance_payments; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_payments TO anon;
GRANT ALL ON TABLE public.maintenance_payments TO authenticated;
GRANT ALL ON TABLE public.maintenance_payments TO service_role;


--
-- TOC entry 4433 (class 0 OID 0)
-- Dependencies: 378
-- Name: SEQUENCE maintenance_payments_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_payments_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_payments_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_payments_id_seq TO service_role;


--
-- TOC entry 4434 (class 0 OID 0)
-- Dependencies: 379
-- Name: TABLE maintenance_plans; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_plans TO anon;
GRANT ALL ON TABLE public.maintenance_plans TO authenticated;
GRANT ALL ON TABLE public.maintenance_plans TO service_role;


--
-- TOC entry 4436 (class 0 OID 0)
-- Dependencies: 380
-- Name: SEQUENCE maintenance_plans_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_plans_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_plans_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_plans_id_seq TO service_role;


--
-- TOC entry 4437 (class 0 OID 0)
-- Dependencies: 381
-- Name: TABLE maintenance_rate_history; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.maintenance_rate_history TO anon;
GRANT ALL ON TABLE public.maintenance_rate_history TO authenticated;
GRANT ALL ON TABLE public.maintenance_rate_history TO service_role;


--
-- TOC entry 4439 (class 0 OID 0)
-- Dependencies: 382
-- Name: SEQUENCE maintenance_rate_history_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.maintenance_rate_history_id_seq TO anon;
GRANT ALL ON SEQUENCE public.maintenance_rate_history_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.maintenance_rate_history_id_seq TO service_role;


--
-- TOC entry 4440 (class 0 OID 0)
-- Dependencies: 383
-- Name: TABLE notification_preferences; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.notification_preferences TO anon;
GRANT ALL ON TABLE public.notification_preferences TO authenticated;
GRANT ALL ON TABLE public.notification_preferences TO service_role;


--
-- TOC entry 4441 (class 0 OID 0)
-- Dependencies: 384
-- Name: SEQUENCE notification_preferences_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.notification_preferences_id_seq TO anon;
GRANT ALL ON SEQUENCE public.notification_preferences_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.notification_preferences_id_seq TO service_role;


--
-- TOC entry 4442 (class 0 OID 0)
-- Dependencies: 420
-- Name: TABLE password_reset_tokens; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.password_reset_tokens TO anon;
GRANT ALL ON TABLE public.password_reset_tokens TO authenticated;
GRANT ALL ON TABLE public.password_reset_tokens TO service_role;


--
-- TOC entry 4443 (class 0 OID 0)
-- Dependencies: 419
-- Name: SEQUENCE password_reset_tokens_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.password_reset_tokens_id_seq TO anon;
GRANT ALL ON SEQUENCE public.password_reset_tokens_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.password_reset_tokens_id_seq TO service_role;


--
-- TOC entry 4444 (class 0 OID 0)
-- Dependencies: 385
-- Name: TABLE payment_modes; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.payment_modes TO anon;
GRANT ALL ON TABLE public.payment_modes TO authenticated;
GRANT ALL ON TABLE public.payment_modes TO service_role;


--
-- TOC entry 4445 (class 0 OID 0)
-- Dependencies: 386
-- Name: SEQUENCE payment_modes_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.payment_modes_id_seq TO anon;
GRANT ALL ON SEQUENCE public.payment_modes_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.payment_modes_id_seq TO service_role;


--
-- TOC entry 4446 (class 0 OID 0)
-- Dependencies: 387
-- Name: TABLE payments; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.payments TO anon;
GRANT ALL ON TABLE public.payments TO authenticated;
GRANT ALL ON TABLE public.payments TO service_role;


--
-- TOC entry 4447 (class 0 OID 0)
-- Dependencies: 388
-- Name: SEQUENCE payments_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.payments_id_seq TO anon;
GRANT ALL ON SEQUENCE public.payments_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.payments_id_seq TO service_role;


--
-- TOC entry 4448 (class 0 OID 0)
-- Dependencies: 389
-- Name: TABLE plan_components; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.plan_components TO anon;
GRANT ALL ON TABLE public.plan_components TO authenticated;
GRANT ALL ON TABLE public.plan_components TO service_role;


--
-- TOC entry 4450 (class 0 OID 0)
-- Dependencies: 390
-- Name: SEQUENCE plan_components_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.plan_components_id_seq TO anon;
GRANT ALL ON SEQUENCE public.plan_components_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.plan_components_id_seq TO service_role;


--
-- TOC entry 4451 (class 0 OID 0)
-- Dependencies: 414
-- Name: TABLE plan_price_history; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.plan_price_history TO anon;
GRANT ALL ON TABLE public.plan_price_history TO authenticated;
GRANT ALL ON TABLE public.plan_price_history TO service_role;


--
-- TOC entry 4452 (class 0 OID 0)
-- Dependencies: 413
-- Name: SEQUENCE plan_price_history_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.plan_price_history_id_seq TO anon;
GRANT ALL ON SEQUENCE public.plan_price_history_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.plan_price_history_id_seq TO service_role;


--
-- TOC entry 4453 (class 0 OID 0)
-- Dependencies: 391
-- Name: TABLE plans; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.plans TO anon;
GRANT ALL ON TABLE public.plans TO authenticated;
GRANT ALL ON TABLE public.plans TO service_role;


--
-- TOC entry 4454 (class 0 OID 0)
-- Dependencies: 409
-- Name: TABLE platform_settings; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.platform_settings TO anon;
GRANT ALL ON TABLE public.platform_settings TO authenticated;
GRANT ALL ON TABLE public.platform_settings TO service_role;


--
-- TOC entry 4455 (class 0 OID 0)
-- Dependencies: 408
-- Name: SEQUENCE platform_settings_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.platform_settings_id_seq TO anon;
GRANT ALL ON SEQUENCE public.platform_settings_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.platform_settings_id_seq TO service_role;


--
-- TOC entry 4456 (class 0 OID 0)
-- Dependencies: 392
-- Name: TABLE refresh_tokens; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.refresh_tokens TO anon;
GRANT ALL ON TABLE public.refresh_tokens TO authenticated;
GRANT ALL ON TABLE public.refresh_tokens TO service_role;


--
-- TOC entry 4457 (class 0 OID 0)
-- Dependencies: 393
-- Name: SEQUENCE refresh_tokens_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.refresh_tokens_id_seq TO anon;
GRANT ALL ON SEQUENCE public.refresh_tokens_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.refresh_tokens_id_seq TO service_role;


--
-- TOC entry 4458 (class 0 OID 0)
-- Dependencies: 394
-- Name: TABLE roles; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.roles TO anon;
GRANT ALL ON TABLE public.roles TO authenticated;
GRANT ALL ON TABLE public.roles TO service_role;


--
-- TOC entry 4459 (class 0 OID 0)
-- Dependencies: 395
-- Name: SEQUENCE roles_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.roles_id_seq TO anon;
GRANT ALL ON SEQUENCE public.roles_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.roles_id_seq TO service_role;


--
-- TOC entry 4460 (class 0 OID 0)
-- Dependencies: 396
-- Name: TABLE societies; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.societies TO anon;
GRANT ALL ON TABLE public.societies TO authenticated;
GRANT ALL ON TABLE public.societies TO service_role;


--
-- TOC entry 4461 (class 0 OID 0)
-- Dependencies: 397
-- Name: SEQUENCE societies_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.societies_id_seq TO anon;
GRANT ALL ON SEQUENCE public.societies_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.societies_id_seq TO service_role;


--
-- TOC entry 4462 (class 0 OID 0)
-- Dependencies: 398
-- Name: TABLE society_fund_ledger; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.society_fund_ledger TO anon;
GRANT ALL ON TABLE public.society_fund_ledger TO authenticated;
GRANT ALL ON TABLE public.society_fund_ledger TO service_role;


--
-- TOC entry 4464 (class 0 OID 0)
-- Dependencies: 399
-- Name: SEQUENCE society_fund_ledger_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.society_fund_ledger_id_seq TO anon;
GRANT ALL ON SEQUENCE public.society_fund_ledger_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.society_fund_ledger_id_seq TO service_role;


--
-- TOC entry 4465 (class 0 OID 0)
-- Dependencies: 400
-- Name: TABLE subscription_events; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.subscription_events TO anon;
GRANT ALL ON TABLE public.subscription_events TO authenticated;
GRANT ALL ON TABLE public.subscription_events TO service_role;


--
-- TOC entry 4466 (class 0 OID 0)
-- Dependencies: 416
-- Name: TABLE subscription_payments; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.subscription_payments TO anon;
GRANT ALL ON TABLE public.subscription_payments TO authenticated;
GRANT ALL ON TABLE public.subscription_payments TO service_role;


--
-- TOC entry 4467 (class 0 OID 0)
-- Dependencies: 415
-- Name: TABLE subscription_snapshots; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.subscription_snapshots TO anon;
GRANT ALL ON TABLE public.subscription_snapshots TO authenticated;
GRANT ALL ON TABLE public.subscription_snapshots TO service_role;


--
-- TOC entry 4468 (class 0 OID 0)
-- Dependencies: 401
-- Name: TABLE subscriptions; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.subscriptions TO anon;
GRANT ALL ON TABLE public.subscriptions TO authenticated;
GRANT ALL ON TABLE public.subscriptions TO service_role;


--
-- TOC entry 4469 (class 0 OID 0)
-- Dependencies: 402
-- Name: TABLE users; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.users TO anon;
GRANT ALL ON TABLE public.users TO authenticated;
GRANT ALL ON TABLE public.users TO service_role;


--
-- TOC entry 4470 (class 0 OID 0)
-- Dependencies: 403
-- Name: SEQUENCE users_id_seq; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON SEQUENCE public.users_id_seq TO anon;
GRANT ALL ON SEQUENCE public.users_id_seq TO authenticated;
GRANT ALL ON SEQUENCE public.users_id_seq TO service_role;


--
-- TOC entry 4471 (class 0 OID 0)
-- Dependencies: 412
-- Name: TABLE v_opening_bal; Type: ACL; Schema: public; Owner: postgres
--

GRANT ALL ON TABLE public.v_opening_bal TO anon;
GRANT ALL ON TABLE public.v_opening_bal TO authenticated;
GRANT ALL ON TABLE public.v_opening_bal TO service_role;


--
-- TOC entry 2580 (class 826 OID 16494)
-- Name: DEFAULT PRIVILEGES FOR SEQUENCES; Type: DEFAULT ACL; Schema: public; Owner: postgres
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON SEQUENCES TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON SEQUENCES TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON SEQUENCES TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON SEQUENCES TO service_role;


--
-- TOC entry 2581 (class 826 OID 16495)
-- Name: DEFAULT PRIVILEGES FOR SEQUENCES; Type: DEFAULT ACL; Schema: public; Owner: supabase_admin
--

ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON SEQUENCES TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON SEQUENCES TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON SEQUENCES TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON SEQUENCES TO service_role;


--
-- TOC entry 2579 (class 826 OID 16493)
-- Name: DEFAULT PRIVILEGES FOR FUNCTIONS; Type: DEFAULT ACL; Schema: public; Owner: postgres
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON FUNCTIONS TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON FUNCTIONS TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON FUNCTIONS TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON FUNCTIONS TO service_role;


--
-- TOC entry 2583 (class 826 OID 16497)
-- Name: DEFAULT PRIVILEGES FOR FUNCTIONS; Type: DEFAULT ACL; Schema: public; Owner: supabase_admin
--

ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON FUNCTIONS TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON FUNCTIONS TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON FUNCTIONS TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON FUNCTIONS TO service_role;


--
-- TOC entry 2578 (class 826 OID 16492)
-- Name: DEFAULT PRIVILEGES FOR TABLES; Type: DEFAULT ACL; Schema: public; Owner: postgres
--

ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON TABLES TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON TABLES TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON TABLES TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE postgres IN SCHEMA public GRANT ALL ON TABLES TO service_role;


--
-- TOC entry 2582 (class 826 OID 16496)
-- Name: DEFAULT PRIVILEGES FOR TABLES; Type: DEFAULT ACL; Schema: public; Owner: supabase_admin
--

ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON TABLES TO postgres;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON TABLES TO anon;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON TABLES TO authenticated;
ALTER DEFAULT PRIVILEGES FOR ROLE supabase_admin IN SCHEMA public GRANT ALL ON TABLES TO service_role;


-- Completed on 2026-07-02 13:31:18

--
-- PostgreSQL database dump complete
--

