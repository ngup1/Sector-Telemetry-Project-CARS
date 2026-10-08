import { loadSettings, saveSettings, settings, onSettingsChange } from './settings.js';

const status = document.getElementById('save-state');
const groups = [...document.querySelectorAll('[data-setting]')];

function render() {
  const s = settings();
  for (const g of groups) {
    g.querySelectorAll('button').forEach((b) => b.classList.toggle('on', b.dataset.value === s[g.dataset.setting]));
  }
}

for (const g of groups) {
  g.addEventListener('click', async (e) => {
    const b = e.target.closest('button[data-value]');
    if (!b) return;
    status.textContent = 'Saving…';
    const ok = await saveSettings({ [g.dataset.setting]: b.dataset.value });
    status.textContent = ok ? 'Saved' : 'Not saved: server unreachable';
  });
}

onSettingsChange(render);
render();
loadSettings();
