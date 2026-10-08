using System.Net;
using System.Net.Sockets;
using Photino.NET;
using SectorTelemetry.Services;
using SectorTelemetry.Sources;

namespace SectorTelemetry;

// Usage:
//   Sector-Telemetry                       desktop window, live AMS2 shared memory (Windows)
//   Sector-Telemetry --mock                desktop window, simulated race (any OS)
//   Sector-Telemetry --server              no window; dashboard in a browser at http://localhost:5080
//   Sector-Telemetry --server --urls http://0.0.0.0:5080   also serve to other devices on the LAN
public static class Program
{
    private const int DefaultPort = 5080;

    // Photino's Windows webview requires a single-threaded apartment on the UI thread.
    [STAThread]
    public static void Main(string[] args)
    {
        bool useMock = args.Contains("--mock") || !OperatingSystem.IsWindows();
        bool serverOnly = args.Contains("--server");
        var hostArgs = args.Where(a => a is not ("--mock" or "--server")).ToArray();

        var app = BuildApp(hostArgs, useMock, serverOnly, out string url);

        if (serverOnly)
        {
            app.Run();
            return;
        }

        app.StartAsync().GetAwaiter().GetResult();
        try
        {
            var window = new PhotinoWindow()
                .SetTitle("Sector Telemetry")
                .SetUseOsDefaultSize(false)
                .SetSize(1600, 960)
                .Center()
                .SetResizable(true)
                .Load(new Uri(url));
            // If the server is stopped (Ctrl+C, system shutdown), close the window too.
            app.Lifetime.ApplicationStopping.Register(() => window.Invoke(window.Close));
            window.WaitForClose();
        }
        finally
        {
            app.StopAsync().GetAwaiter().GetResult();
        }
    }

    private static WebApplication BuildApp(string[] args, bool useMock, bool serverOnly, out string url)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            // A published build has wwwroot beside the executable (or in its single-file extraction folder);
            // when run from source with `dotnet run` it's in the project directory.
            ContentRootPath = Directory.Exists(Path.Combine(AppContext.BaseDirectory, "wwwroot"))
                ? AppContext.BaseDirectory
                : Directory.GetCurrentDirectory(),
        });

        if (builder.Configuration["urls"] is { } urls)
        {
            url = urls.Split(';')[0].Replace("0.0.0.0", "localhost").Replace("+", "localhost").Replace("*", "localhost");
        }
        else
        {
            // The desktop window doesn't care which port it uses, so fall back to a free one if 5080 is taken.
            int port = serverOnly || IsPortFree(DefaultPort) ? DefaultPort : FreePort();
            url = $"http://localhost:{port}";
            builder.WebHost.UseUrls(url);
        }

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

        app.Logger.LogInformation("Telemetry source: {Source}; dashboard at {Url}", useMock ? "mock" : "AMS2 shared memory", url);
        return app;
    }

    private static bool IsPortFree(int port)
    {
        try
        {
            var listener = new TcpListener(IPAddress.Loopback, port);
            listener.Start();
            listener.Stop();
            return true;
        }
        catch (SocketException)
        {
            return false;
        }
    }

    private static int FreePort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
