# Implementation Plan: Billing Catch-Up Mechanism

## Overview

Implement the billing catch-up mechanism in dependency order: extend the repository interface and implementation first, then thread the `source` parameter through the billing service, add the new DTO and hosted service, wire up the endpoint, update configuration, and register the hosted service.

## Tasks

- [x] 1. Add `HasAnyBillsForPeriodAsync` to `IBillRepository` and `BillRepository`
  - [x] 1.1 Add method signature to `IBillRepository`
    - Open `SocietyLedger.Application/Interfaces/Repositories/IBillRepository.cs`
    - Add the following method to the interface:
      ```csharp
      /// <summary>
      /// Returns true if ANY non-deleted bill exists for the given period across ALL societies.
      /// Used by BillingCatchupService to efficiently detect whether a period was missed entirely.
      /// </summary>
      Task<bool> HasAnyBillsForPeriodAsync(string period);
      ```
    - _Requirements: 1.2_

  - [x] 1.2 Implement `HasAnyBillsForPeriodAsync` in `BillRepository`
    - Open `SocietyLedger.Infrastructure/Persistence/Repositories/BillRepository.cs`
    - Add a single cross-society EXISTS query (no `societyId` filter):
      ```csharp
      public Task<bool> HasAnyBillsForPeriodAsync(string period) =>
          _db.bills.AnyAsync(b => b.period == period && !b.is_deleted);
      ```
    - _Requirements: 1.2_

  - [ ]* 1.3 Write unit tests for `HasAnyBillsForPeriodAsync`
    - Test returns `false` when no bills exist for the period
    - Test returns `true` when at least one bill exists for the period across any society
    - Test that soft-deleted bills (`is_deleted = true`) are excluded
    - _Requirements: 1.2_

- [x] 2. Add `source` parameter to `IBillingService.GenerateMonthlyBillsAsync` and thread it through `BillingService`
  - [x] 2.1 Update `IBillingService` signature
    - Open `SocietyLedger.Application/Interfaces/Services/IBillingService.cs`
    - Replace the existing `GenerateMonthlyBillsAsync` signature with:
      ```csharp
      /// <summary>
      /// Generates monthly maintenance bills for ALL active societies.
      /// The <paramref name="source"/> value is stamped on every created bill.
      /// Accepted values: "scheduled", "catchup-startup", "catchup-manual".
      /// Existing callers that omit <paramref name="source"/> continue to use "scheduled".
      /// </summary>
      Task<BillingResult> GenerateMonthlyBillsAsync(DateTime? billingMonth = null, string source = "scheduled");
      ```
    - _Requirements: 6.1, 6.2, 6.3_

  - [x] 2.2 Thread `source` through `BillingService.GenerateMonthlyBillsAsync`
    - Open `SocietyLedger.Infrastructure/Services/BillingService.cs`
    - Update the method signature to accept `string source = "scheduled"`
    - Replace the hardcoded `Source: "scheduled"` in the `BillAddDto` constructor call with `Source: source`
    - No other logic changes — existing callers (`MonthlyBillGenerationService`, `POST /billing/trigger-monthly-job-now`) omit the argument and keep `"scheduled"` by default
    - _Requirements: 6.1, 6.2, 6.3, 7.1, 7.2_

  - [ ]* 2.3 Write property test for source field correctness
    - **Property 7: Source field correctness**
    - **Validates: Requirements 6.1, 6.2, 6.3**
    - Generate random sets of flats and periods; call `GenerateMonthlyBillsAsync` with `source = "catchup-startup"` and verify all created `BillAddDto` records carry `Source = "catchup-startup"`; repeat with `source = "catchup-manual"`; verify the no-argument call still produces `Source = "scheduled"`

  - [ ]* 2.4 Write unit tests for source threading
    - Mock `IBillRepository` and verify that `BillAddDto.Source` equals the value passed as `source` to `GenerateMonthlyBillsAsync`
    - Verify the default value `"scheduled"` is used when `source` is omitted
    - _Requirements: 6.1, 6.2, 6.3_

- [x] 3. Create `CatchupBillingRequest` DTO
  - [x] 3.1 Create `SocietyLedger.Application/DTOs/Billing/CatchupBillingRequest.cs`
    - New file; model after the existing `GenerateMonthlyBillsRequest` pattern
    - Implement `GetBillingMonthDate()` that parses `Period` as `yyyy-MM` and falls back to the previous UTC month when `Period` is null/empty:
      ```csharp
      public record CatchupBillingRequest
      {
          public string? Period { get; init; }

          public DateTime GetBillingMonthDate()
          {
              if (!string.IsNullOrWhiteSpace(Period) &&
                  DateTime.TryParseExact(Period, "yyyy-MM",
                      System.Globalization.CultureInfo.InvariantCulture,
                      System.Globalization.DateTimeStyles.None,
                      out var parsed))
              {
                  return new DateTime(parsed.Year, parsed.Month, 1, 0, 0, 0, DateTimeKind.Utc);
              }
              var prev = DateTime.UtcNow.AddMonths(-1);
              return new DateTime(prev.Year, prev.Month, 1, 0, 0, 0, DateTimeKind.Utc);
          }
      }
      ```
    - _Requirements: 5.2, 5.3_

  - [ ]* 3.2 Write unit tests for `CatchupBillingRequest.GetBillingMonthDate`
    - Test valid `yyyy-MM` string returns first day of that month at UTC midnight
    - Test null `Period` returns first day of the previous UTC month
    - Test empty string `Period` returns first day of the previous UTC month
    - Test invalid format (e.g., `"2026-5"`, `"not-a-date"`) falls back to previous month
    - _Requirements: 5.2, 5.3_

- [x] 4. Implement `BillingCatchupService` hosted service
  - [x] 4.1 Create `SocietyLedger.Api/BackgroundServices/BillingCatchupService.cs`
    - Implement `IHostedService` (not `BackgroundService`) — only `StartAsync` does work; `StopAsync` returns `Task.CompletedTask`
    - Constructor injects `IServiceProvider`, `ILogger<BillingCatchupService>`, and `IConfiguration`
    - Read `BackgroundServices:BillingCatchupLookbackMonths` from config; default to `3`
    - If `lookbackMonths == 0`, log a warning and return immediately from `StartAsync`
    - Implement `BuildPeriodWindow(DateTime utcNow, int lookbackMonths)` — returns the N months immediately preceding the current month as `yyyy-MM` strings in ascending (oldest-first) order; never includes the current month
    - For each period in the window, create a scoped `IServiceScope`, resolve `IBillRepository`, and call `HasAnyBillsForPeriodAsync`; collect periods where it returns `false` (missed periods)
    - For each missed period (oldest first), create a new `IServiceScope`, resolve `IBillingService`, and call `GenerateMonthlyBillsAsync(billingMonth, source: "catchup-startup")`
    - Wrap each per-period call in try/catch; log critical error and continue to the next period on failure (fault isolation)
    - Log startup message with lookback window and evaluated periods (Req 4.1)
    - Log warning for each missed period detected (Req 4.2)
    - Log informational result (period, BillsCreated, BillsSkipped, ExecutionTime) after each successful job (Req 4.3)
    - Log critical error with period + exception on failure (Req 4.4)
    - Log summary (periods checked, periods recovered, total bills created) on completion (Req 4.5)
    - The service must never throw out of `StartAsync` — all exceptions must be caught so the application continues to start normally
    - _Requirements: 1.1, 1.2, 1.3, 1.4, 1.5, 2.1, 3.1, 3.2, 3.3, 3.4, 4.1, 4.2, 4.3, 4.4, 4.5, 6.1_

  - [ ]* 4.2 Write property test for period window bounds
    - **Property 1: Period window bounds**
    - **Validates: Requirements 1.1, 1.5, 3.3**
    - Generate random `DateTime` values (utcNow) and random `lookbackMonths` values (1–24); assert `BuildPeriodWindow` returns exactly `lookbackMonths` entries, the first entry is `lookbackMonths` months before the current month, the last entry is the previous month, and the current month is absent

  - [ ]* 4.3 Write property test for missed-period detection correctness
    - **Property 2: Missed-period detection correctness**
    - **Validates: Requirements 1.2, 1.3**
    - Generate random sets of periods and random boolean responses from a mock `HasAnyBillsForPeriodAsync`; assert that the detected missed periods are exactly those for which the mock returned `false`

  - [ ]* 4.4 Write property test for fault isolation across periods
    - **Property 4: Fault isolation across periods**
    - **Validates: Requirement 4.4**
    - Generate random lists of 2–10 missed periods and randomly designate one as throwing; assert that `GenerateMonthlyBillsAsync` is called for every period regardless of the failure

  - [ ]* 4.5 Write unit tests for `BillingCatchupService`
    - Test `LookbackMonths = 0` logs warning and exits without calling `HasAnyBillsForPeriodAsync`
    - Test no missed periods logs informational message and does not call `GenerateMonthlyBillsAsync`
    - Test missed periods are processed oldest-first
    - Test exception in one period does not prevent remaining periods from being processed
    - _Requirements: 1.3, 1.4, 3.4, 4.4_

- [x] 5. Checkpoint — verify compilation and existing tests pass
  - Ensure all tests pass, ask the user if questions arise.

- [x] 6. Add `POST /billing/catchup` endpoint to `BillingEndpoints.cs`
  - [x] 6.1 Add the catchup route inside `MapBillingRoutes`
    - Open `SocietyLedger.Api/Endpoints/BillingEndpoints.cs`
    - Add `using SocietyLedger.Application.DTOs.Billing;` if not already present (it is)
    - Add the new route after the existing `POST /billing/trigger-monthly-job-now` handler:
      ```csharp
      // POST /billing/catchup
      app.MapPost("/catchup",
          [Authorize("SuperAdmin")]
          [SwaggerOperation(
              Summary     = "Trigger catch-up billing for a past period (SuperAdmin only)",
              Description = "Generates bills for all active societies for the specified past period. " +
                            "Defaults to the previous calendar month when period is omitted. " +
                            "Returns 400 if the period is in the future or more than 12 months in the past."
          )]
          async ([FromBody] CatchupBillingRequest request, IBillingService billingService, HttpContext ctx) =>
          {
              var billingMonth = request.GetBillingMonthDate();
              var period = billingMonth.ToString("yyyy-MM");
              var currentMonth = new DateTime(DateTime.UtcNow.Year, DateTime.UtcNow.Month, 1, 0, 0, 0, DateTimeKind.Utc);

              if (billingMonth > currentMonth)
              {
                  var err = ErrorResponse.Create("PERIOD_IN_FUTURE",
                      $"Period '{period}' is in the future. Catch-up billing can only target past periods.",
                      ctx.TraceIdentifier);
                  return Results.Json(err, statusCode: 400);
              }

              if (billingMonth < currentMonth.AddMonths(-12))
              {
                  var err = ErrorResponse.Create("PERIOD_TOO_OLD",
                      $"Period '{period}' is more than 12 months in the past. Maximum lookback is 12 months.",
                      ctx.TraceIdentifier);
                  return Results.Json(err, statusCode: 400);
              }

              var result = await billingService.GenerateMonthlyBillsAsync(billingMonth, source: "catchup-manual");
              return Results.Ok(ApiResponse<BillingResult>.Success(
                  result, $"Catch-up billing completed for {period}."));
          })
      .RequireRateLimiting("AuthPolicy")
      .WithTags(groupName)
      .WithApiVersionSet(versionSet)
      .HasApiVersion(version_1_0)
      .WithName("TriggerCatchupBilling")
      .Produces<ApiResponse<BillingResult>>(200)
      .Produces<ErrorResponse>(400)
      .Produces<ErrorResponse>(401)
      .Produces<ErrorResponse>(403)
      .Produces<ErrorResponse>(500);
      ```
    - _Requirements: 5.1, 5.2, 5.3, 5.4, 5.5, 5.6, 6.2_

  - [ ]* 6.2 Write property test for future period rejection
    - **Property 5: Future period rejection**
    - **Validates: Requirement 5.4**
    - Generate random `yyyy-MM` strings representing months strictly after the current UTC month; assert the validation logic returns a 400 error for all of them and does not call `GenerateMonthlyBillsAsync`

  - [ ]* 6.3 Write property test for stale period rejection
    - **Property 6: Stale period rejection**
    - **Validates: Requirement 5.5**
    - Generate random `yyyy-MM` strings representing months more than 12 months before the current UTC month; assert the validation logic returns a 400 error for all of them and does not call `GenerateMonthlyBillsAsync`

  - [ ]* 6.4 Write unit tests for `POST /billing/catchup` validation
    - Test future period returns 400 with `PERIOD_IN_FUTURE` error code
    - Test period older than 12 months returns 400 with `PERIOD_TOO_OLD` error code
    - Test current month returns 400 (current month is not a valid past period)
    - Test valid past period within 12 months calls `GenerateMonthlyBillsAsync` with `source = "catchup-manual"` and returns 200
    - Test omitted period defaults to previous month and succeeds
    - _Requirements: 5.2, 5.3, 5.4, 5.5, 5.6_

- [x] 7. Update `appsettings.json` and register `BillingCatchupService` in `Program.cs`
  - [x] 7.1 Add `BillingCatchupLookbackMonths` to `appsettings.json`
    - Open `SocietyLedger.Api/appsettings.json`
    - Add `"BillingCatchupLookbackMonths": 3` to the existing `BackgroundServices` section:
      ```json
      "BackgroundServices": {
        "TrialExpirationIntervalHours": 24,
        "TrialExpirationRetryMinutes": 5,
        "MonthlyBillRetryMinutes": 5,
        "MonthlyBillMaxRetryAttempts": 3,
        "BillingCatchupLookbackMonths": 3
      }
      ```
    - _Requirements: 3.1, 3.2_

  - [x] 7.2 Register `BillingCatchupService` as a hosted service in `Program.cs`
    - Open `SocietyLedger.Api/Program.cs`
    - Add the following line alongside the existing hosted service registrations:
      ```csharp
      builder.Services.AddHostedService<BillingCatchupService>();
      ```
    - Add the required `using SocietyLedger.Api.BackgroundServices;` if not already covered by the existing using block
    - _Requirements: 1.1, 7.4_

- [x] 8. Final checkpoint — ensure all tests pass
  - Build the solution and confirm zero compiler errors
  - Run all tests and confirm no regressions in existing billing flows
  - Ensure all tests pass, ask the user if questions arise.

## Notes

- Tasks marked with `*` are optional and can be skipped for faster MVP
- Each task references specific requirements for traceability
- Checkpoints ensure incremental validation
- Property tests validate universal correctness properties (Properties 1–7 from the design document)
- Unit tests validate specific examples and edge cases
- The `source` parameter default value (`"scheduled"`) means no existing call sites need updating
