using Schnapp.Web.Services;

// Schnapp.Web — Razor Pages BFF for the product UI.
// Talks only to Schnapp.Api over HTTP (Schnapp:ApiBaseUrl). Auth: encrypted cookie holds the
// API bearer token; ApiClient attaches Authorization on each outbound call.

var builder = WebApplication.CreateBuilder(args);

// When hosted by the Service Control Manager, content root must be the install
// directory (the process cwd is System32) and stop signals must reach Kestrel.
builder.Host.UseWindowsService(o => o.ServiceName = "SchnappWeb");

builder.Services.AddRazorPages().AddMvcOptions(o =>
{
    // Many page InputModels use non-nullable `string X { get; set; } = "";` for
    // genuinely optional fields (Notes, Tags, CustomerId, etc.), not required ones.
    // Without this, MVC implicitly adds [Required] to those properties (because
    // Nullable is enabled), so submitting them blank fails ModelState validation
    // silently (pages here don't render a validation summary), making saves look
    // like they succeeded but did nothing. Explicit [Required] attributes still apply.
    o.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true;
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddResponseCompression();
builder.Services.AddDataProtection();
// Local API must not go through HTTP_PROXY (NO_PROXY often lists "localhost" but not 127.0.0.1).
static HttpClientHandler NoProxyHandler() => new() { UseProxy = false };

builder.Services.AddHttpClient("SchnappApi")
    .ConfigurePrimaryHttpMessageHandler(NoProxyHandler); // image/thumb proxies

var apiBase = builder.Configuration["Schnapp:ApiBaseUrl"] ?? "http://localhost:5080";
builder.Services.AddHttpClient<ApiClient>(client =>
{
    client.BaseAddress = new Uri(apiBase.TrimEnd('/') + "/");
    client.Timeout = TimeSpan.FromSeconds(60);
}).ConfigurePrimaryHttpMessageHandler(NoProxyHandler);

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
}

app.UseResponseCompression();
app.UseStaticFiles();
app.UseRouting();
app.MapRazorPages();

app.Run();
