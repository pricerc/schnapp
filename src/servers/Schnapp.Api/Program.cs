using Dapper;
using Schnapp.Api.Auth;
using Schnapp.Api.Journals;
using Schnapp.Api.Lists;
using Schnapp.Api.Machines;
using Schnapp.Api.Reports;
using Schnapp.Api.Screenshots;
using Schnapp.Contracts;
using Schnapp.Data;
using Schnapp.Screenshots;

// Schnapp.Api host entry point (minimal hosting model).
// Startup order: DI (data, auth, thumbnails) → optional DbUp migrate → admin bootstrap →
// bearer middleware → health + feature endpoint maps → Run.

var builder = WebApplication.CreateBuilder(args);

builder.Services.ConfigureHttpJsonOptions(o =>
{
    Schnapp.Contracts.Uuid7Json.AddUuid7Converters(o.SerializerOptions);
});


var connectionString = builder.Configuration.GetConnectionString("Main")
    ?? throw new InvalidOperationException("Connection string 'Main' is required.");

var databaseProvider = DatabaseProviderParser.Parse(builder.Configuration["Schnapp:DatabaseProvider"]);

var blobRoot = builder.Configuration["Schnapp:BlobRoot"] ?? Path.GetFullPath("/opt/schnapp/data/blobs");
builder.Services.AddSchnappData(databaseProvider, connectionString, blobRoot);
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<ThumbnailService>();
builder.Services.AddSingleton<ScreenshotImageProcessor>();

builder.Services.Configure<Microsoft.AspNetCore.Http.Features.FormOptions>(o =>
{
    o.MultipartBodyLengthLimit = 50L * 1024 * 1024;
});

var app = builder.Build();

var migrateOnStartup = builder.Configuration.GetValue("Schnapp:MigrateOnStartup", true);
if (migrateOnStartup)
{
    DatabaseMigrator.Migrate(databaseProvider, connectionString, app.Logger);
}

await AdminBootstrap.EnsureAdminAsync(app.Services, app.Configuration, app.Logger);

app.UseMiddleware<BearerTokenMiddleware>();

app.MapGet("/api/v1/health", async (IDbConnectionFactory db, IDatabaseDialect dialect, CancellationToken ct) =>
{
    try
    {
        await using var conn = (System.Data.Common.DbConnection)db.CreateConnection();
        await conn.OpenAsync(ct);
        var result = await conn.ExecuteScalarAsync<int>(new CommandDefinition(dialect.PingSql, cancellationToken: ct));
        return Results.Ok(new HealthResponse { Status = "ok", Database = result == 1 ? "up" : "unexpected" });
    }
    catch (Exception ex)
    {
        return Results.Json(
            new HealthResponse { Status = "degraded", Database = "down: " + ex.GetType().Name },
            statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapAuthEndpoints();
app.MapMachineEndpoints();
app.MapListEndpoints();
app.MapScreenshotEndpoints();
app.MapJournalEndpoints();
app.MapReportEndpoints();

app.Run();

/// <summary>Marker for WebApplicationFactory&lt;Program&gt; in integration tests.</summary>
public partial class Program;
