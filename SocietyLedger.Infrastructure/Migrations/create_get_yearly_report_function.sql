-- =============================================================================
-- Function : public.get_yearly_report(p_society_id, p_year, p_year_type)
-- Returns  : JSON matching YearlyReportDto (C# / .NET API)
--
-- Produces a year-level report composed of monthly summaries. Uses the same
-- accounting model as the monthly report: adjustments.amount is the canonical
-- opening/arrear seed; bills represent charges; maintenance_payments are ALL
-- receipts (bill payments, OB clearance, advances). The monthly breakdown is
-- produced by generating months between the chosen start and end dates and
-- left-joining aggregates for bills, receipts and expenses.
--
-- year_type: 'calendar' or 'financial' (financial year starts 1-Apr)
-- =============================================================================

CREATE OR REPLACE FUNCTION public.get_yearly_report(
    p_society_id bigint,
    p_year       int,
    p_year_type  text DEFAULT 'calendar'
)
RETURNS json
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    v_start_date   date;
    v_end_date     date;
    v_year_label   text;
    v_society_name text;

    v_opening_bal  numeric := 0;
    v_collected    numeric := 0;
    v_expenses     numeric := 0;
    v_total_billed numeric := 0;
    v_closing_bal  numeric := 0;

    v_month_rows   json;
    v_expense_rows json;

    v_summary text;
    v_alerts  json;

BEGIN
    -- Year type handling
    IF lower(coalesce(p_year_type, 'calendar')) = 'financial' THEN
        v_start_date := make_date(p_year - 1, 4, 1);
        v_end_date   := make_date(p_year, 3, 31);
        v_year_label := 'FY ' || (p_year - 1)::text || '-' || right(p_year::text, 2);
    ELSE
        v_start_date := make_date(p_year, 1, 1);
        v_end_date   := make_date(p_year, 12, 31);
        v_year_label := p_year::text;
    END IF;

    -- Society name
    SELECT name INTO v_society_name
    FROM societies
    WHERE id = p_society_id AND is_deleted = false;

    -- Opening balance (society fund) before the year
    SELECT COALESCE(SUM(amount), 0) INTO v_opening_bal
    FROM society_fund_ledger
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND entry_type = 'opening_fund';

    SELECT v_opening_bal + COALESCE(SUM(amount), 0) INTO v_opening_bal
    FROM maintenance_payments
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND DATE(payment_date) < v_start_date;

    SELECT v_opening_bal - COALESCE(SUM(amount), 0) INTO v_opening_bal
    FROM expenses
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND date_incurred < v_start_date;

    -- Year totals
    SELECT COALESCE(SUM(amount), 0) INTO v_collected
    FROM maintenance_payments
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND DATE(payment_date) BETWEEN v_start_date AND v_end_date;

    SELECT COALESCE(SUM(amount), 0) INTO v_expenses
    FROM expenses
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND date_incurred BETWEEN v_start_date AND v_end_date;

    SELECT COALESCE(SUM(amount), 0) INTO v_total_billed
    FROM bills
    WHERE society_id = p_society_id
      AND is_deleted = false
      AND period >= to_char(v_start_date, 'YYYY-MM')
      AND period <= to_char(v_end_date,   'YYYY-MM');

    v_closing_bal := v_opening_bal + v_collected - v_expenses;

    -- Monthly breakdown (only months with activity or billing)
    SELECT json_agg(row_to_json(d) ORDER BY d.month_start)
    INTO v_month_rows
    FROM (
        SELECT
            m.month_start,
            trim(to_char(m.month_start, 'Month')) || ' ' || to_char(m.month_start, 'YYYY') AS month_label,

            COALESCE(billed.total_billed, 0) AS billed,
            COALESCE(col.total_collected, 0) AS collected,
            COALESCE(exp.total_expenses, 0)  AS expenses,

            -- net = collected - expenses (month net position)
            COALESCE(col.total_collected, 0) - COALESCE(exp.total_expenses, 0) AS net,

            -- month status: 'surplus' when net >= 0, else 'deficit'
            CASE WHEN COALESCE(col.total_collected, 0) - COALESCE(exp.total_expenses, 0) >= 0
                 THEN 'surplus'
                 ELSE 'deficit'
            END AS month_status

        FROM (
            SELECT generate_series(date_trunc('month', v_start_date)::date,
                                   date_trunc('month', v_end_date)::date,
                                   '1 month')::date AS month_start
        ) m

        LEFT JOIN (
            SELECT period, SUM(amount) AS total_billed
            FROM bills
            WHERE society_id = p_society_id AND is_deleted = false
            GROUP BY period
        ) billed ON billed.period = to_char(m.month_start, 'YYYY-MM')

        LEFT JOIN (
            SELECT to_char(DATE(payment_date), 'YYYY-MM') AS period,
                   SUM(amount) AS total_collected
            FROM maintenance_payments
            WHERE society_id = p_society_id AND is_deleted = false
            GROUP BY 1
        ) col ON col.period = to_char(m.month_start, 'YYYY-MM')

        LEFT JOIN (
            SELECT to_char(date_incurred, 'YYYY-MM') AS period,
                   SUM(amount) AS total_expenses
            FROM expenses
            WHERE society_id = p_society_id AND is_deleted = false
            GROUP BY 1
        ) exp ON exp.period = to_char(m.month_start, 'YYYY-MM')

        WHERE
            COALESCE(billed.total_billed,0) > 0
            OR COALESCE(col.total_collected,0) > 0
            OR COALESCE(exp.total_expenses,0) > 0
    ) d;

    -- Expense summary by category for the year
    SELECT json_agg(row_to_json(d))
    INTO v_expense_rows
    FROM (
        SELECT
            COALESCE(ec.display_name, e.category_code) AS category_name,
            SUM(e.amount) AS total_amount
        FROM expenses e
        LEFT JOIN expense_categories ec ON ec.code = e.category_code
        WHERE e.society_id = p_society_id
          AND e.is_deleted = false
          AND e.date_incurred BETWEEN v_start_date AND v_end_date
        GROUP BY e.category_code, ec.display_name
        ORDER BY total_amount DESC
    ) d;

    -- Human summary
    v_summary :=
        'Total collected ₹' || v_collected ||
        ', total expenses ₹' || v_expenses ||
        '. Closing balance ₹' || v_closing_bal || '.';

    v_alerts :=
        CASE WHEN v_closing_bal > 0
             THEN json_build_array('Funds are sufficient for maintenance')
             ELSE json_build_array('Attention: Low balance, review expenses')
        END;

    RETURN json_build_object(
        'society_name', v_society_name,
        'year_label', v_year_label,

        'fund_position', json_build_object(
            'opening_balance', v_opening_bal,
            'total_billed',    v_total_billed,
            'total_collected', v_collected,
            'total_expenses',  v_expenses,
            'collected',       v_collected,
            'expenses',        v_expenses,
            'closing_balance', v_closing_bal
        ),

        'month_summary', COALESCE(v_month_rows, '[]'::json),
        'expenses', COALESCE(v_expense_rows, '[]'::json),

        'summary', v_summary,
        'alerts', v_alerts
    );

END;
$$;
