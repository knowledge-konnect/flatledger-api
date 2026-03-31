-- WARNING: This schema is for context only and is not meant to be run.
-- Table order and constraints may not be valid for execution.

CREATE TABLE public.adjustments (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  society_id bigint NOT NULL,
  flat_id bigint,
  amount numeric NOT NULL,
  reason text,
  created_by bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  entry_type text NOT NULL DEFAULT 'manual'::text,
  period text,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  remaining_amount numeric NOT NULL,
  CONSTRAINT adjustments_pkey PRIMARY KEY (id),
  CONSTRAINT adjustments_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id),
  CONSTRAINT adjustments_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id),
  CONSTRAINT adjustments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.admin_users (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  email character varying NOT NULL,
  password_hash text NOT NULL,
  name character varying NOT NULL,
  is_active boolean NOT NULL DEFAULT true,
  last_login timestamp with time zone,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT admin_users_pkey PRIMARY KEY (id)
);
CREATE TABLE public.attachments (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  society_id bigint NOT NULL,
  object_key text NOT NULL,
  file_name text,
  mime_type text,
  file_size bigint,
  checksum text,
  uploaded_by bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  CONSTRAINT attachments_pkey PRIMARY KEY (id),
  CONSTRAINT attachments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id),
  CONSTRAINT attachments_uploaded_by_fkey FOREIGN KEY (uploaded_by) REFERENCES public.users(id)
);
CREATE TABLE public.audit_logs (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  society_id bigint,
  table_name text NOT NULL,
  record_id bigint,
  record_public_id uuid,
  action text NOT NULL,
  changed_by bigint,
  changed_at timestamp with time zone NOT NULL DEFAULT now(),
  diff jsonb,
  metadata jsonb,
  CONSTRAINT audit_logs_pkey PRIMARY KEY (id)
);
CREATE TABLE public.bill_items (
  id bigint NOT NULL DEFAULT nextval('bill_items_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  bill_id bigint NOT NULL,
  component_name text NOT NULL,
  calculation_type text NOT NULL,
  rate numeric,
  quantity numeric,
  amount numeric NOT NULL,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT bill_items_pkey PRIMARY KEY (id),
  CONSTRAINT bill_items_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id)
);
CREATE TABLE public.bill_payment_allocations (
  id bigint NOT NULL DEFAULT nextval('bill_payment_allocations_id_seq'::regclass),
  payment_id bigint NOT NULL,
  bill_id bigint NOT NULL,
  allocated_amount numeric NOT NULL,
  CONSTRAINT bill_payment_allocations_pkey PRIMARY KEY (id),
  CONSTRAINT bill_payment_allocations_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id),
  CONSTRAINT bill_payment_allocations_payment_id_fkey FOREIGN KEY (payment_id) REFERENCES public.maintenance_payments(id)
);
CREATE TABLE public.bill_statuses (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL,
  display_name text NOT NULL,
  CONSTRAINT bill_statuses_pkey PRIMARY KEY (id)
);
CREATE TABLE public.bills (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  flat_id bigint NOT NULL,
  period text NOT NULL CHECK (period IS NOT NULL AND length(TRIM(BOTH FROM period)) > 0),
  amount numeric NOT NULL CHECK (amount >= 0::numeric),
  due_date date,
  status_code text NOT NULL DEFAULT 'unpaid'::text CHECK (status_code = ANY (ARRAY['unpaid'::text, 'partial'::text, 'paid'::text, 'overdue'::text])),
  generated_by bigint,
  generated_at timestamp with time zone NOT NULL DEFAULT now(),
  note text,
  source text,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  maintenance_plan_id bigint,
  paid_amount numeric DEFAULT 0,
  balance_amount numeric DEFAULT (amount - paid_amount),
  updated_at timestamp with time zone DEFAULT now(),
  CONSTRAINT bills_pkey PRIMARY KEY (id),
  CONSTRAINT bills_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id),
  CONSTRAINT bills_generated_by_fkey FOREIGN KEY (generated_by) REFERENCES public.users(id),
  CONSTRAINT bills_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id),
  CONSTRAINT fk_bill_plan FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id)
);
CREATE TABLE public.expense_categories (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL,
  display_name text NOT NULL,
  CONSTRAINT expense_categories_pkey PRIMARY KEY (id)
);
CREATE TABLE public.expenses (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  date_incurred date NOT NULL,
  category_code text NOT NULL DEFAULT 'others'::text,
  vendor text,
  description text,
  amount numeric NOT NULL CHECK (amount >= 0::numeric),
  attachment_id bigint,
  approved_by bigint,
  status text NOT NULL DEFAULT 'recorded'::text,
  created_by bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  CONSTRAINT expenses_pkey PRIMARY KEY (id),
  CONSTRAINT expenses_approved_by_fkey FOREIGN KEY (approved_by) REFERENCES public.users(id),
  CONSTRAINT expenses_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id),
  CONSTRAINT expenses_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.feature_flags (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  key character varying NOT NULL,
  description text,
  is_enabled boolean NOT NULL DEFAULT false,
  society_id bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT feature_flags_pkey PRIMARY KEY (id),
  CONSTRAINT feature_flags_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.flat_statuses (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL UNIQUE,
  display_name text NOT NULL,
  CONSTRAINT flat_statuses_pkey PRIMARY KEY (id)
);
CREATE TABLE public.flats (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  flat_no text NOT NULL,
  owner_name text,
  contact_mobile text,
  contact_email text,
  maintenance_amount numeric NOT NULL DEFAULT 0.00,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  status_id smallint,
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  area_sqft numeric,
  advance_balance numeric NOT NULL DEFAULT 0,
  opening_balance numeric NOT NULL DEFAULT 0,
  CONSTRAINT flats_pkey PRIMARY KEY (id),
  CONSTRAINT flats_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id),
  CONSTRAINT flats_status_id_fkey FOREIGN KEY (status_id) REFERENCES public.flat_statuses(id)
);
CREATE TABLE public.invoices (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  user_id bigint NOT NULL,
  subscription_id uuid,
  invoice_number character varying NOT NULL UNIQUE,
  invoice_type character varying NOT NULL DEFAULT 'subscription'::character varying CHECK (invoice_type::text = ANY (ARRAY['subscription'::character varying::text, 'renewal'::character varying::text, 'addon'::character varying::text, 'manual'::character varying::text, 'penalty'::character varying::text])),
  amount numeric NOT NULL,
  tax_amount numeric DEFAULT 0.00,
  total_amount numeric NOT NULL,
  currency character varying DEFAULT 'INR'::character varying,
  status character varying NOT NULL DEFAULT 'pending'::character varying CHECK (status::text = ANY (ARRAY['draft'::character varying::text, 'pending'::character varying::text, 'paid'::character varying::text, 'failed'::character varying::text, 'cancelled'::character varying::text, 'refunded'::character varying::text])),
  period_start date,
  period_end date,
  due_date date NOT NULL,
  paid_date timestamp with time zone,
  payment_method character varying,
  payment_reference character varying,
  description text,
  created_at timestamp with time zone DEFAULT now(),
  updated_at timestamp with time zone DEFAULT now(),
  CONSTRAINT invoices_pkey PRIMARY KEY (id),
  CONSTRAINT invoices_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id),
  CONSTRAINT invoices_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.jobs (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  society_id bigint,
  job_type text NOT NULL,
  payload jsonb,
  status text NOT NULL DEFAULT 'queued'::text,
  result jsonb,
  attempts integer NOT NULL DEFAULT 0,
  last_error text,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  CONSTRAINT jobs_pkey PRIMARY KEY (id)
);
CREATE TABLE public.maintenance_components (
  id bigint NOT NULL DEFAULT nextval('maintenance_components_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  society_id bigint NOT NULL,
  name text NOT NULL,
  component_type text NOT NULL,
  default_amount numeric,
  default_rate_per_sqft numeric,
  is_mandatory boolean DEFAULT true,
  is_deleted boolean DEFAULT false,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT maintenance_components_pkey PRIMARY KEY (id),
  CONSTRAINT maintenance_components_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.maintenance_config (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  default_monthly_charge numeric NOT NULL DEFAULT 0,
  due_day_of_month integer NOT NULL DEFAULT 1 CHECK (due_day_of_month >= 1 AND due_day_of_month <= 28),
  late_fee_per_month numeric NOT NULL DEFAULT 0,
  grace_period_days integer NOT NULL DEFAULT 0,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  created_by bigint,
  updated_by bigint,
  CONSTRAINT maintenance_config_pkey PRIMARY KEY (id),
  CONSTRAINT maintenance_config_created_by_fkey FOREIGN KEY (created_by) REFERENCES public.users(id),
  CONSTRAINT maintenance_config_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id),
  CONSTRAINT maintenance_config_updated_by_fkey FOREIGN KEY (updated_by) REFERENCES public.users(id)
);
CREATE TABLE public.maintenance_cycles (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL UNIQUE,
  display_name text NOT NULL,
  description text,
  is_active boolean NOT NULL DEFAULT true,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT maintenance_cycles_pkey PRIMARY KEY (id)
);
CREATE TABLE public.maintenance_payments (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  flat_id bigint NOT NULL,
  bill_id bigint CHECK (bill_id IS NULL OR bill_id > 0),
  amount numeric NOT NULL CHECK (amount > 0::numeric),
  payment_date timestamp with time zone NOT NULL,
  payment_mode_id smallint NOT NULL,
  reference_number text,
  receipt_url text,
  notes text,
  recorded_by bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  idempotency_key text,
  adjustment_id bigint,
  CONSTRAINT maintenance_payments_pkey PRIMARY KEY (id),
  CONSTRAINT fk_maintenance_adjustment FOREIGN KEY (adjustment_id) REFERENCES public.adjustments(id),
  CONSTRAINT fk_maintenance_bill FOREIGN KEY (bill_id) REFERENCES public.bills(id),
  CONSTRAINT fk_maintenance_flat FOREIGN KEY (flat_id) REFERENCES public.flats(id),
  CONSTRAINT fk_maintenance_payment_mode FOREIGN KEY (payment_mode_id) REFERENCES public.payment_modes(id),
  CONSTRAINT fk_maintenance_recorded_by FOREIGN KEY (recorded_by) REFERENCES public.users(id),
  CONSTRAINT fk_maintenance_society FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.maintenance_plans (
  id bigint NOT NULL DEFAULT nextval('maintenance_plans_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  society_id bigint NOT NULL,
  name text NOT NULL,
  calculation_type text NOT NULL,
  fixed_amount numeric DEFAULT 0,
  rate_per_sqft numeric DEFAULT 0,
  effective_from date NOT NULL,
  effective_to date,
  is_active boolean DEFAULT true,
  is_deleted boolean DEFAULT false,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT maintenance_plans_pkey PRIMARY KEY (id),
  CONSTRAINT fk_maintenance_plan_society FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.maintenance_rate_history (
  id bigint NOT NULL DEFAULT nextval('maintenance_rate_history_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  maintenance_plan_id bigint NOT NULL,
  old_fixed_amount numeric,
  old_rate_per_sqft numeric,
  changed_by bigint,
  changed_at timestamp with time zone DEFAULT now(),
  CONSTRAINT maintenance_rate_history_pkey PRIMARY KEY (id),
  CONSTRAINT maintenance_rate_history_maintenance_plan_id_fkey FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id)
);
CREATE TABLE public.notification_preferences (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  user_id bigint NOT NULL,
  payment_reminders boolean NOT NULL DEFAULT true,
  bill_generated boolean NOT NULL DEFAULT true,
  expense_updates boolean NOT NULL DEFAULT true,
  monthly_reports boolean NOT NULL DEFAULT true,
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT notification_preferences_pkey PRIMARY KEY (id),
  CONSTRAINT notification_preferences_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.payment_modes (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL,
  display_name text NOT NULL,
  CONSTRAINT payment_modes_pkey PRIMARY KEY (id)
);
CREATE TABLE public.payments (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  bill_id bigint,
  flat_id bigint,
  amount numeric NOT NULL,
  date_paid timestamp with time zone,
  mode_code text,
  reference text,
  receipt_url text,
  recorded_by bigint,
  idempotency_key text,
  reversed_by_payment_id bigint,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  razorpay_order_id text,
  razorpay_payment_id text,
  razorpay_signature text,
  payment_type text CHECK (payment_type = ANY (ARRAY['bill'::text, 'subscription'::text])),
  verified_at timestamp with time zone,
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  CONSTRAINT payments_pkey PRIMARY KEY (id),
  CONSTRAINT payments_bill_id_fkey FOREIGN KEY (bill_id) REFERENCES public.bills(id),
  CONSTRAINT payments_flat_id_fkey FOREIGN KEY (flat_id) REFERENCES public.flats(id),
  CONSTRAINT payments_recorded_by_fkey FOREIGN KEY (recorded_by) REFERENCES public.users(id),
  CONSTRAINT payments_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);
CREATE TABLE public.plan_components (
  id bigint NOT NULL DEFAULT nextval('plan_components_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid() UNIQUE,
  maintenance_plan_id bigint NOT NULL,
  maintenance_component_id bigint NOT NULL,
  amount numeric,
  rate_per_sqft numeric,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT plan_components_pkey PRIMARY KEY (id),
  CONSTRAINT plan_components_maintenance_component_id_fkey FOREIGN KEY (maintenance_component_id) REFERENCES public.maintenance_components(id),
  CONSTRAINT plan_components_maintenance_plan_id_fkey FOREIGN KEY (maintenance_plan_id) REFERENCES public.maintenance_plans(id)
);
CREATE TABLE public.plans (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  name character varying NOT NULL,
  monthly_amount numeric NOT NULL,
  currency character varying NOT NULL DEFAULT 'INR'::character varying,
  is_active boolean DEFAULT true,
  created_at timestamp with time zone DEFAULT now(),
  duration_months integer NOT NULL DEFAULT 1,
  CONSTRAINT plans_pkey PRIMARY KEY (id)
);
CREATE TABLE public.platform_settings (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  key character varying NOT NULL,
  value text,
  description text,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  CONSTRAINT platform_settings_pkey PRIMARY KEY (id)
);
CREATE TABLE public.refresh_tokens (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  user_id bigint NOT NULL,
  token_hash text NOT NULL,
  jwt_id text,
  expires_at timestamp with time zone NOT NULL,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  created_by_ip text,
  is_revoked boolean NOT NULL DEFAULT false,
  revoked_at timestamp with time zone,
  replaced_by_token_hash text,
  CONSTRAINT refresh_tokens_pkey PRIMARY KEY (id),
  CONSTRAINT refresh_tokens_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.roles (
  id smallint GENERATED ALWAYS AS IDENTITY NOT NULL,
  code text NOT NULL,
  display_name text NOT NULL,
  CONSTRAINT roles_pkey PRIMARY KEY (id)
);
CREATE TABLE public.societies (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  name text NOT NULL,
  address text,
  city text,
  state text,
  pincode text,
  currency text NOT NULL DEFAULT 'INR'::text,
  default_maintenance_cycle text NOT NULL DEFAULT 'monthly'::text,
  billing_plan_id integer,
  settings jsonb DEFAULT '{}'::jsonb,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  maintenance_cycle_id smallint,
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  onboarding_date date NOT NULL DEFAULT CURRENT_DATE,
  CONSTRAINT societies_pkey PRIMARY KEY (id),
  CONSTRAINT societies_maintenance_cycle_id_fkey FOREIGN KEY (maintenance_cycle_id) REFERENCES public.maintenance_cycles(id)
);
CREATE TABLE public.society_fund_ledger (
  id bigint NOT NULL DEFAULT nextval('society_fund_ledger_id_seq'::regclass),
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  amount numeric NOT NULL,
  entry_type text NOT NULL,
  reference text,
  notes text,
  created_by bigint NOT NULL,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  is_deleted boolean DEFAULT false,
  transaction_date date DEFAULT CURRENT_DATE,
  CONSTRAINT society_fund_ledger_pkey PRIMARY KEY (id)
);
CREATE TABLE public.subscription_events (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  user_id bigint NOT NULL,
  subscription_id uuid,
  event_type character varying NOT NULL,
  old_status character varying,
  new_status character varying,
  amount numeric,
  metadata jsonb,
  created_at timestamp with time zone DEFAULT now(),
  CONSTRAINT subscription_events_pkey PRIMARY KEY (id),
  CONSTRAINT subscription_events_subscription_id_fkey FOREIGN KEY (subscription_id) REFERENCES public.subscriptions(id),
  CONSTRAINT subscription_events_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.subscriptions (
  id uuid NOT NULL DEFAULT gen_random_uuid(),
  user_id bigint NOT NULL,
  plan_id uuid NOT NULL,
  status character varying NOT NULL DEFAULT 'trial'::character varying CHECK (status::text = ANY (ARRAY['trial'::character varying::text, 'active'::character varying::text, 'expired'::character varying::text, 'past_due'::character varying::text, 'cancelled'::character varying::text])),
  subscribed_amount numeric NOT NULL,
  currency character varying DEFAULT 'INR'::character varying,
  current_period_start timestamp with time zone,
  current_period_end timestamp with time zone,
  trial_start timestamp with time zone DEFAULT now(),
  trial_end timestamp with time zone DEFAULT (now() + '30 days'::interval),
  cancelled_at timestamp with time zone,
  created_at timestamp with time zone DEFAULT now(),
  updated_at timestamp with time zone DEFAULT now(),
  CONSTRAINT subscriptions_pkey PRIMARY KEY (id),
  CONSTRAINT subscriptions_plan_id_fkey FOREIGN KEY (plan_id) REFERENCES public.plans(id),
  CONSTRAINT subscriptions_user_id_fkey FOREIGN KEY (user_id) REFERENCES public.users(id)
);
CREATE TABLE public.users (
  id bigint GENERATED ALWAYS AS IDENTITY NOT NULL,
  public_id uuid NOT NULL DEFAULT gen_random_uuid(),
  society_id bigint NOT NULL,
  name text NOT NULL,
  email text,
  mobile text,
  role_id smallint NOT NULL,
  password_hash text,
  is_active boolean NOT NULL DEFAULT true,
  last_login timestamp with time zone,
  created_at timestamp with time zone NOT NULL DEFAULT now(),
  updated_at timestamp with time zone NOT NULL DEFAULT now(),
  force_password_change boolean NOT NULL DEFAULT false,
  created_by uuid,
  updated_by uuid,
  trial_start_date timestamp without time zone,
  subscription_status character varying DEFAULT 'trial'::character varying,
  subscription_start_date timestamp without time zone,
  next_billing_date timestamp without time zone,
  trial_ends_date timestamp with time zone DEFAULT (now() + '30 days'::interval),
  last_payment_date timestamp with time zone,
  subscription_plan character varying DEFAULT 'pro'::character varying,
  monthly_amount numeric DEFAULT 299.00,
  is_deleted boolean NOT NULL DEFAULT false,
  deleted_at timestamp with time zone,
  username character varying UNIQUE,
  CONSTRAINT users_pkey PRIMARY KEY (id),
  CONSTRAINT users_role_id_fkey FOREIGN KEY (role_id) REFERENCES public.roles(id),
  CONSTRAINT users_society_id_fkey FOREIGN KEY (society_id) REFERENCES public.societies(id)
);