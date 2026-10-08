import { Tower } from './tower.js';
import { TrackMap, speedRamp, deltaRamp } from './trackmap.js';
import { PlayerPanel } from './player.js';
import { InputTrace } from './charts.js';
import { loadSettings, onSettingsChange, plotStyle, speed, speedUnit, temp, tempUnit } from './settings.js';
import {
  lapTime, sectorTime, clock, gap, signed, carColour, escapeHtml,
  FLAG_STYLES, SESSION_NAMES, YELLOW_STATES, PIT_LABELS, STATE_LABELS,
} from './format.js';

const $ = (id) => document.getElementById(id);
const TOWER_INTERVAL = 100; // ms; the timing tower doesn't need 20 Hz

// ---------------------------------------------------------------- preferences (per browser)

const prefs = loadPrefs();
function loadPrefs() {
  const defaults = { gapMode: 'interval', mapMode: 'team', labels: true, deltaMode: 'best', views: {} };
  try {
    return { ...defaults, ...JSON.parse(localStorage.getItem('sector-telemetry') || '{}') };
  } catch {
    return defaults;
  }
}
function savePrefs() {
  try {
    localStorage.setItem('sector-telemetry', JSON.stringify(prefs));
  } catch { /* storage unavailable: preferences just won't persist */ }
}

// ---------------------------------------------------------------- state

let state = null;
let selected = -1;
let trackVersion = -1;
let trackKey = '';
let lastTowerRender = 0;
let multiMake = false;

const colourFor = (c) => carColour(multiMake ? c.car : c.name);

const tower = new Tower($('tower'), select);
const map = new TrackMap($('map'), { onSelect: select });
map.colourFor = colourFor;
const player = new PlayerPanel();
const inputTrace = new InputTrace(15);

// ---------------------------------------------------------------- controls

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

segmented('gap-mode', prefs.gapMode, (m) => {
  prefs.gapMode = m;
  savePrefs();
  $('gap-head').textContent = m === 'leader' ? 'Gap' : 'Int';
  renderTower(true);
});
$('gap-head').textContent = prefs.gapMode === 'leader' ? 'Gap' : 'Int';

segmented('delta-mode', prefs.deltaMode, (m) => {
  prefs.deltaMode = m;
  savePrefs();
  player.deltaMode = m;
  if (state) player.render(state);
});
player.deltaMode = prefs.deltaMode;

segmented('map-colour', prefs.mapMode, (m) => {
  prefs.mapMode = m;
  savePrefs();
  map.mode = m;
  renderLegend();
});
map.mode = prefs.mapMode;
map.labels = prefs.labels;
$('map-labels').classList.toggle('on', prefs.labels);
$('map-labels').addEventListener('click', () => {
  prefs.labels = !prefs.labels;
  map.labels = prefs.labels;
  $('map-labels').classList.toggle('on', prefs.labels);
  savePrefs();
});

// Mirror/rotation is remembered per track, since the right orientation depends on the layout.
function trackView() {
  return prefs.views[trackKey] || { flip: false, rotation: 0 };
}
function setTrackView(v) {
  prefs.views[trackKey] = v;
  savePrefs();
  map.setView(v);
  $('map-flip').classList.toggle('on', v.flip);
}
$('map-flip').addEventListener('click', () => setTrackView({ ...trackView(), flip: !trackView().flip }));
$('map-rotate').addEventListener('click', () => setTrackView({ ...trackView(), rotation: (trackView().rotation + 1) % 4 }));


document.addEventListener('keydown', (e) => {
  if (e.key === 'Escape') select(-1);
});

function select(idx) {
  selected = idx === selected ? -1 : idx;
  map.selected = selected;
  renderTower(true);
  renderFocus();
  if (selected >= 0) fetchFocusLaps();
}

// ---------------------------------------------------------------- websocket

let socket;
let retry = 0;
function connect() {
  const proto = location.protocol === 'https:' ? 'wss' : 'ws';
  socket = new WebSocket(`${proto}://${location.host}/ws`);
  socket.onopen = () => {
    retry = 0;
    setConn('warn', 'Connected · waiting for data');
  };
  socket.onmessage = (e) => onMessage(JSON.parse(e.data));
  socket.onclose = () => {
    setConn('bad', 'Disconnected · retrying');
    setTimeout(connect, Math.min(5000, 500 * 2 ** retry++));
  };
}

function setConn(level, text) {
  $('conn').className = `conn ${level}`;
  $('conn-text').textContent = text;
}

function onMessage(msg) {
  if (msg.type === 'status') {
    setConn('warn', msg.status);
    $('map-msg').textContent = msg.status;
    return;
  }
  setConn('ok', msg.status);
  state = msg;
  multiMake = new Set(msg.cars.map((c) => c.car)).size > 1;

  if (msg.trackVersion !== trackVersion) fetchTrack(msg.trackVersion);

  map.update(msg.cars, msg.player?.idx ?? -1);
  if (msg.player) {
    inputTrace.push(msg.player);
    player.render(msg);
  }
  renderHeader(msg);
  renderTower(false);
  $('map-msg').textContent = msg.trackCoverage < 0.98
    ? `Learning track layout… ${Math.round(msg.trackCoverage * 100)}%`
    : '';
}

async function fetchTrack(version) {
  trackVersion = version;
  try {
    const track = await (await fetch('/api/track')).json();
    if (track.key !== trackKey) {
      trackKey = track.key;
      const v = trackView();
      map.setView(v);
      $('map-flip').classList.toggle('on', v.flip);
    }
    map.setTrack(track);
  } catch {
    trackVersion = -1;
  }
}

// ---------------------------------------------------------------- header

function renderHeader(s) {
  const ses = s.session;
  $('session-type').textContent = SESSION_NAMES[ses.sessionState] || ses.sessionState;
  $('track-name').textContent = ses.track || '—';
  $('track-variation').textContent = [ses.variation, ses.trackLength ? `${(ses.trackLength / 1000).toFixed(3)} km` : ''].filter(Boolean).join(' · ');

  let counter = '';
  if (ses.timed || (ses.timeRemaining != null && !ses.lapsInEvent)) {
    counter = `<small>REMAINING</small>${clock(ses.timeRemaining)}`;
  } else if (ses.lapsInEvent > 0) {
    counter = `<small>LAP</small>${Math.min(ses.leaderLap, ses.lapsInEvent)} / ${ses.lapsInEvent}`;
  } else if (ses.timeRemaining != null) {
    counter = `<small>REMAINING</small>${clock(ses.timeRemaining)}`;
  }
  const me = s.cars.find((c) => c.isPlayer);
  const pos = me?.pos ? `<span class="pos"><small>POS</small>P${me.pos}/${s.cars.length}</span>` : '';
  $('lap-counter').innerHTML = pos + counter;

  // Safety car / full-course yellow takes precedence over the highest flag shown to the player.
  const yellow = YELLOW_STATES[ses.yellowFlagState];
  const flag = yellow ? { label: yellow, colour: 'var(--flag-yellow)' } : FLAG_STYLES[ses.flag];
  const chip = $('flag-chip');
  if (yellow || (flag && ses.flag !== 'Green')) {
    chip.hidden = false;
    chip.textContent = flag.label;
    chip.style.background = flag.colour;
    $('flag-banner').style.background = flag.colour;
  } else {
    chip.hidden = true;
    $('flag-banner').style.background = ses.flag === 'Green' ? 'var(--flag-green)' : 'transparent';
  }

  const w = ses.weather;
  const rain = w.rain > 0.01 ? ` · Rain <b>${Math.round(w.rain * 100)}%</b>` : '';
  $('weather').innerHTML = `<span>Air <b>${Math.round(temp(w.ambient))}${tempUnit()}</b></span><span>Track <b>${Math.round(temp(w.track))}${tempUnit()}</b></span><span>Wind <b>${w.windSpeed?.toFixed(1) ?? '—'}</b>${rain}</span>`;
}

function renderTower(force) {
  if (!state) return;
  const now = performance.now();
  if (!force && now - lastTowerRender < TOWER_INTERVAL) return;
  lastTowerRender = now;
  tower.render(state, { gapMode: prefs.gapMode, selected, colourFor });
  if (selected >= 0) renderFocus();
}

// ---------------------------------------------------------------- legend & traces

function renderLegend() {
  const el = $('map-legend');
  if (prefs.mapMode === 'speed') {
    const stops = [0, 0.25, 0.5, 0.75, 1].map(speedRamp).join(',');
    el.innerHTML = `<span>${Math.round(speed(map.speedRange[0]))}</span><div class="ramp" style="background:linear-gradient(90deg,${stops})"></div><span>${Math.round(speed(map.speedRange[1]))} ${speedUnit()}</span>`;
    el.hidden = false;
  } else if (prefs.mapMode === 'delta') {
    const stops = [-1, 0, 1].map(deltaRamp).join(',');
    el.innerHTML = `<span>slower</span><div class="ramp" style="background:linear-gradient(90deg,${stops})"></div><span>faster than you (±${Math.round(speed(40))} ${speedUnit()})</span>`;
    el.hidden = false;
  } else {
    el.hidden = true;
  }
}
renderLegend();
setInterval(() => prefs.mapMode === 'speed' && renderLegend(), 2000);

function renderTraceLegend() {
  const item = (colour, text) => `<span><i style="background:${colour}"></i>${text}</span>`;
  const st = plotStyle();
  $('trace-legend').innerHTML = item(st.throttle, 'Throttle') + item(st.brake, 'Brake') + item(st.clutch, 'Clutch');
}
renderTraceLegend();

function drawCharts() {
  inputTrace.draw($('trace'));
  requestAnimationFrame(drawCharts);
}
requestAnimationFrame(drawCharts);

// ---------------------------------------------------------------- focus card (selected opponent)

let focusLaps = null;
let focusTimer = null;

async function fetchFocusLaps() {
  clearTimeout(focusTimer);
  if (selected < 0) return;
  const idx = selected;
  try {
    const data = await (await fetch(`/api/car/${idx}`)).json();
    if (idx === selected) {
      focusLaps = data;
      renderFocus();
    }
  } catch { /* retried below */ }
  focusTimer = setTimeout(fetchFocusLaps, 3000);
}

let focusLapsKey = '';

function renderFocus() {
  const el = $('focus');
  const c = state?.cars.find((x) => x.idx === selected);
  if (!c) {
    el.hidden = true;
    return;
  }
  if (el.hidden || el.dataset.idx !== String(c.idx)) {
    el.hidden = false;
    el.dataset.idx = String(c.idx);
    el.innerHTML = `<div class="focus-head"></div><div class="laps-mini"><table><thead><tr><th>Lap</th><th>Time</th><th>S1</th><th>S2</th><th>S3</th></tr></thead><tbody></tbody></table></div>`;
    focusLapsKey = '';
  }
  const me = state.cars.find((x) => x.isPlayer);
  const isRace = state.session.sessionState === 'Race';

  // Gap to you: difference of both cars' gaps to the leader (race) or best laps (practice/qualifying).
  let toYou = '—';
  if (me && me !== c) {
    if (isRace) {
      const lapDiff = (c.gapLaps || 0) - (me.gapLaps || 0);
      if (lapDiff !== 0) toYou = `${lapDiff > 0 ? '+' : ''}${lapDiff} lap${Math.abs(lapDiff) > 1 ? 's' : ''}`;
      else if ((c.gap ?? (c.pos === 1 ? 0 : null)) != null && (me.gap ?? (me.pos === 1 ? 0 : null)) != null) {
        toYou = signed((c.gap ?? 0) - (me.gap ?? 0), 3);
      }
    } else if (c.best && me.best) {
      toYou = signed(c.best - me.best, 3);
    }
  }
  const dv = me && c.speed != null && me.speed != null && me !== c ? Math.round(speed(c.speed) - speed(me.speed)) : null;
  const status = STATE_LABELS[c.state] || PIT_LABELS[c.pit] || 'On track';

  el.querySelector('.focus-head').innerHTML = `
    <h3><span class="team-bar" style="background:${colourFor(c)}"></span>P${c.pos} ${escapeHtml(c.name)}
      <button class="close" title="Close (Esc)">×</button></h3>
    <div class="sub">${escapeHtml(c.car)}${c.cls ? ` · ${escapeHtml(c.cls)}` : ''}</div>
    <dl>
      <dt>Gap to you</dt><dd>${toYou}</dd>
      <dt>${isRace ? 'Interval' : 'Gap to P' + (c.pos - 1)}</dt><dd>${c.pos === 1 ? '—' : gap(c.interval, c.intervalLaps)}</dd>
      <dt>Speed</dt><dd>${c.speed == null ? '—' : Math.round(speed(c.speed))} ${speedUnit()}${dv == null ? '' : ` <span class="${dv > 0 ? 'speed-down' : 'speed-up'}">(${dv > 0 ? '+' : ''}${dv})</span>`}</dd>
      <dt>Lap</dt><dd>${c.lap}${state.session.lapsInEvent ? ` / ${state.session.lapsInEvent}` : ''} · S${c.sector + 1}</dd>
      <dt>Last / Best</dt><dd><span class="t-${c.lastColour}">${lapTime(c.last)}</span> / <span class="${c.bestColour === 'purple' ? 't-purple' : ''}">${lapTime(c.best)}</span></dd>
      <dt>Status</dt><dd>${status}</dd>
      <dt>Pit stops</dt><dd>${c.pitStops}${c.lastPitTime ? ` · last ${c.lastPitTime.toFixed(1)}s` : ''}</dd>
    </dl>`;
  el.querySelector('.close').onclick = () => select(-1);

  // The lap list only changes when a lap completes; re-rendering it every tick would reset its scroll.
  const laps = focusLaps && focusLaps.idx === c.idx ? focusLaps.laps : [];
  const key = laps.map((l) => `${l.lap}:${l.time}`).join('|');
  if (key === focusLapsKey && focusLapsKey !== '') return;
  focusLapsKey = key;
  el.querySelector('.laps-mini tbody').innerHTML = [...laps].reverse().map((l) => `
    <tr style="${l.invalid ? 'text-decoration:line-through;opacity:.5' : ''}">
      <td>${l.lap}${l.pitted ? ' P' : ''}</td><td>${lapTime(l.time)}</td>
      ${l.sectors.map((t) => `<td>${sectorTime(t)}</td>`).join('')}
    </tr>`).join('') || '<tr><td colspan="5" style="text-align:center">No timed laps yet</td></tr>';
}

// Re-render everything that shows units when settings change.
function applySettings() {
  $('spd-head').textContent = speedUnit() === 'mph' ? 'mph' : 'km/h';
  renderLegend();
  renderTraceLegend();
  if (state) {
    if (state.player) player.render(state);
    renderHeader(state);
    renderTower(true);
  }
}
onSettingsChange(applySettings);
loadSettings().then(applySettings);

connect();
