# Sector Telemetry

A live, F1-style telemetry dashboard for Automobilista 2. It reads the game's shared memory, works out what the game doesn't provide (gaps, sector splits, live delta, fuel per lap, the track map) and serves a browser dashboard over WebSocket.

## Running

In AMS2, set *Options → System → Shared Memory* to **Project CARS 2**.

**Desktop app (Windows):** unzip `Sector-Telemetry-win-x64.zip` and run `Sector-Telemetry.exe`. The dashboard opens in its own window and reads the game on the same PC. It needs the Microsoft Edge WebView2 runtime, which comes with Windows 10 and 11.

**From source:**

```
dotnet run                    # desktop window, live game data (Windows)
dotnet run -- --mock          # desktop window, simulated 20-car race (any OS)
dotnet run -- --server        # no window; open http://localhost:5080 in a browser
dotnet run -- --server --urls http://0.0.0.0:5080   # also viewable from a tablet or another PC on the network
```

For network viewing, allow port 5080 through Windows Firewall when prompted.

**Building the package:**

```
./publish.sh            # dist/Sector-Telemetry-win-x64.zip
./publish.sh osx-arm64  # Apple Silicon Mac build
```

Learned track maps and settings are stored per user in `%LOCALAPPDATA%\SectorTelemetry` (Windows) or `~/Library/Application Support/SectorTelemetry` (macOS).

## What's on the dashboard

- **Timing tower:** position, interval or gap to the leader (toggle), last and best lap, live S1/S2/S3 coloured purple (session best), green (personal best) or yellow, current lap, pit status and stop count, and speed. Click a row to focus that car.
- **Track map:** outline coloured by sector, with every car animated in real time. Colour cars by car/team, absolute speed, or speed relative to you. Mirror and rotate are saved per track.
- **Focus card:** the selected opponent's gap to you, interval, speed difference, last and best laps, pit stops and full lap/sector history.
- **Your car:**
  - live delta to your best lap
  - speed, gear, RPM and shift lights
  - throttle, brake, clutch and steering
  - G-force
  - DRS, ERS, boost, pit limiter and other alerts
  - sector times
  - cars ahead and behind
  - fuel per lap, laps left and fuel to finish
  - brake bias, TC/ABS, engine temperatures and damage
- **Trace:** throttle and brake over the last 15 seconds.
- **Tyres and brakes:** inner/middle/outer temperatures, core temperature, pressure, wear and brake temperature for each corner.
- **Lap history:** your laps with sectors and fuel used.

## Settings

Click **Settings** in the top bar (or open `/settings.html`) to choose:

- **Speed:** km/h or mph
- **Temperature:** °C or °F

Changes apply immediately. They're saved by the app in `settings.json` in the app data folder, so every screen viewing the dashboard (PC, tablet, second monitor) uses the same settings, and they survive restarts and cleared browser data.

## How the derived data works

| | |
|---|---|
| Track map | AMS2 has no track geometry. Each on-track car's world X/Z is averaged into 4 m buckets by lap distance, so the outline completes after about one lap with a full field. It's cached in the app data folder. |
| Gaps | Each car's (distance, time) history is recorded. The gap is how long ago the car ahead passed the point where the car behind is now, which is the same method F1 timing uses. Practice and qualifying use best-lap gaps instead. |
| Sectors | Splits are taken when a car's sector changes. The game's own sector time is used when it agrees with the measured one to within 0.5 s. |
| Live delta | Your current lap time is compared, at the same lap distance, against your best valid lap (sampled every 10 m). |

## Layout

```
Ams2/       SharedMemory.h mirrored in C#; the parser is checked against the C struct (20,700 bytes)
Sources/    live shared-memory reader (sequence-number torn-read protection) and mock race
Services/   timing tracker, track map builder, snapshot builder, WebSocket hub, polling loop
wwwroot/    dashboard and settings page (plain ES modules, no build step)
```

`App.cpp` and `SharedMemory.h` are the original SMS/Reiza sample, kept as a reference.
