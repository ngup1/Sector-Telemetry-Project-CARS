import { lapTime, sectorTime, signed, num, tempColour, escapeHtml, driverCode, gap } from './format.js';
import { speed, speedUnit, temp, tempUnit } from './settings.js';

const $ = (id) => document.getElementById(id);
const TYRE_NAMES = ['FL', 'FR', 'RL', 'RR'];
const SHIFT_LEDS = 15;

/** Brake disc colour like hot metal: dark steel, dull red, orange, then yellow-white. */
function brakeGlow(t) {
  const stops = [[200, [70, 70, 70]], [400, [130, 35, 15]], [600, [215, 50, 15]], [800, [255, 125, 20]], [1000, [255, 215, 110]]];
  if (t == null || t <= stops[0][0]) return 'rgb(70 70 70)';
  for (let k = 1; k < stops.length; k++) {
    if (t <= stops[k][0]) {
      const [t0, c0] = stops[k - 1];
      const [t1, c1] = stops[k];
      const f = (t - t0) / (t1 - t0);
      const c = c0.map((v, j) => Math.round(v + (c1[j] - v) * f));
      return `rgb(${c[0]} ${c[1]} ${c[2]})`;
    }
  }
  return 'rgb(255 215 110)';
}

/** Updates the viewed car's panel, tyres and lap history. */
export class PlayerPanel {
  constructor() {
    const lights = $('shift-lights');
    lights.innerHTML = Array.from({ length: SHIFT_LEDS }, () => '<i></i>').join('');
    this.leds = [...lights.children];
    this.lightsEl = lights;
    this.lastLapsKey = '';
    this.deltaMode = 'best';

    $('tyres').innerHTML = TYRE_NAMES.map((n, i) => `
      <div class="tyre ${i % 2 ? 'right' : 'left'}" id="tyre-${i}">
        <div class="tyre-body"><i></i><i></i><i></i><div class="wear"></div></div>
        <div class="tyre-info">
          <div class="tyre-name">${n}</div>
          <div class="tyre-main"><b class="t-surf">—</b><span class="t-wear">—</span></div>
          <div class="tyre-sub">core <span class="t-core">—</span> · <span class="t-psi">—</span></div>
          <div class="tyre-sub">I/M/O <span class="t-imo">—</span></div>
          <div class="tyre-brake">
            <i class="disc"></i>
            <b class="t-brake">—</b>
          </div>
        </div>
      </div>`).join('');
  }

  render(state) {
    const p = state.player;
    if (!p) return;
    const me = state.cars.find((c) => c.isPlayer);

    $('car-title').textContent = `${p.name || 'Car'} · ${p.car || ''}`;

    this.renderDelta(p);
    this.renderCluster(p);
    this.renderBadges(p, state);
    this.renderTiming(p, state);
    this.renderNeighbours(state, me);
    this.renderCarState(p, state, me);
    this.renderTyres(p);
    this.renderLaps(p, state);
  }

  renderDelta(p) {
    const el = $('delta');
    const mode = this.deltaMode;
    const d = p.timing.delta[mode];
    el.textContent = d == null ? '—' : signed(d, 3);
    el.className = `delta ${d == null ? '' : d <= 0 ? 'faster' : 'slower'}`;
    const ref = p.timing.referenceLap[mode];
    $('delta-ref').textContent = ref ? `vs ${mode} ${lapTime(ref)}` : `vs ${mode} lap`;
  }

  renderCluster(p) {
    $('speed').textContent = num(speed(p.speed), 0, '0');
    $('speed-unit').textContent = speedUnit();
    $('rpm').textContent = num(p.rpm, 0, '0');
    $('gear').textContent = p.gear === -1 ? 'R' : p.gear === 0 ? 'N' : String(p.gear);
    const frac = p.maxRpm > 0 ? p.rpm / p.maxRpm : 0;
    $('rpm-fill').style.width = `${Math.min(100, frac * 100)}%`;

    // F1-style shift lights: 5 green, 5 red, 5 blue between 72% and 97% of max rpm; all flash at the limit.
    const lit = Math.round(Math.max(0, Math.min(1, (frac - 0.72) / 0.25)) * SHIFT_LEDS);
    this.leds.forEach((led, i) => {
      led.className = i < lit ? (i < 5 ? 'g' : i < 10 ? 'r' : 'b') : '';
    });
    this.lightsEl.classList.toggle('flash', frac >= 0.97 && Math.floor(performance.now() / 90) % 2 === 0);
  }

  renderBadges(p, state) {
    const b = [];
    const add = (text, cls = '') => b.push(`<span class="badge ${cls}">${text}</span>`);
    if (p.drs.installed) {
      const cls = p.drs.active ? 'on' : p.drs.availableNow ? 'info' : p.drs.availableNext ? 'warn' : '';
      add(p.drs.active ? 'DRS OPEN' : p.drs.availableNow ? 'DRS AVAILABLE' : p.drs.availableNext ? 'DRS ARMED' : 'DRS', cls);
    }
    if (p.ers.mode && p.ers.mode !== 'None') add(`ERS ${p.ers.mode.toUpperCase()}${p.ers.auto ? ' (AUTO)' : ''}`, 'info');
    if (p.ers.boostActive) add('BOOST', 'on');
    if (p.flags.pitLimiter) add('PIT LIMITER', 'warn');
    if (p.flags.absActive) add('ABS', 'alert');
    if (p.flags.engineWarning) add('ENGINE', 'alert');
    if (p.pit.schedule && p.pit.schedule !== 'None') add(`BOX · ${p.pit.schedule.replace(/([a-z])([A-Z])/g, '$1 $2').toUpperCase()}`, 'warn');
    if (p.timing.invalid) add('LAP INVALID', 'alert');
    if (p.damage.crash && p.damage.crash !== 'None') add(p.damage.crash.toUpperCase(), 'alert');
    if (p.clutchState.overheated) add('CLUTCH HOT', 'alert');
    if (p.clutchState.slipping) add('CLUTCH SLIP', 'warn');
    if (p.setup.launch === 'Rev' || p.setup.launch === 'On') add(`LAUNCH ${p.setup.launch.toUpperCase()}`, 'info');
    if (state.session.enforcedPitStopLap > 0) add(`MANDATORY STOP L${state.session.enforcedPitStopLap}`);
    if (p.flags.headlight) add('LIGHTS');
    $('badges').innerHTML = b.join('');
  }

  renderTiming(p) {
    const cur = $('t-current');
    cur.textContent = lapTime(p.timing.current);
    cur.classList.toggle('invalid', !!p.timing.invalid);
    const last = $('t-last');
    last.textContent = lapTime(p.timing.last);
    last.className = p.timing.lastColour ? `t-${p.timing.lastColour}` : '';
    $('t-best').textContent = lapTime(p.timing.best);
    p.timing.sectors.forEach((s, i) => {
      const el = $(`t-s${i + 1}`);
      if (s.t) {
        el.textContent = sectorTime(s.t);
        el.className = `t-${s.c}`;
      } else {
        el.textContent = p.timing.sector === i ? '···' : sectorTime(s.best);
        el.className = 't-prev';
        el.title = s.best ? 'Personal best' : '';
      }
    });
  }

  renderNeighbours(state, me) {
    if (!me) {
      $('neighbours').innerHTML = '';
      return;
    }
    const k = state.cars.indexOf(me);
    const ahead = state.cars[k - 1];
    const behind = state.cars[k + 1];
    const isRace = state.session.sessionState === 'Race';
    const cell = (label, car, gapText) => {
      if (!car) return `<div><label>${label}</label><b>—</b></div>`;
      const dv = car.speed != null && me.speed != null ? Math.round(speed(car.speed) - speed(me.speed)) : null;
      const dvText = dv == null ? '' : ` <small class="${dv > 0 ? 'speed-down' : 'speed-up'}">${dv > 0 ? '+' : ''}${dv} ${speedUnit()}</small>`;
      return `<div><label>${label} · ${escapeHtml(driverCode(car.name))}</label><b>${gapText}</b>${dvText}</div>`;
    };
    const aheadGap = isRace ? gap(me.interval, me.intervalLaps) : gap(me.interval, 0);
    const behindGap = behind ? (isRace ? gap(behind.interval, behind.intervalLaps) : gap(behind.interval, 0)).replace('+', '-') : '';
    $('neighbours').innerHTML = cell('Ahead', ahead, aheadGap) + cell('Behind', behind, behindGap);
  }

  renderCarState(p, state, me) {
    const f = p.fuel;
    $('fuel-fill').style.width = `${Math.min(100, (f.level ?? 0) * 100)}%`;
    $('fuel-litres').textContent = `${num(f.litres, 1)} L`;
    let detail = f.perLap ? `<b>${num(f.perLap, 2)} L/lap</b> · ${num(f.lapsLeft, 1)} laps` : 'Measuring consumption…';
    if (f.perLap && !state.session.timed && state.session.lapsInEvent > 0 && me) {
      const remaining = state.session.lapsInEvent - me.lapsCompleted;
      // Fuel needed to finish, counting the rest of the current lap as a full lap.
      const margin = f.litres - remaining * f.perLap;
      detail += ` · ${remaining} to go · ${margin >= 0 ? `+${margin.toFixed(1)} L spare` : `${margin.toFixed(1)} L short`}`;
    }
    $('fuel-detail').innerHTML = detail;

    $('bb').textContent = p.setup.brakeBias > 0 ? `${(p.setup.brakeBias * 100).toFixed(1)}% F` : '—';
    const setting = (v) => (v == null || v < 0 ? 'off' : String(v));
    $('tcabs').textContent = `${setting(p.setup.tcSetting)} / ${setting(p.setup.absSetting)}`;
    $('oil').innerHTML = `${num(temp(p.engine.oilTemp), 0)}${tempUnit()} <small>${num(p.engine.oilPressure, 0)} kPa</small>`;
    $('water').innerHTML = `${num(temp(p.engine.waterTemp), 0)}${tempUnit()} <small>${num(p.engine.waterPressure, 0)} kPa</small>`;
    $('ers').innerHTML = `${num(p.ers.boostAmount, 0)}% <small>${p.setup.turboBoost > 0 ? `${num(p.setup.turboBoost, 2)} bar` : ''}</small>`;
    $('wings').textContent = `${num(p.setup.frontWing, 2)} / ${num(p.setup.rearWing, 2)}`;
  }

  renderTyres(p) {
    const compounds = [...new Set(p.tyres.map((t) => t.compound).filter(Boolean))];
    $('compound').textContent = compounds.join(' / ');
    p.tyres.forEach((t, i) => {
      const el = $(`tyre-${i}`);
      const stripes = el.querySelectorAll('.tyre-body i');
      // Colour thresholds stay in °C; only the displayed value is converted.
      [t.left, t.centre, t.right].forEach((celsius, k) => {
        stripes[k].style.background = tempColour(celsius, 60, 90, 110);
        stripes[k].title = `${num(temp(celsius), 1)}${tempUnit()}`;
      });
      el.querySelector('.wear').style.height = `${Math.min(100, (t.wear ?? 0) * 100)}%`;
      const q = (sel) => el.querySelector(sel);
      q('.tyre-name').textContent = `${TYRE_NAMES[i]}${t.onGround === false ? ' · AIR' : ''}`;
      q('.t-surf').textContent = `${num(temp(t.surface), 0)}°`;
      q('.t-wear').textContent = `${num((t.wear ?? 0) * 100, 1)}%`;
      q('.t-psi').textContent = `${num(t.pressurePsi, 1)} psi`;
      q('.t-core').textContent = `${num(temp(t.carcass), 0)}°`;
      q('.t-imo').textContent = this.imo(t, i);
      q('.t-brake').textContent = `${num(temp(t.brakeTemp), 0)}°`;

      // Dot colour and glow follow brake temperature.
      const heat = Math.max(0, Math.min(1, ((t.brakeTemp ?? 0) - 200) / 700));
      const disc = q('.tyre-brake');
      disc.style.setProperty('--brake-colour', brakeGlow(t.brakeTemp));
      disc.style.setProperty('--glow', `${(heat * 6).toFixed(1)}px`);
    });
  }

  /** Inner/middle/outer: on left-hand tyres the left edge is the outside. */
  imo(t, i) {
    const [inner, outer] = i % 2 === 0 ? [t.right, t.left] : [t.left, t.right];
    return `${num(temp(inner), 0)}/${num(temp(t.centre), 0)}/${num(temp(outer), 0)}`;
  }

  renderLaps(p, state) {
    const laps = p.laps || [];
    const key = laps.map((l) => `${l.lap}:${l.time}:${l.fuelUsed}`).join('|') + state.session.sessionBest.lap;
    if (key === this.lastLapsKey) return;
    this.lastLapsKey = key;

    const valid = laps.filter((l) => !l.invalid && l.time);
    const pb = valid.length ? Math.min(...valid.map((l) => l.time)) : null;
    const sb = state.session.sessionBest;
    const bestSectors = [0, 1, 2].map((s) => {
      const ts = valid.map((l) => l.sectors[s]).filter((t) => t > 0);
      return ts.length ? Math.min(...ts) : null;
    });
    const cls = (t, personal, session) => {
      if (!t) return '';
      if (session && t <= session + 0.0005) return 't-purple';
      if (personal && t <= personal + 0.0005) return 't-green';
      return '';
    };

    $('laps-sub').textContent = pb ? `PB ${lapTime(pb)} · ${laps.length} laps` : `${laps.length} laps`;
    $('laps').querySelector('tbody').innerHTML = [...laps].reverse().map((l) => `
      <tr class="${l.invalid ? 'invalid' : ''}">
        <td>${l.lap}${l.pitted ? '<span class="tag">PIT</span>' : ''}</td>
        <td class="${l.invalid ? '' : cls(l.time, pb, sb.lap)}">${lapTime(l.time)}</td>
        ${l.sectors.map((t, s) => `<td class="${l.invalid ? '' : cls(t, bestSectors[s], sb.sectors[s])}">${sectorTime(t)}</td>`).join('')}
        <td>${l.fuelUsed ? l.fuelUsed.toFixed(2) : '—'}</td>
      </tr>`).join('') || '<tr><td colspan="6" style="text-align:center;color:var(--muted)">No completed laps yet</td></tr>';
  }
}
