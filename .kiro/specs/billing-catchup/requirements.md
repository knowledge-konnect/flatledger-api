# Requirements Document

## Introduction

The FlatLedger API runs a `MonthlyBillGenerationService` background service that fires on the 1st of each month to generate maintenance bills for all active societies. If the server is down on the 1st (e.g., May 1, 2026), the job never fires and that month's bills are never created — even after the server comes back up, because the service only checks `now.Day == 1` and then sleeps until the next midnight.

This feature adds a **billing catch-up mechanism** so that missed monthly billing runs are detected and recovered automatically on startup, and so that a SuperAdmin can also trigger catch-up for any arbitrary past period via an API endpoint. The existing `GenerateMonthlyBillsAsync` method is already idempotent (skips flats that already have a bill for the period), so the catch-up logic can safely call it without risk of double-billing.

---

## Glossary

- **BillingCatchupService**: The new hosted service (or startup hook) responsible for detecting and recovering missed billing runs on application startup.
- **CatchupBillingJob**: A single execution of `GenerateMonthlyBillsAsync` for a specific missed period, initiated by the BillingCatchupService.
- **MissedPeriod**: A calendar month (formatted `yyyy-MM`) for which no bills exist for any active society, and whose 1st day has already passed.
- **MonthlyBillGenerationService**: The existing background service that runs `GenerateMonthlyBillsAsync` on the 1st of each month.
- **GenerateMonthlyBillsAsync**: The existing idempotent billing method in `IBillingService` that creates bills for all active societies for a given period, skipping flats that already have a bill.
- **SuperAdmin**: A platform-level administrator role with access to cross-society operations.
- **Period**: A billing month expressed as a string in `yyyy-MM` format (e.g., `"2026-05"`).
- **ActiveSociety**: A society that is not soft-deleted and has at least one active flat.
- **LookbackMonths**: A configurable integer (default: 3) that limits how far back the BillingCatchupService will search for missed periods.

---

## Requirements

### Requirement 1: Startup Catch-Up Detection

**User Story:** As a platform operator, I want the system to automatically detect missed monthly billing runs when the server starts, so that bills are generated even if the server was down on the 1st of the month.

#### Acceptance Criteria

1. WHEN the application starts, THE BillingCatchupService SHALL check each calendar month within the configured lookback window (from the current month going back `LookbackMonths` months) to determine whether bills were missed.
2. WHEN checking a period, THE BillingCatchupService SHALL consider a period missed if the 1st of that month has already passed in UTC and no active society has any bills for that period.
3. WHEN one or more missed periods are detected, THE BillingCatchupService SHALL execute a CatchupBillingJob for each missed period in chronological order (oldest first).
4. WHEN no missed periods are detected on startup, THE BillingCatchupService SHALL log an informational message and take no further action.
5. THE BillingCatchupService SHALL NOT consider the current calendar month as a missed period if the 1st of the current month has not yet passed.

### Requirement 2: Idempotent Catch-Up Execution

**User Story:** As a platform operator, I want catch-up billing runs to be safe to re-run, so that restarting the server multiple times does not create duplicate bills.

#### Acceptance Criteria

1. WHEN a CatchupBillingJob runs for a period, THE BillingCatchupService SHALL call `GenerateMonthlyBillsAsync` with the target period's billing month.
2. WHEN `GenerateMonthlyBillsAsync` is called for a period where some flats already have bills, THE BillingService SHALL skip those flats and only create bills for flats that do not yet have one.
3. WHEN `GenerateMonthlyBillsAsync` is called for a period where all flats already have bills, THE BillingService SHALL return a result with `BillsCreated = 0` and `BillsSkipped > 0` without throwing an error.
4. IF the BillingCatchupService runs concurrently with the MonthlyBillGenerationService (e.g., server restarts on the 1st), THEN THE BillingCatchupService SHALL produce no duplicate bills due to the idempotency of `GenerateMonthlyBillsAsync`.

### Requirement 3: Catch-Up Lookback Window Configuration

**User Story:** As a platform operator, I want to configure how far back the catch-up service looks for missed periods, so that I can control the scope of recovery without risking unintended historical bill generation.

#### Acceptance Criteria

1. THE BillingCatchupService SHALL read the lookback window from the application configuration key `BackgroundServices:BillingCatchupLookbackMonths`.
2. WHEN the configuration key is absent, THE BillingCatchupService SHALL default to a lookback window of 3 months.
3. THE BillingCatchupService SHALL NOT generate bills for periods older than `LookbackMonths` months before the current UTC month.
4. WHERE the `LookbackMonths` value is set to 0, THE BillingCatchupService SHALL skip startup catch-up entirely and log a warning that catch-up is disabled.

### Requirement 4: Catch-Up Logging and Observability

**User Story:** As a platform operator, I want detailed logs for every catch-up action, so that I can audit what happened and diagnose issues.

#### Acceptance Criteria

1. WHEN the BillingCatchupService starts, THE BillingCatchupService SHALL log an informational message including the lookback window and the list of periods being evaluated.
2. WHEN a missed period is detected, THE BillingCatchupService SHALL log a warning message including the missed period and the reason it was identified as missed.
3. WHEN a CatchupBillingJob completes successfully, THE BillingCatchupService SHALL log an informational message including the period, `BillsCreated`, `BillsSkipped`, and execution time.
4. IF a CatchupBillingJob fails, THEN THE BillingCatchupService SHALL log a critical error including the period and the exception details, and SHALL continue processing any remaining missed periods.
5. WHEN catch-up completes (all periods processed), THE BillingCatchupService SHALL log a summary including total periods checked, periods recovered, and total bills created.

### Requirement 5: SuperAdmin Manual Catch-Up Endpoint

**User Story:** As a SuperAdmin, I want an API endpoint to trigger catch-up billing for a specific past period, so that I can recover missed bills on demand without restarting the server.

#### Acceptance Criteria

1. THE Api SHALL expose a `POST /billing/catchup` endpoint accessible only to users with the SuperAdmin role.
2. WHEN a SuperAdmin calls `POST /billing/catchup` with a valid `yyyy-MM` period, THE Api SHALL call `GenerateMonthlyBillsAsync` for that period and return the `BillingResult`.
3. WHEN a SuperAdmin calls `POST /billing/catchup` without specifying a period, THE Api SHALL default to the previous calendar month.
4. IF the requested period is in the future (after the current UTC month), THEN THE Api SHALL return HTTP 400 with a descriptive error message.
5. IF the requested period is more than 12 months in the past, THEN THE Api SHALL return HTTP 400 with a descriptive error message indicating the period is too far in the past.
6. WHEN `POST /billing/catchup` completes successfully, THE Api SHALL return HTTP 200 with the `BillingResult` including `BillsCreated`, `BillsSkipped`, `FailedSocieties`, and `ExecutionTime`.

### Requirement 6: Catch-Up Source Tracking

**User Story:** As a platform operator, I want bills generated by the catch-up mechanism to be distinguishable from scheduled and manually generated bills, so that I can audit the origin of each bill.

#### Acceptance Criteria

1. WHEN a CatchupBillingJob creates bills via the startup BillingCatchupService, THE BillingService SHALL set the `Source` field on each created bill to `"catchup-startup"`.
2. WHEN bills are created via the `POST /billing/catchup` endpoint, THE BillingService SHALL set the `Source` field on each created bill to `"catchup-manual"`.
3. THE BillingService SHALL preserve the existing `Source` values of `"scheduled"` and `"manual"` for bills created by the existing background service and society-admin endpoint respectively.

### Requirement 7: No Impact on Existing Billing Flows

**User Story:** As a platform operator, I want the catch-up mechanism to be additive and not change the behaviour of existing billing flows, so that normal operations are unaffected.

#### Acceptance Criteria

1. THE MonthlyBillGenerationService SHALL continue to operate exactly as before, firing on the 1st of each month and sleeping until the next UTC midnight.
2. THE `POST /billing/trigger-monthly-job-now` endpoint SHALL continue to operate exactly as before, calling `GenerateMonthlyBillsAsync` for the current UTC month.
3. THE `POST /billing/generate-monthly` society-admin endpoint SHALL continue to operate exactly as before, generating bills for the calling user's society for the specified period.
4. WHEN the BillingCatchupService runs on a day that is not the 1st of the month, THE MonthlyBillGenerationService SHALL NOT be triggered or affected.
