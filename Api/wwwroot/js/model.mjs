export function formatNumber(value, digits = 2) {
  if (typeof value !== 'number' || !Number.isFinite(value)) return '—';
  return new Intl.NumberFormat('en-US', { minimumFractionDigits: digits, maximumFractionDigits: digits }).format(value);
}
export function escapeHtml(value) {
  return String(value ?? '').replace(/[&<>"']/g, character => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[character]);
}
export function canStart(snapshot) {
  return !!(snapshot?.broker?.terminalConnected && snapshot.ordersEnabledAtTerminal && snapshot.account &&
    snapshot.lastCycleAt && Date.now() - Date.parse(snapshot.lastCycleAt) < 20000 &&
    (snapshot.account.isDemo || snapshot.settings?.allowLiveAccount) && !snapshot.settings?.observationOnly && !snapshot.entriesEnabled && !snapshot.lastError && !snapshot.executionPolicyError &&
    snapshot.risk && snapshot.risk.accountId===snapshot.account.accountId && snapshot.risk.brokerServer===snapshot.account.server &&
    !snapshot.risk.halted && !(snapshot.risk.blockedUntil && Date.parse(snapshot.risk.blockedUntil)>Date.now()));
}
export function chartGeometry(history, width = 860, height = 260) {
  const valid = (history ?? []).filter(c => Number.isFinite(c.openTime) && c.openTime > 0 &&
    [c.open, c.high, c.low, c.close].every(Number.isFinite) && c.low > 0 &&
    c.high >= Math.max(c.open, c.close) && c.low <= Math.min(c.open, c.close));
  const unique = [...new Map(valid.map(c => [c.openTime, c])).values()].sort((a, b) => a.openTime - b.openTime).slice(-90);
  if (!unique.length) return { candles: [], min: 0, max: 0, width, height };
  const low = Math.min(...unique.map(c => c.low)), high = Math.max(...unique.map(c => c.high));
  const padding = Math.max((high - low) * .12, high * .00001, .00000001);
  const min = low - padding, max = high + padding;
  const y = price => 14 + (max - price) / (max - min) * (height - 40);
  const spacing = (width - 76) / unique.length;
  const candles = unique.map((c, i) => ({ ...c, x: 14 + spacing * (i + .5), yOpen: y(c.open),
    yHigh: y(c.high), yLow: y(c.low), yClose: y(c.close), bodyWidth: Math.max(2, spacing * .56), up: c.close >= c.open }));
  return { candles, min, max, width, height, y };
}

export function formatTime(value, seconds = false) {
  if (!value) return '—';
  const date = new Date(typeof value === 'number' ? value * 1000 : value);
  if (!Number.isFinite(date.getTime())) return '—';
  return date.toLocaleTimeString('fa-IR', { hour: '2-digit', minute: '2-digit', ...(seconds ? { second: '2-digit' } : {}) });
}
export function serverTime(value) {
  if (!value) return '—';
  return new Date(value * 1000).toLocaleTimeString('en-GB', { timeZone: 'UTC', hour: '2-digit', minute: '2-digit' });
}
export const actionLabel = value => ({ Buy: 'خرید', Sell: 'فروش', Hold: 'انتظار' })[value] ?? '—';
export const stateLabel = value => ({ Paused: 'ورود خودکار متوقف', Running: 'ورود خودکار فعال', Waiting: 'منتظر ارتباط', Degraded: 'نیاز به بررسی' })[value] ?? 'در حال اتصال';
export function reasonLabel(value) {
  return ({
    'MT4 agent is not connected': 'Agent متاتریدر هنوز متصل نشده است.',
    'New entries are paused': 'ورود جدید متوقف است؛ پایش و تحلیل ادامه دارد.',
    'Observation mode blocks all new entries': 'حالت پایش: ورود جدید مسدود است؛ محافظت معاملات باز ادامه دارد.',
    'No confirmed retest': 'هنوز بازآزمایی معتبر تأیید نشده است.',
    'No valid indicator-based signal detected.': 'شرایط ورود اندیکاتوری تکمیل نشده است.',
    'An owned position already exists on this symbol': 'یک معامله‌ی متعلق به ربات روی این نماد باز است.',
    'An uncertain order blocks new entries': 'نتیجه‌ی یک سفارش نامشخص است؛ ابتدا باید تطبیق داده شود.',
    'Market quote is stale': 'قیمت به‌روز نیست؛ ورود جدید مجاز نیست.',
    'Macro EMA regime does not confirm this direction': 'EMAهای فاز کلان، جهت این ورود را تأیید نمی‌کنند.',
    'ADX is below the entry trend threshold': 'قدرت روند برای ورود کافی نیست.',
    'Closed candle body is below the configured relative threshold': 'نسبت بدنه کندل بسته‌شده کمتر از آستانه تنظیم‌شده است.',
    'Waiting for a later closed-bar breakout retest in the signal direction': 'منتظر ری‌تست تأییدشده روی کندل بعدی و در جهت سیگنال هستیم.',
    'Spread exceeds the absolute or observed relative limit': 'اسپرد از سقف ثابت یا سقف نسبت به میانگین مشاهده‌شده بیشتر است.',
    'Equity drawdown circuit breaker blocks new entries': 'حفاظت افت سرمایه ورود جدید را مسدود کرده است.',
    'Broker minimum volume exceeds the risk budget or protection is invalid': 'حداقل حجم بروکر از بودجه ریسک بیشتر است یا استاپ/هدف معتبر نیست.',
    'Spread exceeds the configured limit': 'اسپرد از سقف مجاز بیشتر است.',
    'Daily trade limit reached': 'سهمیه‌ی روزانه به پایان رسیده است.',
    'EA execution is disabled': 'اجازه‌ی ارسال سفارش در EA خاموش است.',
    'Live account execution is disabled': 'معامله روی حساب واقعی مجاز نیست.',
    'MT4 is disconnected from its broker': 'متاتریدر به بروکر متصل نیست.',
    'Fresh broker state and enabled EA execution are required': 'ابتدا اتصال و مجوز اجرای EA را بررسی کنید.',
    'Pause new entries before changing settings': 'پیش از تغییر تنظیمات، ورود خودکار را متوقف کنید.'
  })[value] ?? value ?? 'منتظر دریافت داده از متاتریدر';
}
