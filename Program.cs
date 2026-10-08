using SectorTelemetry.Services;
using SectorTelemetry.Sources;

// Usage:
//   dotnet run                      live AMS2 shared memory (Windows), dashboard on http://localhost:5080
//   dotnet run -- --mock            simulated race (any OS)
//   dotnet run -- --urls http://0.0.0.0:5080   also serve to other devices on the LAN (tablet, second PC)

bool useMock = args.Contains("--mock") || !OperatingSystem.IsWindows();

var builder = WebApplication.CreateBuilder(args.Where(a => a != "--mock").ToArray());
if (builder.Configuration["urls"] == null) builder.WebHost.UseUrls("http://localhost:5080");

builder.Services.AddSingleton<TrackMapBuilder>();
builder.Services.AddSingleton<TimingTracker>();
builder.Services.AddSingleton<DashboardHub>();
builder.Services.AddSingleton<SettingsStore>();
builder.Services.AddSingleton<TelemetryService>();
builder.Services.AddHostedService(sp => sp.GetRequiredService<TelemetryService>());
if (useMock)
    builder.Services.AddSingleton<ITelemetrySource, MockSource>();
else if (OperatingSystem.IsWindows())
    builder.Services.AddSingleton<ITelemetrySource, SharedMemorySource>();

var app = builder.Build();

app.UseWebSockets();
app.UseDefaultFiles();
app.UseStaticFiles();

app.Map("/ws", async (HttpContext ctx, DashboardHub hub) =>
{
    if (!ctx.WebSockets.IsWebSocketRequest)
    {
        ctx.Response.StatusCode = StatusCodes.Status400BadRequest;
        return;
    }
    using var socket = await ctx.WebSockets.AcceptWebSocketAsync();
    await hub.RunAsync(socket, ctx.RequestAborted);
});

app.MapGet("/api/track", (TelemetryService t) => Results.Json(t.TrackSnapshot()));
app.MapGet("/api/settings", (SettingsStore s) => Results.Json(s.Get()));
app.MapPut("/api/settings", (SettingsPatch patch, SettingsStore s) => Results.Json(s.Update(patch)));
app.MapGet("/api/car/{index:int}", (int index, TelemetryService t) =>
    t.CarLaps(index) is { } laps ? Results.Json(laps) : Results.NotFound());

app.Logger.LogInformation("Telemetry source: {Source}", useMock ? "mock" : "AMS2 shared memory");
app.Run();
