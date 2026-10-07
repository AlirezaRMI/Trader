import { chartGeometry, formatNumber, escapeHtml, serverTime } from './model.mjs?v=20261007-ui2';
import { icon } from './icons.mjs?v=20261007-ui2';
export function renderChart(history, analysis) {
  const plot = chartGeometry(history);
  if (plot.candles.length < 2) return `<div class="chart-empty"><div class="empty-chart-lines" aria-hidden="true"></div><span class="empty-icon">${icon('chart')}</span><strong>نمودار، بعد از اتصال زنده می‌شود</strong><p>تاریخچه‌ی کندل‌های بسته‌شده از متاتریدر دریافت خواهد شد.</p><span class="tiny-label">NO SYNTHETIC DATA</span></div>`;
  const digits = analysis?.specification?.digits ?? 5;
  const grid = Array.from({ length: 5 }, (_, i) => {
    const value = plot.max - (plot.max - plot.min) * i / 4, y = plot.y(value);
    return `<line x1="10" x2="787" y1="${y}" y2="${y}" class="chart-grid"/><text x="808" y="${y + 4}" class="chart-axis">${formatNumber(value, digits)}</text>`;
  }).join('');
  const candles = plot.candles.map(c => `<g class="candle ${c.up ? 'up' : 'down'}"><title>${serverTime(c.openTime)} / O ${c.open} / H ${c.high} / L ${c.low} / C ${c.close}</title><line x1="${c.x}" x2="${c.x}" y1="${c.yHigh}" y2="${c.yLow}"/><rect x="${c.x - c.bodyWidth/2}" y="${Math.min(c.yOpen,c.yClose)}" width="${c.bodyWidth}" height="${Math.max(1.5,Math.abs(c.yOpen-c.yClose))}" rx=".8"/></g>`).join('');
  const zones = (analysis?.zones ?? []).filter(z => z.bottom >= plot.min && z.top <= plot.max).slice(0, 8).map(z =>
    `<rect x="10" width="777" y="${plot.y(z.top)}" height="${Math.max(2,plot.y(z.bottom)-plot.y(z.top))}" class="zone-band"><title>ناحیه: ${z.bottom} — ${z.top} / قدرت: ${z.strength}</title></rect>`).join('');
  const ticks = plot.candles.filter((_,i) => i % Math.max(1, Math.floor(plot.candles.length/5)) === 0).slice(0,5).map(c => `<text x="${c.x}" y="254" text-anchor="middle" class="chart-axis">${serverTime(c.openTime)}</text>`).join('');
  const last = plot.candles.at(-1);
  return `<svg viewBox="0 0 860 260" class="market-chart" role="img" aria-label="نمودار کندلی واقعی؛ زمان کندل‌ها مطابق سرور متاتریدر"><g>${grid}${zones}<line x1="10" x2="787" y1="${last.yClose}" y2="${last.yClose}" class="current-price-line"/>${candles}${ticks}</g></svg>`;
}
