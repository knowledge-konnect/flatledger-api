using Asp.Versioning.Builder;
using Asp.Versioning;
using Microsoft.AspNetCore.Routing;
using SocietyLedger.Api.Endpoints;
using SocietyLedger.Api.Endpoints.Admin;

namespace SocietyLedger.Api.Extensions;

public static class ApiEndpointMappingExtensions
{
    public static WebApplication MapApiEndpoints(this WebApplication app, ApiVersionSet versionSet)
    {
        var api = app.MapGroup("/api");

        api.MapGroup("/auth").MapAuthRoutes("Auth", versionSet);
        api.MapGroup("/billing").MapBillingRoutes("Billing", versionSet);
        api.MapGroup("/expenses").MapExpenseRoutes("Expenses", versionSet);
        api.MapGroup("/dashboard").MapDashboardRoutes("Dashboard", versionSet);
        api.MapGroup("/contact").MapContactRoutes("Contact", versionSet);
        api.MapGroup("/users").MapUserRoutes("Users", versionSet);
        api.MapGroup("/subscriptions").MapSubscriptionRoutes("Subscriptions", versionSet);
        api.MapGroup("/societies").MapSocietyRoutes("Societies", versionSet);
        api.MapGroup("/reports").MapReportRoutes("Reports", versionSet);
        api.MapGroup("/opening-balance").MapOpeningBalanceRoutes("OpeningBalance", versionSet);
        api.MapGroup("/plans").MapPlanRoutes("Plans", versionSet);
        api.MapGroup("/notifications").MapNotificationRoutes("Notifications", versionSet);
        api.MapGroup("/payment-modes").MapPaymentModeRoutes("PaymentModes", versionSet);
        api.MapGroup("/payments").MapPaymentRoutes("Payments", versionSet);
        api.MapGroup("/invoices").MapInvoiceRoutes("Invoices", versionSet);
        api.MapGroup("/flats").MapFlatRoutes("Flats", versionSet);
        api.MapGroup("/maintenance-payments").MapMaintenancePaymentRoutes("MaintenancePayments", versionSet);

        return app;
    }

    public static WebApplication MapApiAdminEndpoints(this WebApplication app, ApiVersionSet versionSet)
    {
        var admin = app.MapGroup("/api/admin");

        admin.MapGroup("/auth").MapAdminAuthRoutes("Admin", versionSet);
        admin.MapGroup("/users").MapAdminUserRoutes("Admin", versionSet);
        admin.MapGroup("/subscriptions").MapAdminSubscriptionRoutes("Admin", versionSet);
        admin.MapGroup("/societies").MapAdminSocietyRoutes("Admin", versionSet);
        admin.MapGroup("/platform-settings").MapAdminPlatformSettingRoutes("Admin", versionSet);
        admin.MapGroup("/plans").MapAdminPlanRoutes("Admin", versionSet);
        admin.MapGroup("/payments").MapAdminPaymentRoutes("Admin", versionSet);
        admin.MapGroup("/invoices").MapAdminInvoiceRoutes("Admin", versionSet);
        admin.MapGroup("/bills").MapAdminBillRoutes("Admin", versionSet);

        return app;
    }
}
