# Sector Telemetry

A live, F1-style telemetry dashboard for Automobilista 2. It reads the game's shared memory, works out what the game doesn't provide (gaps, sector splits, live delta, fuel per lap, the track map) and serves a browser dashboard over WebSocket.

## Running

**With AMS2 (Windows):** in game, set *Options → System → Shared Memory* to **Project CARS 2**, then:

```
dotnet run
```

Open http://localhost:5080. To view it from a tablet or second PC on your network:

```
dotnet run -- --urls http://0.0.0.0:5080
```

(allow port 5080 through Windows Firewall when prompted).

**Without the game (any OS):** a simulated 20-car race:

```
dotnet run -- --mock
```

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

Changes apply immediately. They're saved on the telemetry server in `settings.json` beside the executable, so every screen viewing the dashboard (PC, tablet, second monitor) uses the same settings, and they survive restarts and cleared browser data.

## How the derived data works

| | |
|---|---|
| Track map | AMS2 has no track geometry. Each on-track car's world X/Z is averaged into 4 m buckets by lap distance, so the outline completes after about one lap with a full field. It's cached in `tracks/` beside the executable. |
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
