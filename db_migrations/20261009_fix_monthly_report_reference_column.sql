-- Migration: 2026-10-09 Fix monthly report reference column
-- Up
DROP FUNCTION IF EXISTS public.get_monthly_report(bigint, integer, integer);

CREATE FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) RETURNS json
    LANGUAGE plpgsql
    AS $$
DECLARE
    v_period         text;
    v_start_date     date;
    v_end_date       date;
    v_end_exclusive  date;
    v_society_name   text;

    v_opening_bal    numeric := 0;
    v_collected      numeric := 0;
    v_other_income   numeric := 0;
    v_expenses       numeric := 0;
    v_closing_bal    numeric := 0;

    v_total_flats    int     := 0;
    v_paid_count     int     := 0;
    v_pending_count  int     := 0;
    v_total_billed   numeric := 0;
    v_pending_amount numeric := 0;
    v_collection_eff numeric := 0;

    v_flat_rows    json;
    v_expense_rows json;
    v_income_rows  json;
    v_summary      text;
    v_alerts       json;

BEGIN
    v_period     := to_char(p_year, 'FM0000') || '-' || to_char(p_month, 'FM00');
    v_start_date := make_date(p_year, p_month, 1);
    v_end_date   := (v_start_date + interval '1 month - 1 day')::date;
    v_end_exclusive := v_end_date + 1;

    SELECT s.name
    INTO   v_society_name
    FROM   societies s
    WHERE  s.id = p_society_id
      AND  s.is_deleted = false;

    IF v_society_name IS NULL THEN
        RETURN '{}'::json;
    END IF;

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

    SELECT COALESCE(SUM(CASE WHEN LOWER(CAST(mp.category AS text)) = 'maintenance' THEN mp.amount ELSE 0 END), 0)
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

    SELECT COALESCE(SUM(CASE WHEN LOWER(CAST(mp.category AS text)) <> 'maintenance' THEN mp.amount ELSE 0 END), 0)
    INTO   v_other_income
    FROM   maintenance_payments mp
    WHERE  mp.society_id = p_society_id
      AND  mp.is_deleted = false
      AND  mp.payment_date >= v_start_date::timestamp
      AND  mp.payment_date <  v_end_exclusive::timestamp;

    v_closing_bal := v_opening_bal + v_collected + v_other_income - v_expenses;

    SELECT COUNT(*)
    INTO   v_total_flats
    FROM   flats f
    WHERE  f.society_id = p_society_id
      AND  f.is_deleted = false;

    SELECT
        json_agg(row_to_json(fd) ORDER BY fd.flat_no),
        COUNT(*) FILTER (WHERE fd.status = 'paid' OR fd.status = 'current_paid'),
        COUNT(*) FILTER (WHERE fd.status IN ('partial','unpaid')),
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
                COALESCE(SUM(CASE WHEN LOWER(CAST(mp.category AS text)) = 'maintenance'
                                     AND mp.payment_date <  v_start_date::timestamp THEN mp.amount END), 0) AS prior_paid,
                COALESCE(SUM(CASE WHEN LOWER(CAST(mp.category AS text)) = 'maintenance'
                                     AND mp.payment_date >= v_start_date::timestamp
                                     AND mp.payment_date <  v_end_exclusive::timestamp THEN mp.amount END), 0) AS current_paid,
                COALESCE(SUM(CASE WHEN LOWER(CAST(mp.category AS text)) = 'maintenance'
                                     AND mp.payment_date <  v_end_exclusive::timestamp THEN mp.amount END), 0) AS total_paid
            FROM   maintenance_payments mp
            WHERE  mp.society_id = p_society_id
              AND  mp.is_deleted  = false
            GROUP  BY mp.flat_id
        )
        SELECT
            f.flat_no,
            f.owner_name,
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.prior_billed, 0)
              - COALESCE(pa.prior_paid,   0)
            ) AS opening_balance,
            COALESCE(ba.current_billed, 0) AS current_bill,
            COALESCE(pa.current_paid,   0) AS current_paid,
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.prior_billed, 0)
              - COALESCE(pa.prior_paid,   0)
              + COALESCE(ba.current_billed, 0)
            ) AS total_due,
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.total_billed,  0)
              - COALESCE(pa.total_paid,    0)
            ) AS balance_amount,
            CASE
              WHEN (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) <= 0
                THEN 'paid'
              WHEN COALESCE(ba.current_billed, 0) = 0
               AND (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) > 0
                THEN 'unpaid'
              WHEN COALESCE(pa.current_paid, 0) >= COALESCE(ba.current_billed, 0)
               AND COALESCE(ba.current_billed, 0) > 0
               AND (COALESCE(oa.adj_original, 0) + COALESCE(ba.total_billed, 0) - COALESCE(pa.total_paid, 0)) > 0
                THEN 'current_paid'
              WHEN COALESCE(pa.current_paid, 0) > 0
                THEN 'partial'
              ELSE 'unpaid'
            END AS status
        FROM   flats f
        LEFT   JOIN opening_agg oa ON oa.flat_id = f.id
        LEFT   JOIN bill_agg    ba ON ba.flat_id = f.id
        LEFT   JOIN payment_agg pa ON pa.flat_id = f.id
        WHERE  f.society_id = p_society_id
          AND  f.is_deleted  = false
    ) fd;

    v_collection_eff :=
        CASE WHEN v_total_billed > 0
             THEN ROUND((v_collected / v_total_billed) * 100, 2)
             ELSE 0
        END;

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

    SELECT json_agg(row_to_json(d))
    INTO   v_income_rows
    FROM (
        SELECT
            f.flat_no                                    AS flat_no,
            f.owner_name                                 AS owner_name,
            f.tenant_name                                AS tenant_name,
            mp.payment_date                              AS date_paid,
            CASE mp.category::text
                WHEN 'maintenance' THEN 'Maintenance'
                WHEN 'lift_usage_charges' THEN 'Lift Usage Charges'
                WHEN 'parking_income' THEN 'Parking Income'
                WHEN 'bank_interest' THEN 'Bank Interest'
                WHEN 'other_income' THEN 'Other Income'
                ELSE initcap(replace(mp.category::text, '_', ' '))
            END AS category_name,
            COALESCE(NULLIF(trim(mp.notes), ''), NULLIF(trim(mp.reference_number), ''), 'Income') AS description,
            mp.amount                                    AS amount
        FROM   maintenance_payments mp
        LEFT   JOIN flats f ON f.id = mp.flat_id
        WHERE  mp.society_id = p_society_id
          AND  mp.is_deleted = false
          AND  mp.payment_date >= v_start_date::timestamp
          AND  mp.payment_date <  v_end_exclusive::timestamp
          AND  LOWER(CAST(mp.category AS text)) <> 'maintenance'
        ORDER  BY mp.payment_date ASC, category_name, COALESCE(NULLIF(trim(mp.notes), ''), ''), mp.id ASC
    ) d;

    v_summary :=
        'Total collection ₹' || v_collected ||
        ', expenses ₹'       || v_expenses  ||
        '. '                 || v_pending_count || ' flat(s) have pending dues.';

    v_alerts :=
        CASE WHEN v_pending_count > 0
             THEN json_build_array(v_pending_count || ' flat(s) have pending payments')
             ELSE json_build_array('All flats have cleared dues')
        END;

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
            'collection_efficiency', v_collection_eff,
            'other_income',          v_other_income
        ),

        'flat_details',  COALESCE(v_flat_rows,   '[]'::json),
        'expenses',      COALESCE(v_expense_rows, '[]'::json),
        'income_details', COALESCE(v_income_rows, '[]'::json),
        'summary',       v_summary,
        'alerts',        v_alerts
    );

END;
$$;

ALTER FUNCTION public.get_monthly_report(p_society_id bigint, p_year integer, p_month integer) OWNER TO postgres;

-- Down
-- DROP FUNCTION IF EXISTS public.get_monthly_report(bigint, integer, integer);
