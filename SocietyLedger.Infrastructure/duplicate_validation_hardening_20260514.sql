-- Duplicate validation hardening (tenant scope + soft-delete aware uniqueness)
-- Date: 2026-05-14
-- Safe to run once in a maintenance window.

BEGIN;

-- 1) USERS: normalize username and enforce per-society uniqueness for active records.
UPDATE public.users
SET username = NULLIF(lower(btrim(username)), '')
WHERE username IS NOT NULL;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM public.users u
    WHERE u.username IS NOT NULL
      AND u.is_deleted = false
    GROUP BY u.society_id, u.username
    HAVING COUNT(*) > 1
  ) THEN
    RAISE EXCEPTION 'Cannot create users_society_username_key: duplicate active usernames exist within a society.';
  END IF;
END $$;

ALTER TABLE public.users DROP CONSTRAINT IF EXISTS users_username_key;
DROP INDEX IF EXISTS public.users_username_key;
DROP INDEX IF EXISTS public.users_society_username_key;
CREATE UNIQUE INDEX users_society_username_key
  ON public.users (society_id, username)
  WHERE username IS NOT NULL AND is_deleted = false;

-- 2) FLATS: normalize flat_no and enforce uniqueness per society for active records only.
UPDATE public.flats
SET flat_no = upper(btrim(flat_no));

DO $$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM public.flats f
    WHERE f.is_deleted = false
    GROUP BY f.society_id, f.flat_no
    HAVING COUNT(*) > 1
  ) THEN
    RAISE EXCEPTION 'Cannot create filtered flats_society_id_flat_no_key: duplicate active flat numbers exist.';
  END IF;
END $$;

ALTER TABLE public.flats DROP CONSTRAINT IF EXISTS flats_society_id_flat_no_key;
DROP INDEX IF EXISTS public.flats_society_id_flat_no_key;
CREATE UNIQUE INDEX flats_society_id_flat_no_key
  ON public.flats (society_id, flat_no)
  WHERE is_deleted = false;

-- 3) INVOICES: replace global invoice_number uniqueness with per-user uniqueness.
UPDATE public.invoices
SET invoice_number = btrim(invoice_number)
WHERE invoice_number IS NOT NULL;

DO $$
BEGIN
  IF EXISTS (
    SELECT 1
    FROM public.invoices i
    GROUP BY i.user_id, i.invoice_number
    HAVING COUNT(*) > 1
  ) THEN
    RAISE EXCEPTION 'Cannot create invoices_user_invoice_number_key: duplicate invoice numbers exist for the same user.';
  END IF;
END $$;

ALTER TABLE public.invoices DROP CONSTRAINT IF EXISTS invoices_invoice_number_key;
DROP INDEX IF EXISTS public.invoices_invoice_number_key;
DROP INDEX IF EXISTS public.invoices_user_invoice_number_key;
CREATE UNIQUE INDEX invoices_user_invoice_number_key
  ON public.invoices (user_id, invoice_number);

COMMIT;
