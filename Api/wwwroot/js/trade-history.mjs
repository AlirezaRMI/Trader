import { escapeHtml as e, formatNumber as num, actionLabel } from './model.mjs?v=20261007-ui2';
import { icon } from './icons.mjs?v=20261007-ui2';

export const tradeKey = trade => JSON.stringify([trade.accountId, trade.brokerServer, trade.position.ticket]);
const badge = (text, kind='neutral') => '<span class="badge '+kind+'">'+e(text)+'</span>';
export function brokerDate(value) {
  if (!Number.isFinite(value) || value <= 0) return '—';
  // MQL timestamps encode the broker's wall clock. Do not apply the browser's UTC offset.
  return new Date(value*1000).toLocaleString('fa-IR', {timeZone:'UTC',year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit'});
}
export function eventDate(value) {
  const date=new Date(value);
  return value && Number.isFinite(date.getTime()) ? date.toLocaleString('fa-IR',
    {year:'numeric',month:'2-digit',day:'2-digit',hour:'2-digit',minute:'2-digit',second:'2-digit'}) : '—';
}
export const precise = value => typeof value==='number'&&Number.isFinite(value) ?
  new Intl.NumberFormat('en-US',{maximumFractionDigits:10}).format(value) : '—';
const accountType = value => value===true?'دمو':value===false?'واقعی':'نوع ثبت نشده';

export function renderTradeHistory(s, ui) {
  const h=ui.trades??{}, accounts=h.accounts??[], query=(h.query??'').trim().toLowerCase();
  const items=(h.items??[]).filter(t=>!query||[t.accountId,t.brokerServer,t.position.ticket,t.position.symbol,t.position.comment]
    .join(' ').toLowerCase().includes(query));
  const summary=accounts.length ? '<div class="history-accounts">'+accounts.map(a=>'<article><div><strong>حساب '+e(a.accountId)+'</strong>'+badge(accountType(a.isDemo),a.isDemo===false?'danger':'neutral')+'</div><small>'+e(a.brokerServer)+'</small><b class="ltr '+(a.netProfit>=0?'text-green':'text-red')+'">'+num(a.netProfit)+' '+e(a.currency??'ارز ثبت نشده')+'</b><p>'+a.trades+' معامله · '+a.wins+' مثبت · '+a.losses+' منفی · '+(a.trades-a.wins-a.losses)+' سربه‌سر</p></article>').join('')+'</div>' : '';
  const note=s.historySync?.error ? '<div class="warning-strip">'+icon('info')+e(s.historySync.error)+'</div>' :
    s.historySync?.inProgress ? '<div class="history-note">در حال بازیابی سابقهٔ MT4؛ '+s.historySync.remainingBrokerRows+' ردیف از تاریخچهٔ بارگذاری‌شده باقی مانده است.</div>' : '';
  const rows=items.length?'<div class="table-scroll"><table><thead><tr><th>معامله / حساب</th><th>جهت / حجم</th><th>ورود</th><th>خروج</th><th>بسته‌شدن</th><th>نتیجهٔ خالص</th><th></th></tr></thead><tbody>'+items.map(t=>{
    const p=t.position,d=p.priceDigits??8;
    return '<tr><td><strong class="ltr">'+e(p.symbol)+' #'+p.ticket+'</strong><small class="table-sub">حساب '+e(t.accountId)+' · '+e(t.brokerServer)+'</small></td><td>'+badge(actionLabel(p.side),p.side==='Buy'?'success':'danger')+'<small class="table-sub ltr">'+precise(p.lots)+' lot</small></td><td class="ltr">'+num(p.entryPrice,d)+'</td><td class="ltr">'+num(p.closePrice,d)+'</td><td>'+brokerDate(p.closeTime)+'</td><td class="ltr '+(p.profit>=0?'text-green':'text-red')+'">'+precise(p.profit)+'<small class="table-sub">'+e(t.context?.currency??'ارز ثبت نشده')+'</small></td><td><button class="icon-button" data-action="history-detail" data-trade-key="'+e(tradeKey(t))+'" aria-label="جزئیات معامله بسته‌شده '+p.ticket+'">'+icon('info')+'</button></td></tr>';
  }).join('')+'</tbody></table></div>' : '<div class="empty-state"><span class="empty-icon">'+icon('wallet')+'</span><h3>'+(h.loading?'در حال خواندن سابقه…':h.error?'خواندن سابقه ناموفق بود':query?'در ردیف‌های بارگذاری‌شده پیدا نشد':'هنوز معاملهٔ بسته‌شده‌ای ثبت نشده')+'</h3><p>بسته‌شدن فقط با تأیید تاریخچهٔ بروکر ثبت می‌شود؛ رکوردها با خاموش‌شدن برنامه حذف نمی‌شوند.</p></div>';
  return '<section class="glass positions-panel history-panel"><div class="section-heading"><div><h2>سابقهٔ معاملات بسته‌شده</h2><p>کارنامهٔ ثبت‌شدهٔ ربات؛ حساب‌ها و ارزها جدا هستند.</p></div><div class="heading-actions"><button class="button secondary compact" data-action="history-refresh" '+(h.loading?'disabled':'')+'>'+icon('refresh')+'به‌روزرسانی</button><button class="button secondary compact" data-action="history-export" '+(h.exporting?'disabled':'')+'>'+icon('download')+(h.exporting?'در حال خروجی…':'خروجی کامل JSON')+'</button></div></div>'+summary+note+
    (h.error?'<div class="warning-strip">'+e(h.error)+'</div>':'')+'<label class="filter-search history-search">'+icon('search')+'<input id="trade-search" type="search" placeholder="جست‌وجو در سابقهٔ بارگذاری‌شده: نماد، تیکت، حساب…" value="'+e(h.query??'')+'"></label>'+rows+
    '<div class="history-footer"><small>ورود/خروج: ساعت سرور بروکر؛ آمار فقط معاملات ثبت‌شده، نه کل عملکرد تضمین‌شده.</small>'+
    (h.nextCursor?'<button class="button secondary compact" data-action="history-more" '+(h.loading?'disabled':'')+'>نمایش قدیمی‌تر</button>':'')+'</div></section>';
}

export function renderPositionDetail(position, context=null, account=null) {
  const p=position, closed=p.closeTime>0, currency=context?.currency??account?.currency??'ارز ثبت نشده';
  const duration=closed&&p.closeTime>=p.openTime ? p.closeTime-p.openTime : null;
  const fields=[['وضعیت',closed?'بسته‌شده':'باز'],['حساب',context?.accountId??account?.accountId??'—'],
    ['سرور بروکر',context?.brokerServer??account?.server??'—'],['نوع حساب',accountType(context?.isDemo??account?.isDemo)],
    ['حجم (لات)',precise(p.lots)],['قیمت واقعی ورود',precise(p.entryPrice)],
    ...(closed?[['قیمت واقعی خروج',precise(p.closePrice)]]:[]),
    ['آخرین حد ضرر گزارش‌شده',p.stopLoss?precise(p.stopLoss):'ثبت نشده'],['آخرین حد سود گزارش‌شده',p.takeProfit?precise(p.takeProfit):'ثبت نشده'],
    ['زمان ورود (سرور بروکر)',brokerDate(p.openTime)],...(closed?[['زمان خروج (سرور بروکر)',brokerDate(p.closeTime)],
      ['مدت طبق ساعت سرور',duration===null?'—':Math.floor(duration/3600)+' ساعت و '+Math.floor(duration%3600/60)+' دقیقه']]:[]),
    ['سود/زیان بدون هزینه',precise(p.grossProfit)+' '+currency],['کمیسیون گزارش بروکر',precise(p.commission)+' '+currency],
    ['سواپ گزارش بروکر',precise(p.swap)+' '+currency],['سود/زیان خالص '+(closed?'نهایی':'شناور'),precise(p.profit)+' '+currency],['توضیح بروکر',p.comment||'—']];
  const decision=context?.decision;
  const entry=decision?'<section class="trade-entry-context"><h3>تصمیم ثبت‌شده هنگام ارسال</h3><dl>'+[
    ['شناسهٔ درخواست',context.intentId??'—'],['زمان ارسال (دستگاه)',eventDate(context.requestedAt)],
    ['زمان کندل سیگنال (بروکر)',brokerDate(context.signalCandleTime)],['علت سیگنال',decision.note??'—'],
    ['قیمت مرجع سفارش',precise(decision.entryPrice)],['استاپ درخواستی',precise(decision.stopLossPrice)],
    ['هدف درخواستی',precise(decision.takeProfitPrice)],['ریسک نسبی تنظیم‌شده',precise(context.settings?.riskPercent)+'%'],
    ['سقف مبلغ ریسک تنظیم‌شده',precise(context.settings?.maximumRiskAmount)+' '+currency]
  ].map(([label,value])=>'<dt>'+e(label)+'</dt><dd>'+e(value)+'</dd>').join('')+'</dl><details><summary>پارامترهای استراتژی هنگام ارسال</summary><pre>'+e(JSON.stringify(context.settings?.strategy??null,null,2))+'</pre></details></section>':'<p class="history-note">'+(closed?'علت و پارامترهای ورود برای این رکورد قدیمی ذخیره نشده‌اند؛ از نتیجهٔ معامله حدس زده نمی‌شوند.':'این نمایش، وضعیت بروکر است؛ تصمیم‌های ثبت‌شدهٔ ارسال را در ژورنال ببین.')+'</p>';
  return '<p class="eyebrow">'+(closed?'CLOSED TRADE':'OPEN POSITION')+' / #'+p.ticket+'</p><h2 class="ltr">'+e(p.symbol)+'</h2><div class="decision-note">'+icon('shield')+'<div><strong>'+actionLabel(p.side)+' · '+(closed?'بسته‌شده':'باز')+'</strong><p>اطلاعات تأییدشدهٔ بروکر؛ «—» یعنی داده ذخیره نشده است. زمان‌های معامله بدون تبدیل منطقهٔ زمانی سرور نمایش داده می‌شوند.</p></div></div><dl class="trade-detail-grid">'+fields.map(([label,value])=>'<dt>'+e(label)+'</dt><dd>'+e(value)+'</dd>').join('')+'</dl>'+entry;
}

export function tradeJournalDetail(row) {
  if (!row.detail || !['TradeOpened','TradeClosed','TradeRecovered'].includes(row.eventType)) return '';
  let data;try{data=JSON.parse(row.detail);}catch{return '';}
  const p=data.position??(data.ticket&&data.entryPrice!=null?data:null);
  const ticket=data.ticket??p?.ticket;
  if(!ticket)return '';
  return '<div class="journal-trade-summary">'+[['تیکت',ticket],['حساب',data.accountId],['سرور',data.brokerServer],
    ['ورود',p?.entryPrice],['خروج',p?.closePrice],['حجم',p?.lots],['خالص',p?.profit],['ارز',data.currency],
    ['علت ورود',data.decision?.note]].filter(([,value])=>value!=null).map(([label,value])=>'<span>'+e(label)+': <strong>'+e(value)+'</strong></span>').join('')+'</div>';
}
