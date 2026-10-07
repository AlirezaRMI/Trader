const paths = {
  grid: '<rect x="3" y="3" width="7" height="7" rx="2"/><rect x="14" y="3" width="7" height="7" rx="2"/><rect x="3" y="14" width="7" height="7" rx="2"/><rect x="14" y="14" width="7" height="7" rx="2"/>',
  chart: '<path d="M3 3v18h18M7 15l4-5 4 3 5-7"/><path d="M17 6h3v3"/>',
  wallet: '<rect x="3" y="6" width="18" height="15" rx="3"/><path d="M3 10h18M7 6V3h10v3M15 14h6v4h-6z"/>',
  activity: '<path d="M3 12h4l3-8 4 16 3-8h4"/>',
  signal: '<path d="M4 18v3M9 13v8M14 8v13M19 3v18"/>',
  sliders: '<path d="M4 7h5M13 7h7M4 17h10M18 17h2"/><circle cx="11" cy="7" r="2"/><circle cx="16" cy="17" r="2"/>',
  shield: '<path d="m12 3 8 3v6c0 5-8 9-8 9s-8-4-8-9V6zM8 12l3 3 5-6"/>',
  search: '<circle cx="10" cy="10" r="6"/><path d="m15 15 5 5"/>',
  play: '<path d="m8 4 12 8-12 8z"/>',
  pause: '<path d="M8 5v14M16 5v14"/>',
  refresh: '<path d="M20 11a8 8 0 0 0-14-5L3 9M3 3v6h6M4 13a8 8 0 0 0 14 5l3-3M21 21v-6h-6"/>',
  arrow: '<path d="M20 12H4m6-6-6 6 6 6"/>',
  close: '<path d="m6 6 12 12M6 18 18 6"/>',
  menu: '<path d="M4 6h16M4 12h16M4 18h16"/>',
  bell: '<path d="M18 8a6 6 0 0 0-12 0c0 8-3 8-3 10h18c0-2-3-2-3-10M10 21h4"/>',
  download: '<path d="M12 3v12m-5-5 5 5 5-5M4 17v4h16v-4"/>',
  check: '<path d="m5 12 4 4 10-10"/>',
  clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l4 2"/>',
  info: '<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7v1"/>',
  cloud: '<path d="M7 18a5 5 0 1 1 1-10 6 6 0 0 1 12 3 4 4 0 0 1 0 7z"/>',
  terminal: '<rect x="3" y="4" width="18" height="13" rx="2"/><path d="M7 21h10M12 17v4m-5-13 3 3-3 3m6 0h4"/>'
};
export const icon = name => `<svg class="icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${paths[name] ?? paths.info}</svg>`;
export function hydrateIcons(root = document) { root.querySelectorAll('[data-icon]').forEach(e => { e.innerHTML = icon(e.dataset.icon); }); }
