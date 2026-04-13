-- =============================================================================
-- Function : public.get_monthly_report(p_society_id, p_year, p_month)
-- Returns  : JSON matching MonthlyReportDto (C# / .NET API)
--
-- ── Accounting model ──────────────────────────────────────────────────────────
--
--  Pre-system arrears are stored in the `adjustments` table
--  (entry_type = 'opening_balance').  `adjustments.amount` is the ORIGINAL
--  amount and never changes.  `adjustments.remaining_amount` is a live field
--  decremented by FIFO allocation in MaintenancePaymentService — we do NOT use
--  it for balance calculations here because mixing a live balance with raw
--  payment sums causes double-counting.
--
--  The correct, double-entry formula for any per-flat balance is:
--
--    balance = Σ adjustments.amount          ← original pre-system arrear
--            + Σ bills.amount (period ≤ P)  ← everything ever billed
--            − Σ maintenance_payments.amount ← everything ever received
--
--  This is allocation-agnostic: it does not matter whether a payment reduced
--  an adjustment row, a bill row or was recorded as advance credit — the net
--  position is always correct.
--
-- ── Per-flat field definitions ────────────────────────────────────────────────
--
--  opening_balance  = adj_original + prior_period_bills − prior_payments
--                     "What this flat owed at the START of the period"
--
--  current_bill     = sum of bills raised for this period
--
--  current_paid     = all money received from this flat within the period
--                     (informational — regardless of what it was applied to)
--
--  total_due        = opening_balance + current_bill
--                     "Total amount owed this period BEFORE payment (arrear + current bill)"
--                     This is always >= 0 and represents the full liability this period.
--
--  balance_amount   = adj_original + total_billed_to_period − total_paid_to_period
--                     "Grand net outstanding (closing balance)"
--                     Positive  = outstanding dues (includes legacy arrear)
--                     Zero/Neg  = fully clear or in advance credit
--
--  status           = based on THIS period's payment behaviour
--                     'paid'    : current_paid >= current_bill  OR  balance <= 0
--                     'partial' : 0 < current_paid < current_bill
--                     'unpaid'  : no payment received AND a bill exists
--
-- ── Society fund position ─────────────────────────────────────────────────────
--
--  fund_position uses society_fund_ledger (opening_fund seed) + all
--  maintenance_payments + expenses — independent of per-flat ledger.
--
-- ── Safety ───────────────────────────────────────────────────────────────────
--  • Pure-CTE, no TEMP TABLE — safe for connection-pool reuse.
--  • JSON keys match MonthlyReportDto properties exactly (snake_case).
-- =============================================================================

CREATE OR REPLACE FUNCTION public.get_monthly_report(
    p_society_id bigint,
    p_year       int,
    p_month      int
)
RETURNS json
LANGUAGE plpgsql
STABLE
AS $$
DECLARE
    v_period         text;
    v_start_date     date;
    v_end_date       date;
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
    v_total_billed   numeric := 0;   -- sum of current-period bills across all flats
    v_pending_amount numeric := 0;   -- sum of balance_amount for partial/unpaid flats
    v_collection_eff numeric := 0;   -- v_collected / v_total_billed * 100

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

    -- ── 2. Society name ───────────────────────────────────────────────────────
    SELECT s.name
    INTO   v_society_name
    FROM   societies s
    WHERE  s.id = p_society_id
      AND  s.is_deleted = false;

    -- ── 3. Society fund opening balance (before selected month) ──────────────
    --  = opening_fund seed  +  all prior maintenance receipts  −  all prior expenses
    SELECT COALESCE(SUM(sfl.amount), 0)
    INTO   v_opening_bal
    FROM   society_fund_ledger sfl
    WHERE  sfl.society_id = p_society_id
      AND  sfl.is_deleted = false
      AND  sfl.entry_type = 'opening_fund';

    SELECT v_opening_bal + COALESCE(SUM(mp.amount), 0)
    INTO   v_opening_bal
    FROM   maintenance_payments mp
    WHERE  mp.society_id = p_society_id
      AND  mp.is_deleted = false
      AND  DATE(mp.payment_date) < v_start_date;

    SELECT v_opening_bal - COALESCE(SUM(e.amount), 0)
    INTO   v_opening_bal
    FROM   expenses e
    WHERE  e.society_id    = p_society_id
      AND  e.is_deleted    = false
      AND  e.date_incurred < v_start_date;

    -- ── 4. Current-month society totals ──────────────────────────────────────
    SELECT COALESCE(SUM(mp.amount), 0)
    INTO   v_collected
    FROM   maintenance_payments mp
    WHERE  mp.society_id = p_society_id
      AND  mp.is_deleted = false
      AND  DATE(mp.payment_date) BETWEEN v_start_date AND v_end_date;

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
    --  Three CTEs are joined to every flat:
    --
    --  opening_agg  — original adjustment amounts (pre-system arrear seed).
    --                 Uses adjustments.amount (NEVER remaining_amount) so the
    --                 formula adj + billed − paid stays self-consistent.
    --
    --  bill_agg     — bill amounts split into three buckets:
    --                   prior_billed  : period < v_period
    --                   current_billed: period = v_period
    --                   total_billed  : period ≤ v_period
    --
    --  payment_agg  — ALL maintenance_payment rows (bill, adjustment, advance)
    --                 split by payment_date into:
    --                   prior_paid    : before v_start_date
    --                   current_paid  : within [v_start_date, v_end_date]
    --                   total_paid    : up to v_end_date
    --
    SELECT
        json_agg(row_to_json(fd) ORDER BY fd.flat_no),
        COUNT(*)           FILTER (WHERE fd.status = 'paid'),
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
                COALESCE(SUM(CASE WHEN DATE(mp.payment_date) <  v_start_date                     THEN mp.amount END), 0) AS prior_paid,
                COALESCE(SUM(CASE WHEN DATE(mp.payment_date) BETWEEN v_start_date AND v_end_date  THEN mp.amount END), 0) AS current_paid,
                COALESCE(SUM(CASE WHEN DATE(mp.payment_date) <= v_end_date                        THEN mp.amount END), 0) AS total_paid
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
            (   COALESCE(oa.adj_original,   0)
              + COALESCE(ba.prior_billed,   0)
              - COALESCE(pa.prior_paid,     0)
            )                                      AS opening_balance,

            COALESCE(ba.current_billed, 0)         AS current_bill,
            COALESCE(pa.current_paid,   0)         AS current_paid,

            -- Total due = opening balance + current bill
            -- = full liability for this period BEFORE deducting payment
            -- Closing balance = total_due - current_paid
            (   COALESCE(oa.adj_original,   0)
              + COALESCE(ba.prior_billed,   0)
              - COALESCE(pa.prior_paid,     0)
              + COALESCE(ba.current_billed, 0)
            )                                      AS total_due,

            -- Closing balance = grand net outstanding position
            -- (original arrear + everything ever billed − everything ever paid)
            (   COALESCE(oa.adj_original, 0)
              + COALESCE(ba.total_billed, 0)
              - COALESCE(pa.total_paid,   0)
            )                                      AS balance_amount,

            -- Status: reflect overall balance first, then monthly payment behaviour
            --   'paid'    : net balance_amount <= 0 (fully cleared or in advance)
            --   'partial' : some payment received this period but outstanding remains
            --   'unpaid'  : no payment received this period and there is an outstanding amount
            CASE
              -- fully cleared or in advance credit
              WHEN (COALESCE(oa.adj_original,0) + COALESCE(ba.total_billed,0) - COALESCE(pa.total_paid,0)) <= 0
                THEN 'paid'

              -- no current bill and still has outstanding (treated as unpaid)
              WHEN COALESCE(ba.current_billed, 0) = 0 AND (COALESCE(oa.adj_original,0) + COALESCE(ba.total_billed,0) - COALESCE(pa.total_paid,0)) > 0
                THEN 'unpaid'

              -- paid current month's bill but outstanding remains (arrears cleared partially)
              WHEN COALESCE(pa.current_paid, 0) >= COALESCE(ba.current_billed, 0)
                 AND (COALESCE(oa.adj_original,0) + COALESCE(ba.total_billed,0) - COALESCE(pa.total_paid,0)) > 0
                THEN 'partial'

              -- some payment this period but less than current bill
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

    -- ── 8. Expenses by category ───────────────────────────────────────────────
    SELECT json_agg(row_to_json(d))
    INTO   v_expense_rows
    FROM (
        SELECT
            COALESCE(ec.display_name, e.category_code) AS category_name,
            SUM(e.amount)                               AS total_amount
        FROM   expenses e
        LEFT   JOIN expense_categories ec ON ec.code = e.category_code
        WHERE  e.society_id    = p_society_id
          AND  e.is_deleted    = false
          AND  e.date_incurred BETWEEN v_start_date AND v_end_date
        GROUP  BY e.category_code, ec.display_name
        ORDER  BY total_amount DESC
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
