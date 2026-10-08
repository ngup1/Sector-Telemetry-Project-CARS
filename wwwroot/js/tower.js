import { lapTime, sectorTime, gap, driverCode, escapeHtml, PIT_LABELS, STATE_LABELS } from './format.js';
import { speed } from './settings.js';

/** F1-style timing tower: position, gaps, last/best laps and live sector splits for every car. */
export class Tower {
  constructor(table, onSelect) {
    this.tbody = table.querySelector('tbody');
    this.tbody.addEventListener('click', (e) => {
      const row = e.target.closest('tr[data-idx]');
      if (row) onSelect(Number(row.dataset.idx));
    });
  }

  render(state, { gapMode, selected, colourFor }) {
    const isRace = state.session.sessionState === 'Race' || state.session.sessionState === 'FormationLap';
    const rows = state.cars.map((c, k) => this.row(c, k, isRace, gapMode, selected, colourFor));
    this.tbody.innerHTML = rows.join('');
  }

  row(c, k, isRace, gapMode, selected, colourFor) {
    const classes = [];
    if (c.isPlayer) classes.push('player');
    if (c.idx === selected) classes.push('selected');
    if (STATE_LABELS[c.state] && c.state !== 'Finished') classes.push('out');

    let gapCell;
    if (k === 0) {
      gapCell = `<span class="leader-tag">${isRace ? (gapMode === 'leader' ? 'LEADER' : 'INTERVAL') : 'FASTEST'}</span>`;
    } else if (isRace) {
      gapCell = gapMode === 'leader' ? gap(c.gap, c.gapLaps) : gap(c.interval, c.intervalLaps);
    } else {
      gapCell = gapMode === 'leader' ? gap(c.gap, 0) : gap(c.interval, 0);
    }

    // Completed sectors of the current lap in full colour; the sector being driven shows "···";
    // sectors not yet reached this lap show last lap's time dimmed.
    const sectors = c.sectors.map((s, i) => {
      if (s.t && !s.prev) return `<td class="c-sec t-${s.c}">${sectorTime(s.t)}</td>`;
      if (c.sector === i) return '<td class="c-sec t-prev">···</td>';
      if (s.t) return `<td class="c-sec t-${s.c} t-prev">${sectorTime(s.t)}</td>`;
      return '<td class="c-sec t-prev">—</td>';
    });

    let pit = '';
    if (STATE_LABELS[c.state]) {
      pit = `<span class="state-badge">${STATE_LABELS[c.state]}</span>`;
    } else if (PIT_LABELS[c.pit]) {
      pit = `<span class="pit-badge ${c.pit === 'InPit' ? 'in' : ''}">${PIT_LABELS[c.pit]}</span>`;
    }
    if (c.pitStops > 0) pit += `<span class="pit-stops" title="Pit stops">${c.pitStops}</span>`;

    return `<tr data-idx="${c.idx}" class="${classes.join(' ')}">
      <td class="c-pos">${c.pos || '—'}</td>
      <td class="c-name"><div class="name-cell">
        <span class="team-bar" style="background:${colourFor(c)}"></span>
        <span class="name" title="${escapeHtml(c.car)}">${escapeHtml(c.name)}</span>
        <span class="code">${escapeHtml(driverCode(c.name))}</span>
        ${c.invalid ? '<span class="inv" title="Current lap invalidated">INV</span>' : ''}
      </div></td>
      <td class="c-gap">${gapCell}</td>
      <td class="c-time t-${c.lastColour || 'none'}">${lapTime(c.last)}</td>
      <td class="c-time ${c.bestColour === 'purple' ? 't-purple' : ''}">${lapTime(c.best)}</td>
      ${sectors.join('')}
      <td class="c-lap">${c.lap}</td>
      <td class="c-pit">${pit}</td>
      <td class="c-spd">${c.speed == null ? '—' : Math.round(speed(c.speed))}</td>
    </tr>`;
  }
}
