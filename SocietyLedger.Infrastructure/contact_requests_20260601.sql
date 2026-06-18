-- Migration: contact_requests table
-- Date: 2026-06-01
-- Description: Persists Contact Us form submissions for record-keeping and follow-up.

CREATE TABLE IF NOT EXISTS contact_requests (
    id          BIGINT GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
    public_id   UUID         NOT NULL DEFAULT gen_random_uuid(),
    name        VARCHAR(100) NOT NULL,
    email       VARCHAR(255) NOT NULL,
    subject     VARCHAR(200),
    message     TEXT         NOT NULL,
    status      VARCHAR(20)  NOT NULL DEFAULT 'New',
    created_at  TIMESTAMPTZ  NOT NULL DEFAULT now()
);

CREATE UNIQUE INDEX IF NOT EXISTS ux_contact_requests_public_id
    ON contact_requests (public_id);

CREATE INDEX IF NOT EXISTS idx_contact_requests_created_at
    ON contact_requests (created_at DESC);

CREATE INDEX IF NOT EXISTS idx_contact_requests_status
    ON contact_requests (status);

COMMENT ON TABLE  contact_requests             IS 'Stores Contact Us form submissions.';
COMMENT ON COLUMN contact_requests.status      IS 'Lifecycle status: New | InProgress | Resolved';
COMMENT ON COLUMN contact_requests.public_id   IS 'Externally-safe UUID identifier.';
