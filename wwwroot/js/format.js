// Shared formatting helpers for the dashboard.

export function lapTime(t) {
  if (t == null || !(t > 0)) return '—';
  const m = Math.floor(t / 60);
  const s = t - m * 60;
  return m > 0 ? `${m}:${s.toFixed(3).padStart(6, '0')}` : s.toFixed(3);
}

export function sectorTime(t) {
  return t == null || !(t > 0) ? '—' : t.toFixed(3);
}

export function clock(seconds) {
  if (seconds == null || seconds < 0) return '—';
  const s = Math.floor(seconds % 60);
  const m = Math.floor((seconds / 60) % 60);
  const h = Math.floor(seconds / 3600);
  const mm = String(m).padStart(2, '0');
  const ss = String(s).padStart(2, '0');
  return h > 0 ? `${h}:${mm}:${ss}` : `${mm}:${ss}`;
}

/** "+1.234", "+1 LAP", or "—". */
export function gap(seconds, laps) {
  if (laps > 0) return `+${laps} LAP${laps > 1 ? 'S' : ''}`;
  if (seconds == null) return '—';
  return `+${seconds.toFixed(3)}`;
}

export function signed(v, digits = 3) {
  if (v == null) return '—';
  const s = v.toFixed(digits);
  return v > 0 ? `+${s}` : s;
}

export function num(v, digits = 0, fallback = '—') {
  return v == null || Number.isNaN(v) ? fallback : v.toFixed(digits);
}

/** Three-letter driver code, F1 style: "A. Moreau" -> "MOR". */
export function driverCode(name) {
  if (!name) return '???';
  const parts = name.trim().split(/\s+/);
  const last = parts[parts.length - 1].replace(/[^\p{L}]/gu, '');
  return (last || parts[0]).slice(0, 3).toUpperCase();
}

/** Stable team-ish colour per car/class name, so the same car keeps its colour between sessions. */
export function carColour(key) {
  let h = 2166136261;
  for (const ch of key || '') h = Math.imul(h ^ ch.charCodeAt(0), 16777619);
  const hue = ((h >>> 0) % 360);
  return `hsl(${hue} 70% 58%)`;
}

export function escapeHtml(s) {
  return String(s ?? '').replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
}

/** Tyre/brake temperature to colour: cold blue -> optimal green -> hot red. */
export function tempColour(t, cold, ideal, hot) {
  if (t == null) return 'var(--muted-2)';
  const stops = [
    [cold, [70, 130, 255]],
    [ideal, [40, 200, 110]],
    [hot, [255, 170, 30]],
    [hot + (hot - ideal), [255, 50, 50]],
  ];
  if (t <= stops[0][0]) return rgb(stops[0][1]);
  for (let i = 1; i < stops.length; i++) {
    if (t <= stops[i][0]) {
      const [t0, c0] = stops[i - 1];
      const [t1, c1] = stops[i];
      const f = (t - t0) / (t1 - t0);
      return rgb(c0.map((v, k) => v + (c1[k] - v) * f));
    }
  }
  return rgb(stops[stops.length - 1][1]);
}

function rgb([r, g, b]) {
  return `rgb(${r | 0} ${g | 0} ${b | 0})`;
}

export const FLAG_STYLES = {
  None: null,
  Green: { label: 'GREEN', colour: 'var(--flag-green)' },
  Blue: { label: 'BLUE FLAG', colour: 'var(--flag-blue)' },
  WhiteSlowCar: { label: 'SLOW CAR', colour: '#e8e8e8' },
  WhiteFinalLap: { label: 'FINAL LAP', colour: '#e8e8e8' },
  Red: { label: 'RED FLAG', colour: 'var(--flag-red)' },
  Yellow: { label: 'YELLOW', colour: 'var(--flag-yellow)' },
  DoubleYellow: { label: 'DOUBLE YELLOW', colour: 'var(--flag-yellow)' },
  BlackAndWhite: { label: 'BLACK & WHITE', colour: '#bbbbbb' },
  BlackOrangeCircle: { label: 'MEATBALL', colour: '#ff8a00' },
  Black: { label: 'BLACK FLAG', colour: '#555' },
  Chequered: { label: 'CHEQUERED', colour: '#e8e8e8' },
};

export const SESSION_NAMES = {
  Invalid: '—', Practice: 'PRACTICE', Test: 'TEST', Qualify: 'QUALIFYING',
  FormationLap: 'FORMATION LAP', Race: 'RACE', TimeAttack: 'TIME TRIAL',
};

export const YELLOW_STATES = {
  Pending: 'FCY PENDING', PitsClosed: 'SC · PITS CLOSED', PitLeadLap: 'SC · LEAD LAP MAY PIT',
  PitsOpen: 'SC · PITS OPEN', PitsOpen2: 'SC · PITS OPEN', LastLap: 'SC IN THIS LAP',
  Resume: 'SC ENDING', RaceHalt: 'RACE HALTED',
};

export const PIT_LABELS = {
  DrivingIntoPits: 'PIT IN', InPit: 'IN PIT', DrivingOutOfPits: 'PIT OUT',
  InGarage: 'GARAGE', DrivingOutOfGarage: 'OUT',
};

export const STATE_LABELS = {
  Finished: 'FIN', Disqualified: 'DSQ', Retired: 'RET', Dnf: 'DNF',
};
