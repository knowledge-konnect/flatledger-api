-- ============================================================
-- Migration: Add maintenance_config and notification_preferences tables
-- Date: 2026-02-27
-- ============================================================

-- -----------------------------------------------------------
-- 1. maintenance_config
--    One row per society; stores default billing configuration.
-- -----------------------------------------------------------
CREATE TABLE IF NOT EXISTS maintenance_config (
    id                      BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id               UUID NOT NULL DEFAULT gen_random_uuid(),
    society_id              BIGINT NOT NULL REFERENCES societies(id) ON DELETE CASCADE,
    default_monthly_charge  NUMERIC(13,2) NOT NULL DEFAULT 0,
    due_day_of_month        INT NOT NULL DEFAULT 1 CHECK (due_day_of_month BETWEEN 1 AND 28),
    late_fee_per_month      NUMERIC(13,2) NOT NULL DEFAULT 0,
    grace_period_days       INT NOT NULL DEFAULT 0,
    created_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    updated_at              TIMESTAMPTZ NOT NULL DEFAULT now(),
    created_by              BIGINT REFERENCES users(id),
    updated_by              BIGINT REFERENCES users(id)
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_config_society_id
    ON maintenance_config (society_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_maintenance_config_public_id
    ON maintenance_config (public_id);

-- -----------------------------------------------------------
-- 2. notification_preferences
--    One row per user; stores notification setting flags.
-- -----------------------------------------------------------
CREATE TABLE IF NOT EXISTS notification_preferences (
    id                  BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id           UUID NOT NULL DEFAULT gen_random_uuid(),
    user_id             BIGINT NOT NULL REFERENCES users(id) ON DELETE CASCADE,
    payment_reminders   BOOLEAN NOT NULL DEFAULT TRUE,
    bill_generated      BOOLEAN NOT NULL DEFAULT TRUE,
    expense_updates     BOOLEAN NOT NULL DEFAULT TRUE,
    monthly_reports     BOOLEAN NOT NULL DEFAULT TRUE,
    updated_at          TIMESTAMPTZ NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_notification_preferences_user_id
    ON notification_preferences (user_id);

CREATE UNIQUE INDEX IF NOT EXISTS ux_notification_preferences_public_id
    ON notification_preferences (public_id);
