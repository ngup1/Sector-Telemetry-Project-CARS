// Display settings (units). The server's settings.json is the source of truth so every device
// shows the same thing; localStorage keeps a copy so the page renders correctly before the fetch.

const CACHE_KEY = 'sector-telemetry-settings';
export const DEFAULTS = { speedUnit: 'kph', tempUnit: 'c' };

/** Only the known fields, falling back to the current value (e.g. an older cache with extra keys). */
const pick = (obj, base) => ({ speedUnit: obj?.speedUnit ?? base.speedUnit, tempUnit: obj?.tempUnit ?? base.tempUnit });

let current = pick(readCache(), DEFAULTS);
const listeners = new Set();
let plotCache = null;

function readCache() {
  try {
    return JSON.parse(localStorage.getItem(CACHE_KEY) || '{}');
  } catch {
    return {};
  }
}

function writeCache() {
  try {
    localStorage.setItem(CACHE_KEY, JSON.stringify(current));
  } catch { /* cache only; the server copy is authoritative */ }
}

export function settings() {
  return current;
}

/** Call fn whenever settings change (here, from the server, or from another tab). */
export function onSettingsChange(fn) {
  listeners.add(fn);
}

function set(next) {
  current = pick(next, current);
  writeCache();
  listeners.forEach((fn) => fn(current));
}

export async function loadSettings() {
  try {
    const res = await fetch('/api/settings');
    if (res.ok) set(await res.json());
  } catch { /* offline: keep the cached copy */ }
  return current;
}

/** Saves a partial change; resolves to true once the server has stored it. */
export async function saveSettings(patch) {
  set({ ...current, ...patch });
  try {
    const res = await fetch('/api/settings', {
      method: 'PUT',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(patch),
    });
    if (!res.ok) return false;
    set(await res.json());
    return true;
  } catch {
    return false;
  }
}

// Another tab (e.g. the settings page) changed the cached copy.
window.addEventListener('storage', (e) => {
  if (e.key === CACHE_KEY) set(pick(readCache(), DEFAULTS));
});

/** Canvas colours and fonts, read from the stylesheet's CSS custom properties. */
export function plotStyle() {
  if (plotCache) return plotCache;
  const css = getComputedStyle(document.documentElement);
  const v = (name, fallback) => css.getPropertyValue(name).trim() || fallback;
  plotCache = {
    grid: v('--plot-grid', '#2a2a2a'),
    axis: v('--plot-axis', '#8a8a8a'),
    track: v('--plot-track', '#3a3a3a'),
    label: v('--plot-label', '#d0d0d0'),
    player: v('--plot-player', '#ffb000'),
    marker: v('--plot-marker', '#ffffff'),
    posText: v('--plot-pos-text', '#05060a'),
    throttle: v('--trace-throttle', '#33ee55'),
    brake: v('--trace-brake', '#ff3333'),
    clutch: v('--trace-clutch', '#ffffff'),
    sectors: [v('--sector-1', '#ff3030'), v('--sector-2', '#00b0ff'), v('--sector-3', '#ffd000')],
    mono: v('--plot-mono', "Consolas, 'Lucida Console', monospace"),
    sans: v('--plot-sans', 'Tahoma, Verdana, sans-serif'),
  };
  return plotCache;
}

// ---------------------------------------------------------------- units

/** km/h -> the selected speed unit. */
export function speed(kmh) {
  if (kmh == null) return null;
  return current.speedUnit === 'mph' ? kmh * 0.621371 : kmh;
}

export function speedUnit() {
  return current.speedUnit === 'mph' ? 'mph' : 'km/h';
}

/** °C -> the selected temperature unit. */
export function temp(c) {
  if (c == null) return null;
  return current.tempUnit === 'f' ? (c * 9) / 5 + 32 : c;
}

export function tempUnit() {
  return current.tempUnit === 'f' ? '°F' : '°C';
}
