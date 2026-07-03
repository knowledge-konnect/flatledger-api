using Asp.Versioning;
using SocietyLedger.Api.BackgroundServices;
using SocietyLedger.Api.Extensions;
using SocietyLedger.Shared;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
    builder.WebHost.UseUrls($"http://+:{renderPort}");

builder.Host.ConfigureApiSerilog();

Log.Information("Starting SocietyLedger API...");

// Services
builder.Services.AddResponseCompression();
builder.Services.AddApiCors(builder.Configuration);
builder.Services.AddApiSwagger();
builder.Services.AddApiVersioningSetup();
//builder.Services.AddApiRateLimiting(builder.Configuration);
builder.Services.AddApiOptions(builder.Configuration, builder.Environment);
builder.Services.AddApiAuthentication(builder.Configuration, builder.Environment);
builder.Services.AddApiAuthorization();
builder.Services.AddApiHealthChecks(builder.Configuration);
builder.Services.AddApiForwardedHeaders();

builder.Services.AddApplicationServices(builder.Configuration);
builder.Services.AddInfrastructureServices(builder.Configuration);
builder.Services.AddSharedServices();

builder.Services.AddHostedService<MonthlyBillGenerationService>();
builder.Services.AddHostedService<TrialExpirationService>();

var app = builder.Build();

Log.Information("Environment: {Environment}", app.Environment.EnvironmentName);
Log.Information("Database connection string present: {HasConnection}",
    !string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString("DefaultConnection")));

// Must be first — corrects Request.Scheme / RemoteIpAddress before any other middleware reads them.
app.UseForwardedHeaders();

app.UseApiSwagger();

if (!app.Environment.IsProduction())
{
    app.UseHttpsRedirection();
}
else
{
    // FIX: HSTS was missing in production.
    app.UseHsts();
}

app.UseApiSecurityHeaders();

// Correlation ID middleware
app.UseMiddleware<SocietyLedger.Api.CorrelationIdMiddleware>();

app.UseResponseCompression();
app.UseCors(CorsExtensions.DefaultPolicyName);
app.UseApiRequestLogging();

app.UseAuthentication();
//app.UseRateLimiter();
app.UseAuthorization();

app.MapApiHealthChecks();

var versionSet = app.NewApiVersionSet()
    .HasApiVersion(new Asp.Versioning.ApiVersion(ApiConstants.API_VERSION_1_0))
    .ReportApiVersions()
    .Build();

app.MapApiEndpoints(versionSet);
app.MapApiAdminEndpoints(versionSet);

await app.WarmUpDatabaseAsync();

app.Run();
