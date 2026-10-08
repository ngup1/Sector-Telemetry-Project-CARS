// Canvas chart: rolling throttle/brake/clutch trace. Colours come from the stylesheet.

import { plotStyle } from './settings.js';

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

function axisLabel(ctx, text, x, y, align = 'right') {
  const st = plotStyle();
  ctx.fillStyle = st.axis;
  ctx.font = `10px ${st.mono}`;
  ctx.textAlign = align;
  ctx.textBaseline = 'middle';
  ctx.fillText(text, x, y);
}

/** Throttle, brake and clutch as plain lines (0–100%) over the last N seconds. */
export class InputTrace {
  constructor(seconds = 15) {
    this.seconds = seconds;
    this.samples = [];
  }

  push(p) {
    const t = performance.now() / 1000;
    this.samples.push({ t, thr: p.throttle ?? 0, brk: p.brake ?? 0, clt: p.clutch ?? 0 });
    while (this.samples.length && this.samples[0].t < t - this.seconds) this.samples.shift();
  }

  draw(canvas) {
    const { ctx, w, h } = setupCanvas(canvas);
    const left = 40, right = 8, top = 24, bottom = 18;
    const pw = w - left - right;
    const ph = h - top - bottom;
    const now = performance.now() / 1000;
    const xOf = (t) => left + pw * (1 - (now - t) / this.seconds);
    const yOf = (v) => top + ph * (1 - v);

    const st = plotStyle();
    ctx.strokeStyle = st.grid;
    ctx.lineWidth = 1;
    for (let s = 0; s <= this.seconds; s += 5) {
      const x = xOf(now - s);
      ctx.beginPath(); ctx.moveTo(x, top); ctx.lineTo(x, top + ph); ctx.stroke();
      axisLabel(ctx, s === 0 ? 'now' : `-${s}s`, x, h - 8, 'center');
    }
    for (let v = 0; v <= 100; v += 25) {
      ctx.beginPath(); ctx.moveTo(left, yOf(v / 100)); ctx.lineTo(left + pw, yOf(v / 100)); ctx.stroke();
      axisLabel(ctx, `${v}%`, left - 6, yOf(v / 100));
    }

    if (this.samples.length < 2) return;

    const line = (key, colour) => {
      ctx.strokeStyle = colour;
      ctx.lineWidth = 2;
      ctx.lineJoin = 'round';
      ctx.beginPath();
      this.samples.forEach((s, i) => (i ? ctx.lineTo(xOf(s.t), yOf(s[key])) : ctx.moveTo(xOf(s.t), yOf(s[key]))));
      ctx.stroke();
    };
    line('clt', st.clutch);
    line('thr', st.throttle);
    line('brk', st.brake);
  }
}

