-- Migration: index society_fund_ledger.society_id for dashboard/report query performance
-- Deployed: 2026-05-15
-- Audit item H-9: full table scans on society_fund_ledger because society_id has no index.

-- Create index concurrently (non-blocking on live DB) for the critical filter column.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_society_fund_ledger_society_id
    ON society_fund_ledger (society_id)
    WHERE is_deleted IS DISTINCT FROM TRUE;

-- Composite index for the most common query pattern: filter by society + order by transaction_date.
CREATE INDEX CONCURRENTLY IF NOT EXISTS idx_society_fund_ledger_society_date
    ON society_fund_ledger (society_id, transaction_date DESC)
    WHERE is_deleted IS DISTINCT FROM TRUE;
