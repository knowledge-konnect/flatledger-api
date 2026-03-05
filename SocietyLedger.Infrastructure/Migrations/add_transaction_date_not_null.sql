-- =============================================================================
-- Migration: add_transaction_date_not_null
-- Description:
--   1. Back-fills NULL transaction_date values in society_fund_ledger using
--      the existing created_at audit timestamp (cast to date).
--   2. Alters the column to NOT NULL — from this point forward every INSERT
--      must supply an explicit transaction_date.
--   3. Ensures the partial unique index that prevents duplicate opening_fund
--      entries per society is in place (idempotent).
--
-- Run order: after the column was originally added as nullable.
-- Safe to re-run (all statements are idempotent or guarded).
-- =============================================================================

BEGIN;

-- ── Step 1: Back-fill: use created_at::date for any row still NULL ───────────
-- This preserves the original financial intent for rows inserted before this
-- migration; they had no explicit transaction_date so created_at is our best
-- approximation.
UPDATE public.society_fund_ledger
SET    transaction_date = created_at::date
WHERE  transaction_date IS NULL;

-- ── Step 2: Enforce NOT NULL at the database level ───────────────────────────
ALTER TABLE public.society_fund_ledger
    ALTER COLUMN transaction_date SET NOT NULL;

-- ── Step 3: Idempotent unique partial index for opening_fund entries ─────────
-- Prevents a second non-deleted opening_fund row per society even if the
-- application-level duplicate guard is bypassed (e.g. concurrent requests).
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM   pg_indexes
        WHERE  schemaname = 'public'
          AND  tablename  = 'society_fund_ledger'
          AND  indexname  = 'uq_society_single_opening'
    ) THEN
        CREATE UNIQUE INDEX uq_society_single_opening
            ON public.society_fund_ledger (society_id)
            WHERE entry_type = 'opening_fund'
              AND COALESCE(is_deleted, false) = false;
    END IF;
END;
$$;

-- ── Step 4: Supporting index for report date-range queries ───────────────────
-- Covering index on (society_id, transaction_date) so date-range report
-- queries (get_fund_ledger_report, get_income_vs_expense, etc.) avoid full
-- table scans.  is_deleted included to allow index-only filtering.
DO $$
BEGIN
    IF NOT EXISTS (
        SELECT 1
        FROM   pg_indexes
        WHERE  schemaname = 'public'
          AND  tablename  = 'society_fund_ledger'
          AND  indexname  = 'ix_fund_ledger_society_txdate'
    ) THEN
        CREATE INDEX ix_fund_ledger_society_txdate
            ON public.society_fund_ledger (society_id, transaction_date)
            WHERE COALESCE(is_deleted, false) = false;
    END IF;
END;
$$;

COMMIT;
