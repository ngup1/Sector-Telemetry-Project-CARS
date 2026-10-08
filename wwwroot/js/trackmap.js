import { driverCode } from './format.js';
import { plotStyle, speed } from './settings.js';

const SNAP_DISTANCE = 150; // metres: jumps larger than this are teleports (pit, reset), not motion

/** Canvas track map: learned outline coloured by sector, with smoothly interpolated car markers. */
export class TrackMap {
  constructor(canvas, { onSelect }) {
    this.canvas = canvas;
    this.ctx = canvas.getContext('2d');
    this.track = null;
    this.cars = [];
    this.anim = new Map(); // idx -> { fx, fz, tx, tz, t0 }
    this.tickInterval = 50;
    this.lastTick = 0;
    this.playerIdx = -1;
    this.selected = -1;
    this.mode = 'team';
    this.labels = true;
    this.flip = false;
    this.rotation = 0;
    this.colourFor = () => '#888';
    this.bounds = null;
    this.speedRange = [60, 320];

    canvas.addEventListener('click', (e) => {
      const hit = this.hitTest(e.offsetX, e.offsetY);
      onSelect(hit);
    });
    new ResizeObserver(() => this.resize()).observe(canvas);
    this.resize();
    const loop = () => {
      this.draw();
      requestAnimationFrame(loop);
    };
    requestAnimationFrame(loop);
  }

  resize() {
    const dpr = window.devicePixelRatio || 1;
    const w = this.canvas.clientWidth;
    const h = this.canvas.clientHeight;
    this.canvas.width = Math.max(1, Math.round(w * dpr));
    this.canvas.height = Math.max(1, Math.round(h * dpr));
    this.ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    this.w = w;
    this.h = h;
  }

  setTrack(track) {
    this.track = track;
    this.bounds = null;
  }

  setView({ flip, rotation }) {
    this.flip = flip;
    this.rotation = rotation;
    this.bounds = null;
  }

  update(cars, playerIdx) {
    const now = performance.now();
    if (this.lastTick) this.tickInterval = 0.8 * this.tickInterval + 0.2 * Math.min(200, now - this.lastTick);
    this.lastTick = now;
    this.playerIdx = playerIdx;
    this.cars = cars;

    const seen = new Set();
    let maxSpeed = 0;
    for (const c of cars) {
      seen.add(c.idx);
      maxSpeed = Math.max(maxSpeed, c.speed || 0);
      const a = this.anim.get(c.idx);
      if (!a) {
        this.anim.set(c.idx, { fx: c.x, fz: c.z, tx: c.x, tz: c.z, t0: now });
        continue;
      }
      const [cx, cz] = this.interp(a, now);
      const jump = Math.hypot(c.x - cx, c.z - cz) > SNAP_DISTANCE;
      a.fx = jump ? c.x : cx;
      a.fz = jump ? c.z : cz;
      a.tx = c.x;
      a.tz = c.z;
      a.t0 = now;
    }
    for (const idx of this.anim.keys()) if (!seen.has(idx)) this.anim.delete(idx);
    // Speed colour scale adapts to the fastest car seen, so it works for karts and prototypes alike.
    this.speedRange[1] = Math.max(this.speedRange[1] * 0.999, maxSpeed, 120);
    if (!this.hasOutline()) this.bounds = null;
  }

  interp(a, now) {
    const f = Math.min(1, (now - a.t0) / this.tickInterval);
    return [a.fx + (a.tx - a.fx) * f, a.fz + (a.tz - a.fz) * f];
  }

  hasOutline() {
    return this.track && this.track.points && this.track.points.some(Boolean);
  }

  /** World (x, z) -> view space before fitting: applies mirror and rotation. */
  orient(x, z) {
    let u = this.flip ? -x : x;
    let v = z;
    for (let r = 0; r < this.rotation; r++) [u, v] = [-v, u];
    return [u, v];
  }

  computeBounds() {
    const pts = [];
    if (this.hasOutline()) {
      for (const p of this.track.points) if (p) pts.push(this.orient(p[0], p[1]));
    } else {
      for (const c of this.cars) if (c.x || c.z) pts.push(this.orient(c.x, c.z));
    }
    if (pts.length === 0) return null;
    let minU = Infinity, maxU = -Infinity, minV = Infinity, maxV = -Infinity;
    for (const [u, v] of pts) {
      minU = Math.min(minU, u); maxU = Math.max(maxU, u);
      minV = Math.min(minV, v); maxV = Math.max(maxV, v);
    }
    return { minU, maxU, minV, maxV };
  }

  toScreen(x, z) {
    const b = this.bounds;
    const pad = 36;
    const sw = Math.max(1, b.maxU - b.minU);
    const sh = Math.max(1, b.maxV - b.minV);
    const scale = Math.min((this.w - pad * 2) / sw, (this.h - pad * 2) / sh);
    const ox = (this.w - sw * scale) / 2;
    const oy = (this.h - sh * scale) / 2;
    const [u, v] = this.orient(x, z);
    return [ox + (u - b.minU) * scale, oy + (v - b.minV) * scale];
  }

  draw() {
    const ctx = this.ctx;
    ctx.clearRect(0, 0, this.w, this.h);
    if (!this.bounds) this.bounds = this.computeBounds();
    if (!this.bounds) return;

    if (this.hasOutline()) this.drawOutline();
    this.drawCars();
  }

  drawOutline() {
    const ctx = this.ctx;
    const { points, bucketSize, sectorBoundaries, trackLength } = this.track;
    const st = plotStyle();
    const sectorColour = st.sectors;
    const b1 = sectorBoundaries?.[0] ?? trackLength / 3;
    const b2 = sectorBoundaries?.[1] ?? (2 * trackLength) / 3;
    const sectorOf = (i) => {
      const d = i * bucketSize;
      return d < b1 ? 0 : d < b2 ? 1 : 2;
    };

    // Split into runs of consecutive known points; small holes are bridged.
    const runs = [];
    let run = [];
    let missing = 0;
    for (let i = 0; i < points.length; i++) {
      if (points[i]) {
        run.push(i);
        missing = 0;
      } else if (++missing > 6 && run.length) {
        runs.push(run);
        run = [];
      }
    }
    if (run.length) runs.push(run);
    const closed = runs.length === 1 && points[0] && points[points.length - 1];

    ctx.lineJoin = 'round';
    ctx.lineCap = 'round';

    // Wide dark base.
    ctx.strokeStyle = st.track;
    ctx.lineWidth = 10;
    for (const r of runs) {
      ctx.beginPath();
      r.forEach((i, k) => {
        const [sx, sy] = this.toScreen(points[i][0], points[i][1]);
        k ? ctx.lineTo(sx, sy) : ctx.moveTo(sx, sy);
      });
      if (closed) ctx.closePath();
      ctx.stroke();
    }

    // Sector-coloured centre line.
    ctx.lineWidth = 3;
    for (const r of runs) {
      for (let k = 1; k < r.length + (closed ? 1 : 0); k++) {
        const i0 = r[k - 1];
        const i1 = r[k % r.length];
        const [x0, y0] = this.toScreen(points[i0][0], points[i0][1]);
        const [x1, y1] = this.toScreen(points[i1][0], points[i1][1]);
        ctx.strokeStyle = sectorColour[sectorOf(i0)];
        ctx.beginPath();
        ctx.moveTo(x0, y0);
        ctx.lineTo(x1, y1);
        ctx.stroke();
      }
    }
    ctx.globalAlpha = 1;

    // Start/finish line, drawn perpendicular to the track direction.
    const first = points.findIndex(Boolean);
    const next = points.findIndex((p, i) => p && i > first + 2);
    if (first >= 0 && next > 0) {
      const [ax, ay] = this.toScreen(points[first][0], points[first][1]);
      const [bx, by] = this.toScreen(points[next][0], points[next][1]);
      const len = Math.hypot(bx - ax, by - ay) || 1;
      const nx = -(by - ay) / len;
      const ny = (bx - ax) / len;
      ctx.strokeStyle = st.marker;
      ctx.lineWidth = 3;
      ctx.beginPath();
      ctx.moveTo(ax - nx * 10, ay - ny * 10);
      ctx.lineTo(ax + nx * 10, ay + ny * 10);
      ctx.stroke();
    }
  }

  carColour(c) {
    if (this.mode === 'speed') return speedRamp(((c.speed ?? 0) - this.speedRange[0]) / (this.speedRange[1] - this.speedRange[0]));
    if (this.mode === 'delta') {
      const player = this.cars.find((p) => p.idx === this.playerIdx);
      if (!player || c.idx === this.playerIdx) return '#ffffff';
      return deltaRamp(((c.speed ?? 0) - (player.speed ?? 0)) / 40);
    }
    return this.colourFor(c);
  }

  drawCars() {
    const ctx = this.ctx;
    const st = plotStyle();
    const now = performance.now();
    this.screenPos = new Map();
    const player = this.cars.find((p) => p.idx === this.playerIdx);

    // Draw back-to-front so the leader and the player end up on top.
    const order = [...this.cars].sort((a, b) => (b.pos || 99) - (a.pos || 99));
    order.sort((a, b) => (a.idx === this.playerIdx) - (b.idx === this.playerIdx) || (a.idx === this.selected) - (b.idx === this.selected));

    for (const c of order) {
      const a = this.anim.get(c.idx);
      if (!a || (!a.tx && !a.tz)) continue;
      const [x, z] = this.interp(a, now);
      const [sx, sy] = this.toScreen(x, z);
      this.screenPos.set(c.idx, [sx, sy]);

      const isPlayer = c.idx === this.playerIdx;
      const inPits = c.pit && c.pit !== 'None';
      const r = isPlayer ? 10 : 8.5;

      ctx.globalAlpha = inPits ? 0.45 : 1;
      if (c.idx === this.selected) {
        const pulse = 4 + 2 * Math.sin(now / 160);
        ctx.strokeStyle = '#fff';
        ctx.lineWidth = 2;
        ctx.beginPath();
        ctx.arc(sx, sy, r + pulse, 0, Math.PI * 2);
        ctx.stroke();
      }

      ctx.fillStyle = this.carColour(c);
      ctx.beginPath();
      ctx.arc(sx, sy, r, 0, Math.PI * 2);
      ctx.fill();
      if (isPlayer) {
        ctx.strokeStyle = st.player;
        ctx.lineWidth = 2.5;
        ctx.stroke();
      }

      ctx.fillStyle = st.posText;
      ctx.font = `bold ${isPlayer ? 11 : 10}px ${st.sans}`;
      ctx.textAlign = 'center';
      ctx.textBaseline = 'middle';
      ctx.fillText(String(c.pos || ''), sx, sy + 0.5);

      if (this.labels) {
        let label = driverCode(c.name);
        if (this.mode === 'delta' && player && !isPlayer && c.speed != null && player.speed != null) {
          const d = Math.round(speed(c.speed) - speed(player.speed));
          label += ` ${d > 0 ? '+' : ''}${d}`;
        } else if (this.mode === 'speed' && c.speed != null) {
          label += ` ${Math.round(speed(c.speed))}`;
        }
        ctx.font = `11px ${st.mono}`;
        ctx.textAlign = 'left';
        ctx.fillStyle = isPlayer ? st.player : st.label;
        ctx.fillText(label, sx + r + 4, sy - r + 2);
      }
      ctx.globalAlpha = 1;
    }
  }

  hitTest(px, py) {
    if (!this.screenPos) return -1;
    let best = -1;
    let bestD = 16;
    for (const [idx, [sx, sy]] of this.screenPos) {
      const d = Math.hypot(sx - px, sy - py);
      if (d < bestD) {
        bestD = d;
        best = idx;
      }
    }
    return best;
  }
}

/** Slow -> fast: blue, cyan, green, yellow, red. */
export function speedRamp(f) {
  const stops = [[45, 110, 255], [40, 210, 255], [40, 210, 110], [255, 215, 0], [255, 60, 60]];
  return ramp(stops, f);
}

/** Slower than you -> faster than you: blue, grey, orange. */
export function deltaRamp(f) {
  const stops = [[60, 140, 255], [150, 155, 170], [255, 120, 30]];
  return ramp(stops, (f + 1) / 2);
}

function ramp(stops, f) {
  const t = Math.max(0, Math.min(1, f)) * (stops.length - 1);
  const i = Math.min(stops.length - 2, Math.floor(t));
  const k = t - i;
  const c = stops[i].map((v, j) => Math.round(v + (stops[i + 1][j] - v) * k));
  return `rgb(${c[0]} ${c[1]} ${c[2]})`;
}
