// Session review: loads a recorded CSV, splits it into laps, and draws stacked trace charts that share
// one x axis (lap distance, lap time or session time) with a synced cursor, zoom and pan.

import { loadSettings, onSettingsChange, plotStyle, speed, speedUnit, temp, tempUnit } from './settings.js';
import { lapTime, signed, clock, escapeHtml } from './format.js';

const $ = (id) => document.getElementById(id);
const PREFS_KEY = 'sector-telemetry-analysis';
const WHEELS = ['FL', 'FR', 'RL', 'RR'];
const LEFT = 48, RIGHT = 10, TOP = 4, AXIS_H = 16;
const MIN_SPAN = { dist: 20, time: 0.5 };

// ---------------------------------------------------------------- channel groups (one chart each)

const pct = (v) => v * 100;
const wheels = (prefix) => WHEELS.map((w, i) => ({ col: prefix + w, label: w, colour: i }));

// colour: a plotStyle() key, or an index into the categorical series colours.
const GROUPS = [
  { id: 'speed', title: 'Speed', unit: () => speedUnit(), conv: speed, digits: 0, grow: 2, series: [{ col: 'speed', label: 'Speed', colour: 'player' }] },
  { id: 'delta', title: 'Delta', unit: () => 's', digits: 3, zero: true, series: [{ col: 'delta', label: 'Δ', colour: 'player' }] },
  {
    id: 'inputs', title: 'Throttle / brake', unit: () => '%', conv: pct, digits: 0, fixed: [0, 100], grow: 1.5,
    series: [{ col: 'throttle', label: 'Throttle', colour: 'throttle' }, { col: 'brake', label: 'Brake', colour: 'brake' }, { col: 'clutch', label: 'Clutch', colour: 'clutch' }],
  },
  { id: 'steering', title: 'Steering', unit: () => '%', conv: pct, digits: 0, fixed: [-100, 100], zero: true, series: [{ col: 'steering', label: 'Steering', colour: 'player' }] },
  { id: 'gear', title: 'Gear', unit: () => '', digits: 0, step: true, grow: 0.7, series: [{ col: 'gear', label: 'Gear', colour: 'player' }] },
  { id: 'rpm', title: 'RPM', unit: () => 'rpm', digits: 0, series: [{ col: 'rpm', label: 'RPM', colour: 'player' }] },
  { id: 'g', title: 'G-force', unit: () => 'g', digits: 2, zero: true, series: [{ col: 'latG', label: 'Lateral', colour: 0 }, { col: 'longG', label: 'Long.', colour: 1 }] },
  { id: 'tyreTemp', title: 'Tyre temp', unit: () => tempUnit(), conv: temp, digits: 0, series: wheels('tyreTemp') },
  { id: 'brakeTemp', title: 'Brake temp', unit: () => tempUnit(), conv: temp, digits: 0, off: true, series: wheels('brakeTemp') },
  { id: 'pressure', title: 'Tyre pressure', unit: () => 'psi', digits: 1, off: true, series: wheels('pressure') },
  { id: 'susp', title: 'Suspension travel', unit: () => 'mm', digits: 1, off: true, series: wheels('susp') },
  { id: 'rideHeight', title: 'Ride height', unit: () => 'cm', digits: 2, off: true, series: wheels('rideHeight') },
  { id: 'fuel', title: 'Fuel', unit: () => 'L', digits: 2, off: true, series: [{ col: 'fuel', label: 'Fuel', colour: 'player' }] },
];

// Columns that describe position/timing rather than something worth plotting on its own.
const META_COLS = new Set(['time', 'clock', 'lap', 'lapDist', 'lapTime', 'lastLap', 'sector', 'invalid', 'inPit', 'x', 'y', 'z']);

// ---------------------------------------------------------------- preferences

const prefs = loadPrefs();
function loadPrefs() {
  const defaults = { xMode: 'dist', flip: false, rotation: 0, channels: {} };
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem(PREFS_KEY) || '{}') };
  } catch {
    return defaults;
  }
}
function savePrefs() {
  try {
    localStorage.setItem(PREFS_KEY, JSON.stringify(prefs));
  } catch { /* preferences just won't persist */ }
}

// ---------------------------------------------------------------- state

let rec = null;        // { name, cols, n, header, laps, groups, xcol }
let main = -1;         // selected lap index into rec.laps, or -1 for the whole session
let ref = -1;          // reference lap index, or -1
let runs = [];         // what's plotted: [{ rows, xs, dist, el, delta, ref }]
let extent = null;     // { min, max } of the x axis
let view = null;       // { x0, x1 } zoom window
let cursor = null;     // x value under the mouse
let markers = [];      // [{ x, label }] vertical lines (sector or lap boundaries)
let charts = [];
let drawQueued = false;

// ---------------------------------------------------------------- loading

function parseCsv(text) {
  const lines = text.replace(/^﻿/, '').split(/\r?\n/).filter((l) => l.length);
  if (lines.length < 2) throw new Error('The file has no data rows.');
  const header = lines[0].split(',').map((h) => h.trim());
  const n = lines.length - 1;
  const arrays = header.map(() => new Float64Array(n));
  for (let r = 0; r < n; r++) {
    const parts = lines[r + 1].split(',');
    for (let c = 0; c < header.length; c++) {
      const s = parts[c];
      arrays[c][r] = s === undefined || s === '' ? NaN : Number(s);
    }
  }
  const cols = {};
  header.forEach((h, c) => { cols[h] = arrays[c]; });
  return { header, cols, n };
}

function hasData(a) {
  if (!a) return false;
  for (let i = 0; i < a.length; i += Math.max(1, Math.floor(a.length / 500))) if (Number.isFinite(a[i])) return true;
  return false;
}

function load(name, text) {
  let parsed;
  try {
    parsed = parseCsv(text);
  } catch (e) {
    alert(`Could not read ${name}: ${e.message}`);
    return;
  }
  const { cols, header } = parsed;
  const xcol = ['time', 'clock'].find((c) => hasData(cols[c])) || header[0];

  const groups = GROUPS
    .map((g) => ({ ...g, series: g.series.filter((s) => hasData(cols[s.col])) }))
    .filter((g) => g.series.length || g.id === 'delta');
  const known = new Set(GROUPS.flatMap((g) => g.series.map((s) => s.col)));
  for (const h of header) {
    if (known.has(h) || META_COLS.has(h) || h === xcol || !hasData(cols[h])) continue;
    groups.push({ id: `col:${h}`, title: h, unit: () => '', digits: 2, off: true, series: [{ col: h, label: h, colour: 'player' }] });
  }

  rec = { name, ...parsed, xcol, groups };
  rec.laps = findLaps(rec);
  main = -1;
  ref = -1;

  $('track-name').textContent = name.replace(/\.csv$/i, '').replaceAll('_', ' · ');
  const t = cols[xcol];
  const duration = t[parsed.n - 1] - t[0];
  const hz = duration > 0 ? Math.round(parsed.n / duration) : 0;
  $('track-variation').textContent = `${parsed.n.toLocaleString()} samples · ${clock(duration)} · ${rec.laps.filter((l) => l.time).length} timed laps${hz ? ` · ~${hz} Hz` : ''}`;
  $('charts-empty').hidden = true;

  renderChannels();
  buildCharts();
  renderLaps();
  select(-1);
}

$('file').addEventListener('change', async (e) => {
  const file = e.target.files[0];
  if (file) load(file.name, await file.text());
  e.target.value = '';
});

$('saved').addEventListener('change', async (e) => {
  const name = e.target.value;
  if (!name) return;
  try {
    const res = await fetch(`/api/recordings/${encodeURIComponent(name)}`);
    if (!res.ok) throw new Error(res.statusText);
    load(name, await res.text());
  } catch (err) {
    alert(`Could not load ${name}: ${err.message}`);
  }
  e.target.value = '';
});

async function listSaved() {
  const el = $('saved');
  try {
    const list = await (await fetch('/api/recordings')).json();
    el.innerHTML = `<option value="">${list.length ? 'Saved recordings…' : 'No saved recordings'}</option>` + list.map((r) =>
      `<option value="${escapeHtml(r.name)}">${escapeHtml(r.name.replace(/\.csv$/i, ''))} (${(r.size / 1e6).toFixed(1)} MB)</option>`).join('');
  } catch {
    el.parentElement.hidden = true; // no server (e.g. page opened from disk): file picker only
  }
}
// Pick up recordings made in another window since this page was opened.
window.addEventListener('focus', listSaved);

// ---------------------------------------------------------------- laps

const same = (a, b) => a === b || (Number.isNaN(a) && Number.isNaN(b));

/** Splits the recording into contiguous runs of the same lap number, with lap times where they're known. */
function findLaps(r) {
  const { lap, lapDist, lapTime: lt, lastLap, invalid, inPit } = r.cols;
  if (!hasData(lap)) return [];
  const segs = [];
  let start = 0;
  for (let i = 1; i <= r.n; i++) {
    if (i === r.n || lap[i] !== lap[start]) {
      if (Number.isFinite(lap[start]) && i - start > 10) segs.push({ lap: lap[start], start, end: i });
      start = i;
    }
  }
  segs.forEach((s, k) => {
    const next = segs[k + 1];
    const atLine = (lt && lt[s.start] < 3) || (lapDist && lapDist[s.start] < 30);
    s.complete = !!next && next.lap === s.lap + 1 && next.start === s.end && atLine;
    s.time = null;
    if (s.complete) {
      // The game's last-lap time updates a few frames after the line; fall back to the lap timer.
      if (lastLap) {
        for (let i = next.start; i < Math.min(next.end, next.start + 300); i++) {
          if (Number.isFinite(lastLap[i]) && !same(lastLap[i], lastLap[s.end - 1])) {
            s.time = lastLap[i];
            break;
          }
        }
      }
      if (s.time == null && lt) s.time = lt[s.end - 1];
    }
    s.invalid = false;
    s.pitted = false;
    for (let i = s.start; i < s.end; i++) {
      if (invalid && invalid[i] === 1) s.invalid = true;
      if (inPit && inPit[i] === 1) s.pitted = true;
    }
    s.kind = s.complete ? '' : !atLine ? (s.pitted ? 'out' : 'partial') : next ? 'partial' : 'unfinished';
  });
  return segs;
}

function bestLap() {
  let best = -1;
  rec.laps.forEach((l, k) => {
    if (l.time && !l.invalid && (best < 0 || l.time < rec.laps[best].time)) best = k;
  });
  return best;
}

function renderLaps() {
  const tbody = $('an-laps').querySelector('tbody');
  if (!rec) return;
  const best = bestLap();
  const bt = best >= 0 ? rec.laps[best].time : null;
  const rows = [`<tr data-k="-1" class="${main === -1 ? 'selected' : ''}"><td colspan="4" class="session-row">Whole session</td></tr>`];
  rec.laps.forEach((l, k) => {
    const cls = [k === main ? 'selected' : '', k === ref ? 'is-ref' : '', l.invalid ? 'invalid' : ''].join(' ');
    const time = l.time ? lapTime(l.time) : `<span class="tag">${l.kind}</span>`;
    const d = l.time && bt && k !== best ? signed(l.time - bt, 3) : '';
    const tags = (k === best ? '<span class="tag best">best</span>' : '') + (l.pitted ? '<span class="tag">pit</span>' : '');
    const canRef = hasData(rec.cols.lapDist);
    rows.push(`<tr data-k="${k}" class="${cls}"><td>${l.lap}${tags}</td><td class="${k === best ? 't-purple' : ''}">${time}</td><td>${d}</td>` +
      `<td>${canRef ? `<button class="ref-btn${k === ref ? ' on' : ''}" data-ref="${k}" title="Compare against this lap">${k === ref ? 'REF' : '·'}</button>` : ''}</td></tr>`);
  });
  tbody.innerHTML = rows.join('');
  $('laps-sub').textContent = bt ? `best ${lapTime(bt)}` : '';
}

$('an-laps').addEventListener('click', (e) => {
  const rb = e.target.closest('[data-ref]');
  if (rb) {
    const k = Number(rb.dataset.ref);
    ref = ref === k ? -1 : k;
    if (ref === main) main = -1;
    if (main === -1 && ref >= 0) {
      // Comparing needs a lap to compare: pick the best lap that isn't the reference.
      const b = bestLap();
      main = b >= 0 && b !== ref ? b : rec.laps.findIndex((l, i) => i !== ref && l.complete);
      if (main < 0) ref = -1;
    }
    select(main);
    return;
  }
  const tr = e.target.closest('tr[data-k]');
  if (tr) select(Number(tr.dataset.k));
});

function select(k) {
  main = k;
  if (ref === main) ref = -1;
  buildRuns();
  view = extent ? { x0: extent.min, x1: extent.max } : null;
  renderLaps();
  $('x-mode').hidden = main < 0;
  const l = rec.laps[main];
  $('charts-title').textContent = main < 0 ? 'Traces · whole session' : `Traces · lap ${l.lap}${ref >= 0 ? ` vs lap ${rec.laps[ref].lap}` : ''}`;
  redraw();
}

// ---------------------------------------------------------------- runs (the data actually plotted)

/** Longest stretch of rows where `col` keeps increasing without jumping more than maxJump. */
function monotonic(rows, col, maxJump) {
  let best = [0, 0];
  let s = 0;
  for (let k = 1; k <= rows.length; k++) {
    const brk = k === rows.length || !(col[rows[k]] >= col[rows[k - 1]]) || col[rows[k]] - col[rows[k - 1]] > maxJump;
    if (brk) {
      if (k - s > best[1] - best[0]) best = [s, k];
      s = k;
    }
  }
  return rows.slice(best[0], best[1]);
}

function lapRun(k, isRef) {
  const l = rec.laps[k];
  const { lapDist, lapTime: lt } = rec.cols;
  const clk = hasData(rec.cols.clock) ? rec.cols.clock : rec.cols[rec.xcol];
  let rows = Int32Array.from({ length: l.end - l.start }, (_, i) => l.start + i);
  // Around the line the lap counter and lap distance don't change on exactly the same frame.
  if (hasData(lapDist)) rows = monotonic(rows, lapDist, 200);
  // Time into the lap from the session clock, which (unlike the game's lap timer) is never unset.
  const t0 = clk[rows[0]];
  const offset = lt && lt[rows[0]] >= 0 && lt[rows[0]] < 5 ? lt[rows[0]] : 0;
  const el = Float64Array.from(rows, (r) => clk[r] - t0 + offset);
  const dist = lapDist ? Float64Array.from(rows, (r) => lapDist[r]) : null;
  const xs = prefs.xMode === 'dist' && dist ? dist : el;
  return { rows, xs, dist, el, delta: null, ref: isRef };
}

function buildRuns() {
  markers = [];
  if (main < 0) {
    const t = rec.cols[rec.xcol];
    const rows = monotonic(Int32Array.from({ length: rec.n }, (_, i) => i), t, Infinity);
    runs = [{ rows, xs: Float64Array.from(rows, (r) => t[r]), ref: false }];
    for (const l of rec.laps) markers.push({ x: t[l.start], label: `L${l.lap}` });
  } else {
    const m = lapRun(main, false);
    runs = [m];
    if (ref >= 0 && m.dist) {
      const r = lapRun(ref, true);
      runs.push(r);
      m.delta = Float64Array.from(m.dist, (d, k) => m.el[k] - interp(r.dist, r.el, d));
    }
    const sector = rec.cols.sector;
    if (sector) {
      for (let k = 1; k < m.rows.length; k++) {
        const s = sector[m.rows[k]];
        if (Number.isFinite(s) && s > 1 && s !== sector[m.rows[k - 1]]) markers.push({ x: m.xs[k], label: `S${s}` });
      }
    }
  }
  let min = Infinity, max = -Infinity;
  for (const r of runs) {
    if (!r.xs.length) continue;
    min = Math.min(min, r.xs[0]);
    max = Math.max(max, r.xs[r.xs.length - 1]);
  }
  extent = min < max ? { min, max } : null;
}

/** Index of the first element >= x in a non-decreasing array. */
function lowerBound(a, x) {
  let lo = 0, hi = a.length;
  while (lo < hi) {
    const mid = (lo + hi) >> 1;
    if (a[mid] < x) lo = mid + 1;
    else hi = mid;
  }
  return lo;
}

function interp(xs, ys, x) {
  const i = lowerBound(xs, x);
  if (i === 0 || i >= xs.length) return i === 0 && xs[0] === x ? ys[0] : NaN;
  const f = (x - xs[i - 1]) / (xs[i] - xs[i - 1] || 1);
  return ys[i - 1] + (ys[i] - ys[i - 1]) * f;
}

function nearest(run, x) {
  const i = lowerBound(run.xs, x);
  if (i >= run.xs.length) return run.xs.length - 1;
  if (i > 0 && x - run.xs[i - 1] < run.xs[i] - x) return i - 1;
  return i;
}

// ---------------------------------------------------------------- channels panel

function groupOn(g) {
  return prefs.channels[g.id] ?? !g.off;
}

function renderChannels() {
  $('channels').innerHTML = rec.groups.map((g) =>
    `<label><input type="checkbox" data-group="${escapeHtml(g.id)}" ${groupOn(g) ? 'checked' : ''}>${escapeHtml(g.title)}</label>`).join('');
}

$('channels').addEventListener('change', (e) => {
  const id = e.target.dataset.group;
  if (!id) return;
  prefs.channels[id] = e.target.checked;
  savePrefs();
  buildCharts();
  redraw();
});

// ---------------------------------------------------------------- charts

function buildCharts() {
  const host = $('charts');
  charts.forEach((c) => c.el.remove());
  charts = rec.groups.filter(groupOn).map((g) => new Chart(g, host));
}

function colourOf(s, st) {
  return typeof s.colour === 'number' ? st.series[s.colour] : st[s.colour] || st.player;
}

function setupCanvas(canvas) {
  const dpr = window.devicePixelRatio || 1;
  const w = canvas.clientWidth;
  const h = canvas.clientHeight;
  if (canvas.width !== Math.round(w * dpr) || canvas.height !== Math.round(h * dpr)) {
    canvas.width = Math.max(1, Math.round(w * dpr));
    canvas.height = Math.max(1, Math.round(h * dpr));
  }
  const ctx = canvas.getContext('2d');
  ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
  ctx.clearRect(0, 0, w, h);
  return { ctx, w, h };
}

function niceTicks(lo, hi, count) {
  const raw = (hi - lo) / Math.max(1, count);
  const mag = 10 ** Math.floor(Math.log10(raw || 1));
  const f = raw / mag;
  const step = (f < 1.5 ? 1 : f < 3 ? 2 : f < 7 ? 5 : 10) * mag;
  const out = [];
  for (let v = Math.ceil(lo / step) * step; v <= hi + step * 1e-6; v += step) out.push(Math.round(v / step) * step);
  return { ticks: out, step };
}

function xLabel(x, step) {
  if (main >= 0 && prefs.xMode === 'dist') return x >= 1000 ? `${(x / 1000).toFixed(step < 100 ? 2 : 1)} km` : `${Math.round(x)} m`;
  if (main >= 0) return `${x.toFixed(step < 1 ? 1 : 0)}s`;
  return clock(x);
}

class Chart {
  constructor(group, host) {
    this.group = group;
    this.el = document.createElement('div');
    this.el.className = 'an-chart';
    this.el.style.setProperty('--grow', group.grow ?? 1);
    this.el.innerHTML = '<div class="an-chart-legend"></div><canvas></canvas>';
    this.legend = this.el.querySelector('.an-chart-legend');
    this.canvas = this.el.querySelector('canvas');
    host.appendChild(this.el);
  }

  /** The value of series s at index k of run r, in display units. */
  value(r, s, k) {
    const g = this.group;
    let v;
    if (s.col === 'delta' && main >= 0 && ref >= 0) {
      if (!r.delta) return NaN;
      v = r.delta[k];
    } else {
      const col = rec.cols[s.col];
      if (!col) return NaN;
      v = col[r.rows[k]];
    }
    return g.conv ? g.conv(v) : v;
  }

  series() {
    // Delta: computed against the reference lap when there is one, else the live delta as recorded.
    if (this.group.id === 'delta') {
      if (main >= 0 && ref >= 0) return [{ col: 'delta', label: `vs lap ${rec.laps[ref].lap}`, colour: 'player' }];
      return hasData(rec.cols.delta) ? [{ col: 'delta', label: 'live, vs best', colour: 'player' }] : [];
    }
    return this.group.series;
  }

  draw(showX) {
    const g = this.group;
    const st = plotStyle();
    const series = this.series();
    const { ctx, w, h } = setupCanvas(this.canvas);
    const bottom = showX ? AXIS_H : 4;
    const pw = w - LEFT - RIGHT;
    const ph = h - TOP - bottom;
    if (!view || pw <= 0 || ph <= 0) return;
    const { x0, x1 } = view;
    const xOf = (x) => LEFT + ((x - x0) / (x1 - x0)) * pw;

    // Visible index range of each run.
    const ranges = runs.map((r) => [Math.max(0, lowerBound(r.xs, x0) - 1), Math.min(r.xs.length, lowerBound(r.xs, x1) + 1)]);

    let lo, hi;
    if (g.fixed) {
      [lo, hi] = g.fixed;
    } else {
      lo = Infinity;
      hi = -Infinity;
      runs.forEach((r, ri) => {
        const [i0, i1] = ranges[ri];
        const stride = Math.max(1, Math.floor((i1 - i0) / 3000));
        for (const s of series) {
          for (let k = i0; k < i1; k += stride) {
            const v = this.value(r, s, k);
            if (v < lo) lo = v;
            if (v > hi) hi = v;
          }
        }
      });
      if (!Number.isFinite(lo)) [lo, hi] = [0, 1];
      if (g.zero) [lo, hi] = [Math.min(lo, 0), Math.max(hi, 0)];
      if (g.zero && g.id !== 'g') [lo, hi] = [-Math.max(-lo, hi), Math.max(-lo, hi)]; // symmetric around 0
      if (hi - lo < 1e-6) [lo, hi] = [lo - 1, hi + 1];
      const pad = (hi - lo) * 0.06;
      lo -= g.id === 'gear' ? 0.5 : pad;
      hi += g.id === 'gear' ? 0.5 : pad;
    }
    const yOf = (v) => TOP + ph * (1 - (v - lo) / (hi - lo));

    // Grid and axes.
    ctx.lineWidth = 1;
    ctx.font = `10px ${st.mono}`;
    ctx.textBaseline = 'middle';
    const yt = niceTicks(lo, hi, ph > 90 ? 4 : 2);
    if (g.id === 'gear' && yt.step < 1) yt.ticks = yt.ticks.filter(Number.isInteger);
    for (const v of yt.ticks) {
      const y = Math.round(yOf(v)) + 0.5;
      ctx.strokeStyle = v === 0 && g.zero ? st.axis : st.grid;
      ctx.beginPath(); ctx.moveTo(LEFT, y); ctx.lineTo(LEFT + pw, y); ctx.stroke();
      ctx.fillStyle = st.axis;
      ctx.textAlign = 'right';
      ctx.fillText(+v.toFixed(Math.max(0, -Math.floor(Math.log10(yt.step)))) + '', LEFT - 6, y);
    }
    const xt = niceTicks(x0, x1, Math.max(2, Math.floor(pw / 110)));
    ctx.strokeStyle = st.grid;
    for (const x of xt.ticks) {
      const px = Math.round(xOf(x)) + 0.5;
      ctx.beginPath(); ctx.moveTo(px, TOP); ctx.lineTo(px, TOP + ph); ctx.stroke();
      if (showX) {
        ctx.fillStyle = st.axis;
        ctx.textAlign = 'center';
        ctx.fillText(xLabel(x, xt.step), px, h - AXIS_H / 2);
      }
    }

    // Sector / lap boundaries.
    ctx.setLineDash([3, 3]);
    ctx.strokeStyle = st.axis;
    for (const m of markers) {
      if (m.x < x0 || m.x > x1) continue;
      const px = Math.round(xOf(m.x)) + 0.5;
      ctx.beginPath(); ctx.moveTo(px, TOP); ctx.lineTo(px, TOP + ph); ctx.stroke();
      if (showX) {
        ctx.fillStyle = st.label;
        ctx.textAlign = 'left';
        ctx.fillText(m.label, px + 3, TOP + 7);
      }
    }
    ctx.setLineDash([]);

    // Traces: reference first (dashed, dimmer) so the selected lap draws on top.
    ctx.save();
    ctx.beginPath();
    ctx.rect(LEFT, TOP, pw, ph);
    ctx.clip();
    ctx.lineJoin = 'round';
    ctx.lineWidth = 1.5;
    for (let ri = runs.length - 1; ri >= 0; ri--) {
      const r = runs[ri];
      ctx.globalAlpha = r.ref ? 0.55 : 1;
      ctx.setLineDash(r.ref ? [5, 3] : []);
      for (const s of series) {
        if (r.ref && s.col === 'delta') continue;
        ctx.strokeStyle = colourOf(s, st);
        strokeSeries(ctx, r.xs, (k) => this.value(r, s, k), ranges[ri], xOf, yOf, g.step, pw);
      }
    }
    ctx.restore();

    if (cursor != null && cursor >= x0 && cursor <= x1) {
      const px = Math.round(xOf(cursor)) + 0.5;
      ctx.strokeStyle = st.marker;
      ctx.beginPath(); ctx.moveTo(px, TOP); ctx.lineTo(px, TOP + ph); ctx.stroke();
    }

    this.renderLegend(series, st);
  }

  renderLegend(series, st) {
    const g = this.group;
    const fmt = (v) => (Number.isFinite(v) ? (g.id === 'delta' ? signed(v, g.digits) : v.toFixed(g.digits)) : '—');
    const at = (r, s) => (cursor == null || !r.xs.length ? NaN : this.value(r, s, nearest(r, cursor)));
    const unit = g.unit();
    const items = series.map((s) => {
      const value = fmt(at(runs[0], s));
      const refRun = runs[1];
      const refVal = refRun && s.col !== 'delta' ? ` <span class="ref">ref ${fmt(at(refRun, s))}</span>` : '';
      const label = series.length > 1 || g.id === 'delta' ? `${escapeHtml(s.label)} ` : '';
      return `<span><i style="background:${colourOf(s, st)}"></i>${label}<span class="val">${value}</span>${refVal}</span>`;
    });
    const html = `<b>${escapeHtml(g.title)}</b>${unit ? `<small>${escapeHtml(unit)}</small>` : ''}${items.join('')}`;
    if (html !== this.legendHtml) {
      this.legend.innerHTML = html;
      this.legendHtml = html;
    }
  }
}

/** Draws one series. When there are more points than pixels, draws each pixel column's min–max instead. */
function strokeSeries(ctx, xs, val, [i0, i1], xOf, yOf, step, pw) {
  ctx.beginPath();
  let pen = false;
  if (i1 - i0 > pw * 3) {
    let col = null, mn = Infinity, mx = -Infinity;
    const flush = () => {
      if (col == null) return;
      if (mn === Infinity) { pen = false; return; }
      if (!pen) { ctx.moveTo(col, yOf(mn)); pen = true; } else ctx.lineTo(col, yOf(mn));
      ctx.lineTo(col, yOf(mx));
    };
    for (let k = i0; k < i1; k++) {
      const c = Math.round(xOf(xs[k]));
      if (c !== col) {
        flush();
        col = c;
        mn = Infinity;
        mx = -Infinity;
      }
      const v = val(k);
      if (v < mn) mn = v;
      if (v > mx) mx = v;
    }
    flush();
  } else {
    let prevY = 0;
    for (let k = i0; k < i1; k++) {
      const v = val(k);
      if (!Number.isFinite(v)) { pen = false; continue; }
      const x = xOf(xs[k]);
      const y = yOf(v);
      if (!pen) { ctx.moveTo(x, y); pen = true; } else {
        if (step) ctx.lineTo(x, prevY);
        ctx.lineTo(x, y);
      }
      prevY = y;
    }
  }
  ctx.stroke();
}

// ---------------------------------------------------------------- map

function orient(x, z) {
  let u = prefs.flip ? -x : x;
  let v = z;
  for (let r = 0; r < prefs.rotation; r++) [u, v] = [-v, u];
  return [u, v];
}

function drawMap() {
  const canvas = $('an-map');
  const { ctx, w, h } = setupCanvas(canvas);
  if (!rec) return;
  const { x: X, z: Z } = rec.cols;
  if (!hasData(X) || !hasData(Z)) return;
  const st = plotStyle();

  const stride = Math.max(1, Math.floor(rec.n / 6000));
  let minU = Infinity, maxU = -Infinity, minV = Infinity, maxV = -Infinity;
  for (let i = 0; i < rec.n; i += stride) {
    if (!Number.isFinite(X[i])) continue;
    const [u, v] = orient(X[i], Z[i]);
    minU = Math.min(minU, u); maxU = Math.max(maxU, u);
    minV = Math.min(minV, v); maxV = Math.max(maxV, v);
  }
  const pad = 14;
  const sw = Math.max(1, maxU - minU), sh = Math.max(1, maxV - minV);
  const scale = Math.min((w - pad * 2) / sw, (h - pad * 2) / sh);
  const ox = (w - sw * scale) / 2, oy = (h - sh * scale) / 2;
  const toScreen = (row) => {
    const [u, v] = orient(X[row], Z[row]);
    return [ox + (u - minU) * scale, oy + (v - minV) * scale];
  };
  const path = (rows, from, to, s = 1) => {
    ctx.beginPath();
    let pen = false;
    for (let k = from; k < to; k += s) {
      const row = rows ? rows[k] : k;
      if (!Number.isFinite(X[row])) { pen = false; continue; }
      const [px, py] = toScreen(row);
      if (pen) ctx.lineTo(px, py); else { ctx.moveTo(px, py); pen = true; }
    }
    ctx.stroke();
  };

  ctx.lineJoin = 'round';
  ctx.lineCap = 'round';
  ctx.strokeStyle = st.track;
  ctx.lineWidth = 5;
  path(null, 0, rec.n, stride);

  runs.forEach((r) => {
    if (!r.rows.length) return;
    const s = Math.max(1, Math.floor(r.rows.length / 4000));
    ctx.setLineDash(r.ref ? [5, 3] : []);
    ctx.globalAlpha = r.ref ? 0.6 : 0.35;
    ctx.strokeStyle = st.player;
    ctx.lineWidth = 2;
    path(r.rows, 0, r.rows.length, s);
    if (!r.ref && view) {
      // The part of the lap currently in the zoom window.
      ctx.globalAlpha = 1;
      ctx.lineWidth = 2.5;
      path(r.rows, Math.max(0, lowerBound(r.xs, view.x0) - 1), Math.min(r.rows.length, lowerBound(r.xs, view.x1) + 1), s);
    }
  });
  ctx.setLineDash([]);
  ctx.globalAlpha = 1;

  if (cursor != null) {
    runs.forEach((r) => {
      if (!r.rows.length) return;
      const [px, py] = toScreen(r.rows[nearest(r, cursor)]);
      ctx.beginPath();
      ctx.arc(px, py, 5, 0, Math.PI * 2);
      ctx.lineWidth = 2;
      if (r.ref) {
        ctx.strokeStyle = st.marker;
        ctx.stroke();
      } else {
        ctx.fillStyle = st.player;
        ctx.fill();
        ctx.strokeStyle = st.posText;
        ctx.stroke();
      }
    });
  }
}

$('an-flip').addEventListener('click', () => {
  prefs.flip = !prefs.flip;
  $('an-flip').classList.toggle('on', prefs.flip);
  savePrefs();
  redraw();
});
$('an-rotate').addEventListener('click', () => {
  prefs.rotation = (prefs.rotation + 1) % 4;
  savePrefs();
  redraw();
});
$('an-flip').classList.toggle('on', prefs.flip);

// ---------------------------------------------------------------- drawing & interaction

function redraw() {
  if (drawQueued) return;
  drawQueued = true;
  requestAnimationFrame(() => {
    drawQueued = false;
    if (!rec) return;
    charts.forEach((c, i) => c.draw(i === charts.length - 1));
    drawMap();
  });
}

function xAt(e) {
  const rect = e.target.getBoundingClientRect();
  const pw = rect.width - LEFT - RIGHT;
  return view.x0 + ((e.clientX - rect.left - LEFT) / pw) * (view.x1 - view.x0);
}

function minSpan() {
  return main >= 0 ? MIN_SPAN[prefs.xMode] : 2;
}

function setView(x0, x1) {
  const span = Math.min(extent.max - extent.min, Math.max(minSpan(), x1 - x0));
  x0 = Math.max(extent.min, Math.min(x0, extent.max - span));
  view = { x0, x1: x0 + span };
  redraw();
}

const host = $('charts');
let drag = null;

host.addEventListener('pointermove', (e) => {
  if (!view || e.target.tagName !== 'CANVAS') return;
  if (drag) {
    const pw = e.target.getBoundingClientRect().width - LEFT - RIGHT;
    const dx = ((e.clientX - drag.px) / pw) * (drag.x1 - drag.x0);
    setView(drag.x0 - dx, drag.x1 - dx);
  }
  cursor = xAt(e);
  redraw();
});
host.addEventListener('pointerleave', () => {
  cursor = null;
  redraw();
});
host.addEventListener('pointerdown', (e) => {
  if (!view || e.target.tagName !== 'CANVAS') return;
  drag = { px: e.clientX, ...view };
  e.target.setPointerCapture(e.pointerId);
});
host.addEventListener('pointerup', () => { drag = null; });
host.addEventListener('pointercancel', () => { drag = null; });
host.addEventListener('dblclick', () => extent && setView(extent.min, extent.max));
host.addEventListener('wheel', (e) => {
  if (!view || e.target.tagName !== 'CANVAS') return;
  e.preventDefault();
  const span = view.x1 - view.x0;
  if (Math.abs(e.deltaX) > Math.abs(e.deltaY)) {
    // Horizontal (trackpad) scroll pans.
    const dx = (e.deltaX / e.target.clientWidth) * span;
    setView(view.x0 + dx, view.x1 + dx);
    return;
  }
  const at = xAt(e);
  const f = Math.exp(e.deltaY * 0.0015);
  setView(at - (at - view.x0) * f, at + (view.x1 - at) * f);
}, { passive: false });

$('reset-zoom').addEventListener('click', () => extent && setView(extent.min, extent.max));

function segmented(id, value, onChange) {
  const el = $(id);
  const update = (v) => el.querySelectorAll('button').forEach((b) => b.classList.toggle('on', b.dataset.mode === v));
  update(value);
  el.addEventListener('click', (e) => {
    const b = e.target.closest('button[data-mode]');
    if (!b) return;
    update(b.dataset.mode);
    onChange(b.dataset.mode);
  });
}

segmented('x-mode', prefs.xMode, (m) => {
  prefs.xMode = m;
  savePrefs();
  if (rec) select(main);
});
$('x-mode').hidden = true;

new ResizeObserver(redraw).observe(document.querySelector('.analysis'));
onSettingsChange(redraw);
loadSettings();
listSaved();
