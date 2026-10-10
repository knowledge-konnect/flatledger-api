-- Migration: 2026-09-08 Add payment_category enum and category column to maintenance_payments
-- Up
DO $$
BEGIN
  IF NOT EXISTS (SELECT 1 FROM pg_type WHERE typname = 'payment_category') THEN
    CREATE TYPE payment_category AS ENUM (
      'maintenance',
      'lift_usage_charges',
      'parking_income',
      'bank_interest',
      'other_income'
    );
  END IF;
END;
$$;

ALTER TABLE maintenance_payments
  ADD COLUMN IF NOT EXISTS category payment_category NOT NULL DEFAULT 'maintenance';

UPDATE maintenance_payments
SET category = 'lift_usage_charges'
WHERE category::text = 'moving_charges';

-- Down
-- To rollback, run the following statements (careful: data will be lost):
-- ALTER TABLE maintenance_payments DROP COLUMN IF EXISTS category;
-- DO $$
-- BEGIN
--   IF EXISTS (SELECT 1 FROM pg_type WHERE typname = 'payment_category') THEN
--     DROP TYPE payment_category;
--   END IF;
-- END;
-- $$;
