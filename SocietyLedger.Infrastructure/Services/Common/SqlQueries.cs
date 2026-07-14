namespace SocietyLedger.Infrastructure.Services.Common
{
    /// <summary>
    /// Central location for all raw SQL queries used by DapperService and related services.
    /// </summary>
    public static class SqlQueries
    {
        // ── Maintenance-payment allocation (current month first) ───────────────────────────

        /// <summary>
        /// Checks whether a maintenance payment with the given idempotency key already exists for this society.
        /// Returns fast on duplicate submissions.
        /// </summary>
        public const string CheckMaintenancePaymentIdempotency = @"
            SELECT id
            FROM   maintenance_payments
            WHERE  society_id      = @SocietyId
              AND  idempotency_key = @IdempotencyKey
            LIMIT  1";

        /// <summary>
        /// Resolves a flat by its public UUID within the caller's society. FOR UPDATE acquires a row lock to serialise concurrent payment submissions.
        /// </summary>
        public const string LockFlatByPublicId = @"
            SELECT id, public_id, flat_no, society_id
            FROM   flats
            WHERE  public_id  = @FlatPublicId
              AND  society_id = @SocietyId
              AND  is_deleted = FALSE
            FOR UPDATE";

        /// <summary>
        /// Returns all OpeningBalance adjustment rows that still have an outstanding remaining_amount for the given flat, ordered oldest-first (FIFO).
        /// FOR UPDATE prevents concurrent payments from double-allocating the same row.
        /// </summary>
        public const string LockOpeningBalanceAdjustments = @"
            SELECT id,
                   public_id,
                   remaining_amount
            FROM   adjustments
            WHERE  flat_id          = @FlatId
              AND  society_id       = @SocietyId
              AND  entry_type       = @EntryType
              AND  remaining_amount > 0
              AND  is_deleted       = FALSE
            ORDER  BY created_at ASC
            FOR UPDATE";

        /// <summary>
        /// Deducts the allocated amount from an adjustment's remaining_amount. Runs under the same FOR UPDATE lock acquired by LockOpeningBalanceAdjustments.
        /// </summary>
        public const string DeductAdjustmentRemainingAmount = @"
            UPDATE adjustments
            SET    remaining_amount = remaining_amount - @Allocation
            WHERE  id         = @AdjustmentId
              AND  society_id = @SocietyId";

        /// <summary>
        /// Returns all bills with an outstanding balance for the given flat, ordered newest-period-first (current month first). FOR UPDATE locks each row to prevent concurrent allocations.
        /// Excludes 'paid' and 'cancelled' bills — cancelled bills are not real obligations and
        /// must never receive payment allocations.
        /// </summary>
        public const string LockUnpaidBillsByFlat = @"
            SELECT b.id,
                   b.public_id,
                   b.amount,
                   COALESCE(b.paid_amount, 0) AS paid_amount,
                   b.status_code,
                   b.period
            FROM   bills b
            WHERE  b.flat_id    = @FlatId
              AND  b.society_id = @SocietyId
              AND  b.is_deleted = FALSE
              AND  b.status_code NOT IN ('paid', 'cancelled')
              AND  (b.amount - COALESCE(b.paid_amount, 0)) > 0
            ORDER  BY b.period DESC
            FOR UPDATE";

        /// <summary>
        /// Inserts one allocation row into maintenance_payments. Each allocation step (opening balance, bill, or advance) is a separate row sharing the same idempotency_key.
        /// </summary>
        public const string InsertMaintenancePayment = @"
            INSERT INTO maintenance_payments
                (society_id, flat_id, bill_id, adjustment_id, amount, payment_date, payment_mode_id,
                 reference_number, receipt_url, notes, recorded_by, idempotency_key, created_at)
            VALUES
                (@SocietyId, @FlatId, @BillId, @AdjustmentId, @Amount, @PaymentDate, @PaymentModeId,
                 @ReferenceNumber, @ReceiptUrl, @Notes, @RecordedBy, @IdempotencyKey, @Now)
            RETURNING id";

        /// <summary>
        /// Updates the accumulated paid amount and derived status on a bill after each FIFO allocation step.
        /// Sets 'paid' when fully settled, 'overdue' when partially paid and past due date, else 'partial'.
        /// </summary>
        public const string UpdateBillPayment = @"
            UPDATE bills
            SET    paid_amount = @PaidAmount,
                   status_code = CASE
                       WHEN @PaidAmount >= amount
                           THEN 'paid'
                       WHEN due_date IS NOT NULL AND due_date < NOW()
                           THEN 'overdue'
                       ELSE 'partial'
                   END,
                   updated_at  = @Now
            WHERE  id         = @BillId
              AND  society_id = @SocietyId
              AND  is_deleted = FALSE";

        /// <summary>
        /// Loads all allocation rows associated with a given idempotency key. Used to reconstruct the response on duplicate (idempotent) submissions without re-running any write operations.
        /// </summary>
        public const string GetAllocationsByIdempotencyKey = @"
            SELECT mp.bill_id,
                   b.public_id                  AS bill_public_id,
                   b.period                     AS period,
                   mp.adjustment_id,
                   a.public_id                  AS adjustment_public_id,
                   mp.amount                    AS allocated_amount,
                   mp.notes                     AS notes,
                   mp.outstanding_after_payment AS outstanding_after_payment
            FROM   maintenance_payments mp
            LEFT JOIN bills       b ON b.id = mp.bill_id
            LEFT JOIN adjustments a ON a.id = mp.adjustment_id
            WHERE  mp.society_id      = @SocietyId
              AND  mp.idempotency_key = @IdempotencyKey
            ORDER  BY mp.id";

        /// <summary>
        /// Stamps outstanding_after_payment on every row that shares the same idempotency key.
        /// Executed once after all FIFO allocation inserts, before the transaction commits.
        /// </summary>
        public const string UpdateOutstandingAfterPayment = @"
            UPDATE maintenance_payments
            SET    outstanding_after_payment = @OutstandingAfterPayment
            WHERE  society_id      = @SocietyId
              AND  idempotency_key = @IdempotencyKey";

        // ── Maintenance Summary (4 focused, index-friendly queries) ──────────

        /// <summary>
        /// Total billed amount for a given period and society. Hits the (society_id, period) index on bills.
        /// </summary>
        public const string SummaryTotalCharges = @"
            SELECT COALESCE(SUM(amount), 0)
            FROM   bills
            WHERE  society_id = @SocietyId
              AND  period     = @Period
              AND  is_deleted = FALSE";

        /// <summary>
        /// Total payments already allocated to bills for a specific period. Only rows where bill_id IS NOT NULL are counted, which excludes OpeningBalance clearances and advance rows.
        /// </summary>
        public const string SummaryTotalCollected = @"
            SELECT COALESCE(SUM(mp.amount), 0)
            FROM   maintenance_payments mp
            JOIN   bills b ON b.id = mp.bill_id
            WHERE  b.society_id  = @SocietyId
              AND  b.period      = @Period
              AND  mp.is_deleted = FALSE";

        /// <summary>
        /// Remaining unpaid balance on bills for the period. Uses (amount − paid_amount) > 0 rather than status_code for accuracy.
        /// </summary>
        public const string SummaryBillOutstanding = @"
            SELECT COALESCE(SUM(amount - COALESCE(paid_amount, 0)), 0)
            FROM   bills
            WHERE  society_id                        = @SocietyId
              AND  period                            = @Period
              AND  is_deleted                        = FALSE
              AND  (amount - COALESCE(paid_amount, 0)) > 0";

        /// <summary>
        /// Sum of all pre-system opening-balance dues still owed across the society. Not period-specific: these dues exist until each flat's adjustment is fully cleared by FIFO payments.
        /// </summary>
        public const string SummaryOpeningBalanceRemaining = @"
            SELECT COALESCE(SUM(remaining_amount), 0)
            FROM   adjustments
            WHERE  society_id       = @SocietyId
              AND  entry_type       = @EntryType
              AND  remaining_amount > 0
              AND  is_deleted       = FALSE";

        // ── Maintenance Summary CTE ────────────────────────────────────────────────

        /// <summary>
        /// Single CTE that returns total charges, collected, bill outstanding, and opening-balance
        /// remaining for a society period — all in one round-trip.
        /// Parameters: @SocietyId, @Period, @EntryType.
        /// Maps to <see cref="SocietyLedger.Infrastructure.Services.Common.SummaryRow"/>.
        /// </summary>
        public const string MaintenanceSummary = @"
            WITH charges AS (
                SELECT COALESCE(SUM(amount), 0) AS v
                FROM   bills
                WHERE  society_id = @SocietyId AND period = @Period AND is_deleted = FALSE
            ),
            collected AS (
                SELECT COALESCE(SUM(mp.amount), 0) AS v
                FROM   maintenance_payments mp
                                WHERE  mp.society_id = @SocietyId
                                    AND  mp.is_deleted = FALSE
                                    AND  to_char(mp.payment_date, 'YYYY-MM') = @Period
            ),
            outstanding AS (
                SELECT COALESCE(SUM(amount - COALESCE(paid_amount, 0)), 0) AS v
                FROM   bills
                WHERE  society_id  = @SocietyId AND period = @Period AND is_deleted = FALSE
                  AND  status_code != 'cancelled'
                  AND  (amount - COALESCE(paid_amount, 0)) > 0
            ),
            ob AS (
                SELECT COALESCE(SUM(remaining_amount), 0) AS v
                FROM   adjustments
                WHERE  society_id = @SocietyId AND entry_type = @EntryType
                  AND  remaining_amount > 0 AND is_deleted = FALSE
            )
            SELECT
                charges.v     AS ""TotalCharges"",
                collected.v   AS ""TotalCollected"",
                outstanding.v AS ""BillOutstanding"",
                ob.v          AS ""ObRemaining""
            FROM charges, collected, outstanding, ob";

        /// <summary>
        /// Atomically recalculates a bill's <c>paid_amount</c> and <c>status_code</c> after a
        /// maintenance payment row is soft-deleted. Single UPDATE with correlated sub-SELECTs —
        /// no read-then-write race condition.
        /// When paid_amount drops to zero, restores 'overdue' if due_date has passed, else 'unpaid'.
        /// Parameter: @BillId (bigint).
        /// </summary>
        public const string RecalculateBillAfterPaymentDelete = @"
            UPDATE bills
            SET    paid_amount = COALESCE((
                       SELECT SUM(amount)
                       FROM   maintenance_payments
                       WHERE  bill_id    = @BillId
                         AND  is_deleted = FALSE
                   ), 0),
                   status_code = CASE
                       WHEN COALESCE((
                           SELECT SUM(amount)
                           FROM   maintenance_payments
                           WHERE  bill_id    = @BillId
                             AND  is_deleted = FALSE
                       ), 0) >= amount
                           THEN 'paid'
                       WHEN COALESCE((
                           SELECT SUM(amount)
                           FROM   maintenance_payments
                           WHERE  bill_id    = @BillId
                             AND  is_deleted = FALSE
                       ), 0) > 0
                           THEN 'partial'
                       WHEN due_date IS NOT NULL AND due_date < NOW()
                           THEN 'overdue'
                       ELSE 'unpaid'
                   END,
                   updated_at = NOW()
            WHERE  id         = @BillId
              AND  is_deleted = FALSE";

        // ── Advance re-allocation after bill generation ────────────────────────────────────

        /// <summary>
        /// Step 1 of advance re-allocation: links unallocated advance payment rows (bill_id IS NULL,
        /// adjustment_id IS NULL) to a newly generated bill, FIFO order, up to the bill's amount.
        /// A row is split when it straddles the boundary (running total exceeds the bill amount):
        /// the portion up to the boundary is linked to the bill; the remainder is left unlinked.
        /// Parameters: @BillId, @FlatId, @SocietyId, @Now.
        /// </summary>
        public const string LinkAdvancesToNewBill = @"
            WITH ranked AS (
                SELECT id,
                       amount,
                       SUM(amount) OVER (ORDER BY created_at, id ROWS UNBOUNDED PRECEDING) AS running_total
                FROM   maintenance_payments
                WHERE  flat_id        = @FlatId
                  AND  society_id     = @SocietyId
                  AND  bill_id        IS NULL
                  AND  adjustment_id  IS NULL
                  AND  is_deleted     = FALSE
                ORDER  BY created_at, id
                FOR UPDATE
            ),
            bill_amount AS (
                SELECT amount FROM bills WHERE id = @BillId AND society_id = @SocietyId
            )
            UPDATE maintenance_payments mp
            SET    bill_id     = @BillId,
                   updated_at  = @Now
            FROM   ranked r, bill_amount ba
            WHERE  mp.id = r.id
              AND  r.running_total <= ba.amount";

        /// <summary>
        /// Step 2 of advance re-allocation: after linking rows, recalculates the bill's
        /// paid_amount and status_code from scratch using the now-linked payment rows.
        /// Parameters: @BillId, @SocietyId, @Now.
        /// </summary>
        public const string RecalculateBillFromLinkedAdvances = @"
            UPDATE bills
            SET    paid_amount = COALESCE((
                       SELECT SUM(amount)
                       FROM   maintenance_payments
                       WHERE  bill_id     = @BillId
                         AND  is_deleted  = FALSE
                   ), 0),
                   status_code = CASE
                       WHEN COALESCE((
                                SELECT SUM(amount) FROM maintenance_payments
                                WHERE bill_id = @BillId AND is_deleted = FALSE
                            ), 0) >= amount
                           THEN 'paid'
                       WHEN COALESCE((
                                SELECT SUM(amount) FROM maintenance_payments
                                WHERE bill_id = @BillId AND is_deleted = FALSE
                            ), 0) > 0
                           THEN 'partial'
                       ELSE status_code
                   END,
                   updated_at  = @Now
            WHERE  id         = @BillId
              AND  society_id = @SocietyId
              AND  is_deleted = FALSE";
    }
}
