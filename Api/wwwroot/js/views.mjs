import { formatNumber as num, escapeHtml as e, actionLabel, stateLabel, reasonLabel, formatTime, canStart } from './model.mjs?v=20261007-ui2';
import { icon } from './icons.mjs?v=20261007-ui2';
import { renderChart } from './chart.mjs?v=20261007-ui2';
import { renderResearch, strategySettings } from './research.mjs?v=20261007-ui2';
import { renderTradeHistory, tradeJournalDetail, eventDate, precise } from './trade-history.mjs?v=20261007-ui2';
import { renderRiskProfiles, riskChartNote, riskPending } from './risk-profiles.mjs?v=20261007-ui2';

export const views = { overview: 'نمای کلی', market: 'بازار و تحلیل', research:'آزمایشگاه استراتژی', positions: 'معاملات', journal: 'ژورنال رویدادها', connections: 'اتصال‌ها و سلامت', settings: 'تنظیمات ربات' };
const badge = (text, kind='neutral') => '<span class="badge ' + kind + '">' + e(text) + '</span>';
const heading = (title, subtitle, actions='') => '<div class="page-heading"><div><p class="eyebrow">TRADER / WORKSPACE</p><h1>' + title + '</h1><p>' + subtitle + '</p></div><div class="heading-actions">' + actions + '</div></div>';
const refreshButton = '<button class="button secondary compact" data-action="refresh">' + icon('refresh') + 'به‌روزرسانی</button>';
const empty = (title, text, name='wallet') => '<div class="empty-state"><span class="empty-icon">' + icon(name) + '</span><h3>' + title + '</h3><p>' + text + '</p></div>';
const sectionHead = (label, sub='', right='') => '<div class="section-heading"><div><h2>' + label + '</h2>' + (sub ? '<p>' + sub + '</p>' : '') + '</div>' + right + '</div>';
const timeframeLabel = minutes => minutes>=60?'H'+minutes/60:'M'+minutes;

function accountCards(s) {
  const a=s.account, currency=e(a?.currency ?? '');
  const pnl=a ? (s.positions ?? []).reduce((sum,p)=>sum+p.profit,0) : null;
  const metric=(label,value,note,name,kind='') => '<article class="glass metric '+kind+'"><div class="metric-label">'+label+'<span class="metric-icon">'+icon(name)+'</span></div><div class="metric-value ltr">'+num(value)+'<small>'+currency+'</small></div><div class="metric-note">'+note+'</div></article>';
  return '<div class="metrics-grid">'+metric('ارزش حساب',a?.equity,'Equity / آخرین وضعیت بروکر','wallet','accent')+
    metric('موجودی حساب',a?.balance,'Balance / بدون سود و زیان شناور','cloud')+
    metric('مارجین آزاد',a?.freeMargin,'ظرفیت اعلام‌شده توسط بروکر','shield')+
    metric('سود و زیان باز',pnl,a ? (s.positions?.length ?? 0)+' معامله‌ی متعلق به ربات' : 'هنوز حسابی متصل نشده است','activity',pnl>0?'positive':pnl<0?'negative':'')+'</div>';
}
function pipeline(s) {
  const hasAnalysis=!!s.analysis;
  const steps=[['متاتریدر',s.broker?.terminalConnected],['داده‌ی بازار',hasAnalysis],['تحلیل',hasAnalysis],['کنترل ریسک',hasAnalysis],['اجرا',s.entriesEnabled&&s.ordersEnabledAtTerminal]];
  return '<div class="pipeline">'+steps.map(([label,ok],i)=>'<div class="pipeline-step '+(ok?'done':'')+'"><span class="pipeline-node">'+(ok?icon('check'):String(i+1))+'</span><small>'+label+'</small></div>').join('')+'</div>';
}
function hero(s, ui) {
  const connected=s.broker?.terminalConnected, active=s.entriesEnabled;
  const button = active ? '<button class="button secondary" data-action="pause">'+icon('pause')+'توقف ورود جدید</button>' :
    '<button class="button primary" data-action="start" '+(canStart(s)&&!riskPending(ui.risk)?'':'disabled')+'>'+icon('play')+'فعال‌سازی ورود خودکار</button>';
  return '<section class="glass hero"><div class="hero-copy"><div class="hero-eyebrow"><span class="eyebrow">AUTONOMOUS TRADING</span>'+badge(stateLabel(s.state),active?'success':'purple')+'</div><h2>'+
    (connected?'بازار را ببین.<br><span>فرمان را در دست بگیر.</span>':'همه‌چیز، در یک نگاه.<br><span>منتظر اتصال متاتریدر.</span>')+
    '</h2><p>'+e(s.lastError ? reasonLabel(s.lastError) : 'تحلیل، مدیریت ریسک و اجرای ربات؛ با دید کامل روی هر تصمیم.')+'</p><div class="hero-actions">'+button+'<a class="text-link" href="#connections">بررسی مسیر ارتباط '+icon('arrow')+'</a></div>'+
    '<small class="safety-caption">'+icon('shield')+'توقف ورود، معاملات باز و محافظت بروکر را غیرفعال نمی‌کند.</small></div><div class="hero-visual" aria-hidden="true"><div class="orbital orbit-one"></div><div class="orbital orbit-two"></div><div class="orbital orbit-three"></div><div class="core-symbol">'+icon('activity')+'</div><span class="orbit-label orbit-label-top">ANALYZE</span><span class="orbit-label orbit-label-bottom">PROTECT · EXECUTE</span></div>'+pipeline(s)+'</section>';
}
function chartPanel(s, ui) {
  const a=s.analysis, entry=a?.entry, symbol=a?.symbol ?? s.settings?.symbol ?? '';
  return '<section class="glass chart-panel">'+sectionHead('نبض بازار',symbol ? e(symbol)+' / کندل‌های بسته‌شده' : 'داده‌ی مستقیم متاتریدر',
    '<div class="timeframes">'+[...new Set([s.settings?.strategy?.entryTimeframeMinutes??5,s.settings?.strategy?.patternTimeframeMinutes??15,s.settings?.strategy?.phaseTimeframeMinutes??60])].map(t=>'<button data-action="timeframe" data-timeframe="'+t+'" class="'+(ui.timeframe===t?'selected':'')+'" '+(!a?'disabled':'')+'>'+(t>=60?t/60+'H':t+'M')+'</button>').join('')+'</div>')+
    '<div class="quote-row"><div><strong class="ltr">'+e(symbol||'—')+'</strong><span>'+badge(a?'داده‌ی واقعی':'منتظر اتصال',a?'success':'neutral')+'</span></div><div class="quote-price ltr">'+num(entry?.bid,a?.specification?.digits ?? 5)+'<small> BID</small></div></div>'+
    (ui.chartLoading ? '<div class="chart-empty"><span class="loader"></span><p>در حال دریافت تاریخچه…</p></div>' : renderChart(ui.timeframe===(s.settings?.strategy?.entryTimeframeMinutes??5) ? a?.history : ui.chartHistory,a))+
    riskChartNote(s,ui.risk)+'<div class="chart-footer"><span><i class="legend-dot green"></i>صعودی <i class="legend-dot red"></i>نزولی <i class="legend-dot lavender"></i>ناحیه‌ی قیمت</span><small>زمان کندل‌ها: سرور متاتریدر</small></div></section>';
}
function riskPanel(s) {
  const count=s.confirmedTradesToday ?? 0, limit=s.settings?.dailyTradeLimit ?? 10, ratio=limit>0?Math.min(1,count/limit):0;
  return '<section class="glass risk-panel">'+sectionHead('محافظ ریسک','محدودیت‌ها پیش از ارسال سفارش',icon('shield'))+
    '<div class="quota-ring"><svg viewBox="0 0 120 120" aria-hidden="true"><circle cx="60" cy="60" r="49" class="ring-track"/><circle cx="60" cy="60" r="49" class="ring-fill" stroke-dasharray="'+(ratio*308)+' 308"/></svg><div><strong class="ltr">'+count+'<small> / '+limit+'</small></strong><span>معامله‌ی تأییدشده امروز</span></div></div>'+
    '<div class="risk-rows"><div><span>سقف ریسک هر ورود</span><strong class="ltr">'+num(s.settings?.maximumRiskAmount)+' '+e(s.account?.currency ?? 'واحد حساب')+'</strong></div>'+
    '<div><span>سقف نسبی ریسک</span><strong class="ltr">'+num(s.settings?.riskPercent,2)+'%</strong></div><div><span>ریسک سفارش پیشنهادی</span><strong class="ltr">'+(s.analysis?.decision?.action!=='Hold' && s.analysis ? num(s.analysis.estimatedRisk):'—')+'</strong></div></div>'+
    '<div class="guard-note">'+icon('info')+'ریسک بر پایه‌ی فاصله‌ی واقعی ورود تا استاپ است؛ گپ، لغزش و کارمزد می‌تواند آن را تغییر دهد.</div></section>';
}
function positionsPanel(s, full=false) {
  const list=s.positions ?? [];
  const body=list.length ? '<div class="table-scroll"><table><thead><tr><th>نماد / تیکت</th><th>جهت</th><th>حجم</th><th>ورود</th><th>حد ضرر</th><th>سود و زیان</th><th></th></tr></thead><tbody>'+
    list.map(p=>'<tr><td><strong class="ltr">'+e(p.symbol)+'</strong><small class="table-sub ltr">#'+p.ticket+'</small></td><td>'+badge(actionLabel(p.side),p.side==='Buy'?'success':'danger')+'</td><td class="ltr">'+precise(p.lots)+'</td><td class="ltr">'+num(p.entryPrice,p.priceDigits??8)+'</td><td class="ltr">'+(p.stopLoss ? num(p.stopLoss,p.priceDigits??8):'بدون استاپ')+'</td><td class="ltr '+(p.profit>=0?'text-green':'text-red')+'">'+precise(p.profit)+'</td><td><button class="icon-button" data-action="position" data-ticket="'+p.ticket+'" aria-label="جزئیات معامله '+p.ticket+'">'+icon('info')+'</button></td></tr>').join('')+'</tbody></table></div>' :
    empty('هنوز معامله‌ی بازی نیست',s.broker?.terminalConnected ? 'ربات فقط معاملات متعلق به Magic Number خودش را پایش می‌کند.' : 'پس از اتصال، معاملات موجود هم از بروکر بازیابی می‌شوند.');
  return '<section class="glass positions-panel">'+sectionHead('معاملات باز',s.account ? 'حساب '+e(s.account.accountId)+' / '+e(s.account.brokerName) : 'بازیابی خودکار از بروکر',full?badge(list.length+' معامله'): '<a class="text-link" href="#positions">همه‌ی معاملات '+icon('arrow')+'</a>')+body+'</section>';
}
export function journalRows(s, ui, short=false) {
  let rows=ui.journal ?? s.journal ?? [];
  if (ui.level && ui.level!=='all') rows=rows.filter(x=>x.level===ui.level);
  if (ui.query) rows=rows.filter(x=>(x.message+' '+x.symbol+' '+x.eventType).toLowerCase().includes(ui.query.toLowerCase()));
  if (short) rows=rows.slice(0,4);
  if (!rows.length) return empty('رویدادی برای نمایش نیست','رویدادها و دلیل تصمیم‌ها، به‌صورت پایدار ثبت می‌شوند.','activity');
  return '<div class="journal-list">'+rows.map(r=>'<article class="journal-row"><span class="journal-icon '+(r.level==='Error'?'error':r.level==='Warning'?'warning':'')+'">'+icon(r.eventType.includes('Trade')?'wallet':r.level==='Error'?'info':'activity')+'</span><div><div class="journal-meta"><strong>'+e(({RuntimeStarted:'شروع سیستم',RuntimeError:'وضعیت ارتباط',Analysis:'تحلیل بازار',TradeOpened:'معامله تأیید شد',TradeClosed:'معامله بسته شد',TradeRecovered:'بازیابی سفارش',RuntimePaused:'توقف ورود',RuntimeResumed:'فعال‌سازی ورود',SettingsChanged:'تغییر تنظیمات',TelegramDeliveryFailed:'اعلان تلگرام',ClosureUnconfirmed:'بسته‌شدن تأیید نشده',HistorySyncFailed:'بازیابی سابقه'})[r.eventType]??r.eventType)+'</strong>'+ (r.symbol?badge(r.symbol):'')+'</div><p>'+e(reasonLabel(r.message))+'</p>'+(short?'':tradeJournalDetail(r)+(r.detail?'<details><summary>جزئیات فنی</summary><pre>'+e(r.detail)+'</pre></details>':''))+'</div><time>'+e(eventDate(r.timestamp))+'</time></article>').join('')+'</div>';
}
function journalPanel(s,ui,short=false) {
  return '<section class="glass journal-panel">'+sectionHead('جریان رویدادها','هر تصمیم، با دلیل قابل بررسی',short?'<a class="text-link" href="#journal">مشاهده‌ی ژورنال '+icon('arrow')+'</a>':
    '<button class="button secondary compact" data-action="export">'+icon('download')+'خروجی JSON</button>')+
    (short?'':'<div class="journal-filters"><label class="filter-search">'+icon('search')+'<input id="journal-search" type="search" placeholder="جست‌وجوی نماد، رویداد یا پیام…" value="'+e(ui.query??'')+'"></label><select id="journal-level" aria-label="سطح رویداد"><option value="all">همه‌ی رویدادها</option><option value="Information">اطلاعات</option><option value="Warning">هشدار</option><option value="Error">خطا</option></select></div>')+
    '<div id="journal-rows">'+journalRows(s,ui,short)+'</div>'+(short?'':'<div class="history-footer"><small>زمان ثبت رویداد: منطقهٔ زمانی دستگاه. خروجی شامل ردیف‌های بارگذاری‌شده است.</small>'+(ui.journalNextBefore?'<button class="button secondary compact" data-action="journal-more" '+(ui.journalLoading?'disabled':'')+'>رویدادهای قدیمی‌تر</button>':'')+'</div>')+'</section>';
}
function enginePanel(s) {
  const a=s.analysis;
  const p=s.settings?.strategy??{},phase=timeframeLabel(p.phaseTimeframeMinutes??60),pattern=timeframeLabel(p.patternTimeframeMinutes??15),entry=timeframeLabel(p.entryTimeframeMinutes??5);
  if(!a) return '<section class="glass engine-panel">'+sectionHead('دیدبان تحلیل','ساختار چندتایم‌فریمی')+empty('منتظر اولین تحلیل','داده‌ی '+phase+'، '+pattern+' و '+entry+' پس از آماده‌شدن متاتریدر دریافت می‌شود.','chart')+'</section>';
  return '<section class="glass engine-panel">'+sectionHead('دیدبان تحلیل','موتور فعلی: '+(a.engine==='Indicator'?'اندیکاتوری':'پرایس‌اکشن'),badge(actionLabel(a.action),a.action==='Hold'?'neutral':a.action==='Buy'?'success':'danger'))+
    '<div class="analysis-timeframes">'+[[phase,'فاز بازار',a.phase],[pattern,'الگوی بازار',a.pattern],[entry,'تریگر ورود',a.entry]].map(([tf,label,c])=>'<article><div><strong>'+tf+'</strong><span>'+label+'</span></div><dl><dt>قیمت بسته‌شدن</dt><dd class="ltr">'+num(c.close,a.specification.digits)+'</dd><dt>EMA '+e(p.slowEmaPeriod??30)+'</dt><dd class="ltr">'+num(c.emaSlow,a.specification.digits)+'</dd><dt>ADX</dt><dd class="ltr">'+num(c.adx)+'</dd><dt>ATR</dt><dd class="ltr">'+num(c.atr,a.specification.digits)+'</dd></dl></article>').join('')+'</div>'+
    '<div class="decision-note">'+icon('info')+'<div><strong>دلیل اقدام فعلی</strong><p>'+e(reasonLabel(a.reason))+'</p><small>'+e(a.decision.note)+'</small></div></div>'+
    '<div class="analysis-summary"><span>اسپرد <strong class="ltr">'+num(a.spreadPoints,1)+' pt</strong></span><span>ناحیه‌های معتبر <strong>'+a.zones.length+'</strong></span><span>کندل‌های تاریخچه <strong>'+a.history.length+'</strong></span><span>آخرین تحلیل <strong>'+formatTime(a.timestamp)+'</strong></span></div></section>';
}
function connections(s) {
  const nodes=[['cloud','بک‌اند تحلیل',true,'مستقل از سیستم‌عامل','API · Runtime · SQLite'],['signal','Agent محلی',s.broker?.agentConnected,'ارتباط احرازهویت‌شده','Outbound WebSocket'],['terminal','ترمینال متاتریدر',s.broker?.terminalConnected,'ارتباط محلی غیرمسدودکننده','Loopback TCP · MT4']];
  return '<div class="connection-grid">'+nodes.map(([name,title,ok,note,tech])=>'<article class="glass connection-node"><span class="node-art">'+icon(name)+'</span>'+badge(ok?'متصل':'منتظر اتصال',ok?'success':'neutral')+'<h2>'+title+'</h2><p>'+note+'</p><small class="ltr">'+tech+'</small></article>').join('')+'</div>'+
    '<section class="glass health-panel">'+sectionHead('سلامت و محافظت','بدون وابستگی به انتخاب نماد روی نمودار')+
    '<div class="health-grid">'+[
      ['اجازه‌ی اجرای EA',s.ordersEnabledAtTerminal?'فعال':'خاموش',s.ordersEnabledAtTerminal],
      ['تریلینگ مستقل در EA',s.trailingEnabledAtTerminal?'فعال':'تأیید نشده',s.trailingEnabledAtTerminal],
      ['حساب متصل',s.account?(s.account.isDemo?'دمو':'واقعی'):'دریافت نشده',!!s.account],
      ['تلگرام',!s.telegramConfigured?'پیکربندی نشده':!s.telegramDeliveryEnabled?'ارسال غیرفعال':s.telegramError?'خطای ارسال':'آمادهٔ ارسال',s.telegramConfigured&&s.telegramDeliveryEnabled&&!s.telegramError],
      ['نتایج سفارش نامشخص',String(s.unresolvedIntents?.length ?? 0),!(s.unresolvedIntents?.length)],
      ['مهلت آخرین سیکل',s.lastCycleDurationMs != null ? num(s.lastCycleDurationMs,0)+' ms':'—',s.lastCycleDurationMs!=null],
      ['تأخیر پاسخ Agent',s.broker?.latencyMs != null ? num(s.broker.latencyMs,0)+' ms':'—',s.broker?.latencyMs!=null],
      ['اعلان‌های منتظر ارسال',String(s.pendingNotifications??0),!(s.pendingNotifications)]
    ].map(([label,value,ok])=>'<div><span>'+label+'</span>'+badge(value,ok?'success':'neutral')+'</div>').join('')+'</div>'+
    (s.lastError?'<div class="warning-strip">'+icon('info')+e(reasonLabel(s.lastError))+'</div>':'')+
    (s.telegramError?'<div class="warning-strip">'+icon('info')+'تلگرام: '+e(s.telegramError)+'</div>':'')+'</section>'+
    '<section class="glass setup-panel">'+sectionHead('مسیر راه‌اندازی','ابتدا روی لپ‌تاپ، سپس بک‌اند روی لینوکس')+'<ol class="setup-steps"><li><strong>اجرای API و Agent</strong><span>روی Start-Trader.cmd در پوشهٔ پروژه دوبار کلیک کنید؛ برنامه و پل به‌ترتیب آماده می‌شوند.</span></li><li><strong>نصب TraderBridgeEA</strong><span>EA جدید را در MetaEditor کامپایل کنید؛ DLL imports برای Winsock لازم است.</span></li><li><strong>تست روی حساب دمو</strong><span>EnableOrders را در EA فعال کنید و اتصال‌ها را در همین صفحه بررسی کنید.</span></li><li><strong>فعال‌سازی آگاهانه‌ی ورود</strong><span>پایش از ابتدا فعال است؛ ارسال سفارش فقط با فعال‌سازی اپراتور انجام می‌شود.</span></li></ol></section>';
}
function settingsView(s) {
  const x=s.settings??{}, disabled=s.entriesEnabled?'disabled':'';
  const input=(key,label,help,type='number',attributes='')=>'<label class="setting-field"><span>'+label+'</span><input name="'+key+'" type="'+type+'" value="'+e(x[key]??'')+'" '+attributes+' '+disabled+'><small>'+help+'</small></label>';
  return '<form id="settings-form"><section class="glass settings-panel">'+sectionHead('قوانین اجرا','تنظیمات پس از ذخیره، به‌صورت پایدار نگهداری می‌شود',badge(s.entriesEnabled?'ابتدا ورود را متوقف کنید':'قابل ویرایش',s.entriesEnabled?'danger':'success'))+
    '<div class="settings-grid">'+input('symbol','نماد معاملاتی','خالی = نماد نمودار EA. نام دقیق بروکر، شامل پسوند.','text','maxlength="64" placeholder="EURUSD"')+
    input('dailyTradeLimit','سقف معاملات روزانه','فقط معاملات تأییدشده؛ روز تقویمی UTC. در حالت پایش صفر و قفل است.','number',x.observationOnly?'min="0" max="0" readonly required':'min="1" max="100" required')+
    input('riskPercent','حداکثر ریسک نسبی (%)','حجم بر اساس فاصله‌ی واقعی تا حد ضرر محاسبه می‌شود.','number',x.observationOnly?'min="0" max="0" readonly required':'min="0.01" max="5" step="0.01" required')+
    input('maximumRiskAmount','سقف ریسک به واحد پول حساب','محدودیت کوچک‌ترِ مبلغ و درصد اعمال می‌شود. برای خروج از پایش از ولوم معاملات استفاده کن.','number',x.observationOnly?'min="0" max="0" readonly required':'min="0.01" max="10000" step="0.01" required')+
    input('maximumSpreadPoints','سقف اسپرد (Point)','Point با Pip متفاوت است؛ مشخصات از بروکر گرفته می‌شود.','number','min="1" max="10000" step="1" required')+
    input('dailyDrawdownLimitPercent','حد افت روزانه (%)','افت از بالاترین Equity مشاهده‌شده روز UTC، شامل سود/زیان شناور.','number','min="0.01" max="99.99" step="0.01" required')+
    input('totalDrawdownLimitPercent','حد افت کل (%)','توقف ورود تا ریست دستی؛ واریز و برداشت خودکار تعدیل نمی‌شوند.','number','min="0.01" max="99.99" step="0.01" required')+
    input('drawdownCooldownHours','توقف پس از افت روزانه / ساعت','مرجع و دوره توقف در ژورنال پایدار می‌مانند.','number','min="1" max="720" step="1" required')+
    input('analysisIntervalSeconds','فاصله‌ی تحلیل (ثانیه)','سیکل‌ها هم‌پوشانی ندارند؛ داده‌ی کندل بسته‌شده استفاده می‌شود.','number','min="10" max="3600" required')+
    input('positionIntervalSeconds','فاصله‌ی پایش معاملات (ثانیه)','تریلینگ در EA مستقل از این فاصله اجرا می‌شود.','number','min="2" max="60" required')+'</div>'+
    '<div class="switch-settings"><label class="switch-row"><div><strong>اعلان‌های تلگرام</strong><small>ورود، خروج، بازیابی و خطاهای اجرایی در صف پایدار</small></div><input type="checkbox" name="telegramEnabled" '+(x.telegramEnabled?'checked':'')+' '+disabled+'></label>'+
    '<label class="switch-row danger-setting"><div><strong>اجازه‌ی حساب واقعی در بک‌اند</strong><small>EA نیز باید جداگانه AllowLiveAccount را تأیید کند.</small></div><input type="checkbox" name="allowLiveAccount" '+(x.allowLiveAccount?'checked':'')+' '+disabled+'></label></div>'+
    strategySettings(s)+'<div class="settings-footer"><p>'+icon('shield')+'محدودیت سخت EA و کنترل بروکر همچنان اعمال می‌شود.</p><button class="button primary" type="submit" '+disabled+'>'+icon('check')+'ذخیره‌ی تنظیمات</button></div></section></form>'+
    '<section class="glass settings-panel">'+sectionHead('حفاظت سرمایه',s.risk?.halted?'توقف کل سرمایه فعال است':s.risk?.blockedUntil&&Date.parse(s.risk.blockedUntil)>Date.now()?'دوره توقف روزانه فعال است':'مرجع حساب پس از اتصال تازه ثبت می‌شود')+
    '<p class="muted">ریست فقط مرجع بک‌اند را عوض می‌کند؛ ورود خودکار روشن نمی‌شود. حفاظت مستقل EA و مرجع آن باید جداگانه بررسی شوند.</p>'+
    '<button type="button" class="button secondary" data-action="reset-risk" '+(s.entriesEnabled||!s.account?'disabled':'')+'>'+icon('shield')+'ریست دستی مرجع بک‌اند</button></section>'+
    '<div class="glass font-note">'+icon('check')+'<p>فونت مدام از فایل‌های محلی پروژه در هشت وزن بارگذاری می‌شود.</p></div>';
}
function unresolved(s) {
  if(!s.unresolvedIntents?.length) return '';
  return '<div class="warning-strip uncertainty">'+icon('shield')+'<div><strong>'+s.unresolvedIntents.length+' نتیجه‌ی سفارش نامشخص</strong><p>سفارش مجدداً ارسال نمی‌شود. تطبیق خودکار ادامه دارد؛ نبودن در تاریخچه، اثبات شکست سفارش نیست.</p></div><button class="button secondary compact" data-action="refresh">تطبیق مجدد</button></div>';
}
export function renderView(view,s,ui) {
  const top=heading(views[view] ?? views.overview,({
    overview:'یک فضای روشن برای دیدن، تصمیم‌گرفتن و کنترل‌کردن.',
    market:'از ساختار بازار تا دلیل تصمیم؛ بدون پنهان‌کردن جزئیات.',
    research:'پژوهش، بک‌تست و آزمون خارج از نمونه؛ جدا از اجرای حساب.',
    positions:'پایش معاملات ربات، مستقل از ری‌استارت و تغییر نمودار.',
    journal:'تاریخچه‌ی تصمیم‌ها، اجراها و خطاها؛ قابل جست‌وجو.',
    connections:'سلامت تمام مسیر، از بک‌اند تا بروکر.',
    settings:'کنترل رفتار ربات، با مرزهای مشخص و قابل توسعه.'
  })[view],refreshButton);
  switch(view) {
    case 'market': return top+unresolved(s)+chartPanel(s,ui)+enginePanel(s);
    case 'positions': return '<div data-live-section="heading">'+top+unresolved(s)+'</div><div data-live-section="account">'+accountCards(s)+'</div>'+renderRiskProfiles(s,ui.risk)+
      '<div data-live-section="chart">'+chartPanel(s,ui)+'</div><div data-live-section="open">'+positionsPanel(s,true)+'</div><div data-live-section="history">'+renderTradeHistory(s,ui)+'</div>';
    case 'journal': return top+journalPanel(s,ui);
    case 'connections': return top+unresolved(s)+connections(s);
    case 'settings': return top+settingsView(s);
    case 'research': return top+renderResearch(s,ui);
    default: return top+unresolved(s)+hero(s,ui)+accountCards(s)+'<div class="overview-columns">'+chartPanel(s,ui)+riskPanel(s)+'</div>'+positionsPanel(s)+'<div class="overview-bottom">'+journalPanel(s,ui,true)+'<section class="glass analysis-mini">'+sectionHead('آخرین تصمیم','موتور قابل تعویض / MTF',icon('chart'))+'<span class="decision-action">'+actionLabel(s.analysis?.action)+'</span><p>'+e(reasonLabel(s.analysis?.reason))+'</p><div class="analysis-mini-footer"><span>'+(s.analysis?.engine==='Indicator'?'Indicator Engine':s.analysis?'Price Action Engine':'AWAITING DATA')+'</span><a class="text-link" href="#market">جزئیات '+icon('arrow')+'</a></div></section></div>';
  }
}
