import { escapeHtml as e, formatNumber as n, canStart } from './model.mjs?v=20261007-ui2';
import { icon } from './icons.mjs?v=20261007-ui2';

export const createRiskState = () => ({ profiles:[], draftId:null, baseline:null, saving:false, operationPending:false, loading:false, error:null, stale:false });
export const riskPending = state => !!(state?.draftId || state?.saving);
export function settingsKey(value) {
  const ordered = item => item && typeof item==='object' ? (Array.isArray(item) ? item.map(ordered) :
    Object.fromEntries(Object.keys(item).sort().map(key=>[key,ordered(item[key])]))) : item;
  return JSON.stringify(ordered(value));
}
export function discardRiskDraft(state) { state.draftId=null;state.baseline=null;state.stale=false;state.error=null; }
function matches(profile,settings) {
  const p=settings.strategy??{};
  return ['riskPercent','maximumRiskAmount','dailyTradeLimit','observationOnly'].every(key=>settings[key]===profile[key]) &&
    ['minimumEntryAdx','autoSwitchAdx','weakTrendAdx','momentumAdx','minimumZoneStrength','stopAtrMultiplier'].every(key=>p[key]===profile[key]) &&
    p.strongTrendAdx===profile.minimumEntryAdx && p.mode==='Auto' && p.requireMacroTrend && p.requireBreakoutRetest;
}
export function selectedRisk(snapshot,state) {
  const settings=snapshot.settings??{},profiles=state?.profiles??[];
  const draft=profiles.find(p=>p.id===state?.draftId);
  const saved=profiles.find(p=>p.id===settings.riskProfileId && matches(p,settings));
  return { profile:draft??saved, preview:!!draft, settings, parameters:settings.strategy??{} };
}
export function riskChartNote(snapshot,state) {
  const {profile,preview,settings,parameters:p}=selectedRisk(snapshot,state);
  const observe=profile?.observationOnly??settings.observationOnly;
  const percent=profile?.riskPercent??settings.riskPercent,amount=profile?.maximumRiskAmount??settings.maximumRiskAmount;
  const label=profile?.label??(observe?'پایش؛ تنظیمات سفارشی':'تنظیمات سفارشی');
  return '<aside class="risk-chart-note" data-risk-chart-note><div><span class="badge '+(preview?'purple':'neutral')+'">'+(preview?'پیش‌نمایش؛ ذخیره نشده':'تنظیمات ثبت‌شده')+'</span><strong>'+e(label)+'</strong></div><p>'+e(profile?.frequencyHint??'تعداد ورود به سیگنال معتبر، سقف روزانه و محدودیت بروکر وابسته است.')+'</p><small>'+e(profile?.exposureHint??'ریسک کمتر یا بیشتر به معنی سود تضمین‌شده نیست.')+'</small><div class="risk-chart-values">'+
    (observe?'ورود جدید مسدود؛ محافظت معاملات باز باقی است.':
      'ریسک ≤ '+n(percent,2)+'% و '+n(amount,2)+' '+e(snapshot.account?.currency??'واحد حساب')+' · ADX ≥ '+n(profile?.minimumEntryAdx??p.minimumEntryAdx,1)+' · استاپ ساختاری / ATR')+'</div></aside>';
}
export function riskProfileDetails(snapshot,state) {
  const {profile,preview,settings,parameters:p}=selectedRisk(snapshot,state);
  const observe=profile?.observationOnly??settings.observationOnly;
  const get=key=>profile?.[key]??settings[key],strategy=key=>profile?.[key]??p[key];
  const currency=snapshot.account?.currency??'واحد پول حساب';
  const technical=[['حداکثر ریسک هر ورود',n(get('riskPercent'),2)+'%'],['سقف مبلغ ریسک',n(get('maximumRiskAmount'),2)+' '+currency],
    ['سقف ورود روزانه',String(get('dailyTradeLimit')??'—')],['حداقل ADX ورود',n(strategy('minimumEntryAdx'),1)],
    ['مرز انتخاب Auto / ADX',n(strategy('autoSwitchAdx'),1)],['حداقل قدرت ناحیه',String(strategy('minimumZoneStrength')??'—')],
    ['حداقل فاصله استاپ / ATR',n(strategy('stopAtrMultiplier'),1)+'×'],['تأیید روند کلان / ری‌تست',profile?'هر دو الزامی':(p.requireMacroTrend?'کلان روشن':'کلان خاموش')+' / '+(p.requireBreakoutRetest?'ری‌تست روشن':'ری‌تست خاموش')],
    ['انتخاب استراتژی',profile?'Auto':p.mode??'—'],['حداقل سود / ریسک خالص',n(p.rewardRiskRatio,1)+'×'],
    ['حفاظت افت روزانه / کل',n(settings.dailyDrawdownLimitPercent,1)+'% / '+n(settings.totalDrawdownLimitPercent,1)+'%'],['تریلینگ کندل‌بسته',p.trailingEnabled?'فعال؛ تنظیمات فعلی حفظ می‌شود':'خاموش؛ تنظیمات فعلی حفظ می‌شود']];
  const locked=snapshot.entriesEnabled||state?.saving||state?.operationPending,stale=state?.stale;
  return '<div class="risk-profile-intro"><div><p class="eyebrow">'+(preview?'PREVIEW / NOT SAVED':'CURRENT / SAVED')+'</p><h3>'+e(profile?.label??(observe?'پایش؛ سفارشی':'تنظیمات سفارشی'))+'</h3></div><span class="badge '+(observe?'neutral':'purple')+'">'+(observe?'بدون ورود جدید':preview?'هنوز اعمال نشده':'حالت فعلی')+'</span></div>'+
    '<p class="risk-description">'+e(profile?.description??'مقادیر فعلی با هیچ‌یک از حالت‌های آماده یکسان نیستند. انتخاب ولوم فقط پیش‌نمایش است؛ برای ذخیره، اعمال را بزن.')+'</p>'+
    '<div class="risk-expectations"><p>'+icon('activity')+e(profile?.frequencyHint??'تعداد معاملات وابسته به شروط فعلی و داده بازار است.')+'</p><p>'+icon('shield')+e(profile?.exposureHint??'ریسک در تنظیمات پیشرفته تعیین شده است؛ سود یا تعداد معامله تضمین نمی‌شود.')+'</p></div>'+
    '<div class="risk-technical">'+technical.map(([label,value])=>'<div><span>'+e(label)+'</span><strong>'+e(value)+'</strong></div>').join('')+'</div>'+
    '<p class="risk-common-guards">'+icon('info')+'سقف حجم EA، اسپرد، افت سرمایه و تریلینگ فعلی تغییر نمی‌کنند. اگر حداقل لات بروکر از بودجه بیشتر باشد، هیچ سفارشی باز نمی‌شود. حالت پایش ریسک معاملات باز را حذف نمی‌کند.</p>'+
    (stale?'<p class="risk-status warning" role="alert">تنظیمات از زمان انتخاب تغییر کرده‌اند؛ پیش‌نمایش را لغو و دوباره انتخاب کن.</p>':'')+
    (state?.error?'<p class="risk-status warning" role="alert">'+e(state.error)+'</p>':'')+
    '<div class="risk-profile-footer"><small>'+(snapshot.entriesEnabled?'برای تغییر حالت، ابتدا ورود جدید را متوقف کن.':preview?'ذخیرهٔ این انتخاب، ورود خودکار را فعال نمی‌کند.':'این تنظیمات فقط برای ورودهای بعدی‌اند؛ استاپ معاملهٔ باز جابه‌جا نمی‌شود.')+'</small><div class="heading-actions">'+
    (preview?'<button class="button secondary compact" data-action="risk-cancel" '+(state?.saving?'disabled':'')+'>لغو پیش‌نمایش</button>':'')+
    '<button class="button primary compact" data-action="risk-apply" '+(!preview||locked||stale?'disabled':'')+'>'+icon('check')+(state?.saving?'در حال ذخیره…':'اعمال حالت')+'</button>'+
    (snapshot.entriesEnabled?'<button class="button secondary compact" data-action="pause">'+icon('pause')+'توقف ورود جدید</button>':
      '<button class="button secondary compact" data-action="start" '+(preview||locked||!canStart(snapshot)?'disabled':'')+'>'+icon('play')+'فعال‌سازی ورود خودکار</button>')+'</div></div>';
}
export function renderRiskProfiles(snapshot,state) {
  const profiles=state?.profiles??[],{profile}=selectedRisk(snapshot,state);
  const index=Math.max(0,profiles.findIndex(p=>p.id===profile?.id)),locked=snapshot.entriesEnabled||state?.saving||state?.operationPending||!profiles.length;
  return '<section class="glass risk-profile-card" id="risk-profile-card"><div class="section-heading"><div><h2>ولوم ریسک معاملات</h2><p>پنج حالت؛ پیش‌نمایش، بررسی شروط، سپس اعمال آگاهانه</p></div><span class="badge '+(snapshot.entriesEnabled?'danger':'success')+'" id="risk-lock-label">'+(snapshot.entriesEnabled?'قفل؛ ورود فعال است':'قابل تنظیم؛ ورود متوقف')+'</span></div>'+
    (!profiles.length?'<p class="risk-status">'+e(state?.error??'در حال دریافت حالت‌های ریسک…')+'</p><button class="button secondary compact" data-action="risk-reload" '+(state?.loading?'disabled':'')+'>دریافت مجدد حالت‌ها</button>':
      '<label class="risk-volume-label" for="risk-volume">از پایش تا مواجههٔ بیشتر؛ هیچ حالت معاملاتی بدون ریسک نیست.</label><div class="risk-volume-track"><input id="risk-volume" type="range" min="0" max="'+(profiles.length-1)+'" step="1" value="'+index+'" aria-label="انتخاب حالت ریسک" aria-describedby="risk-profile-details" aria-valuetext="'+e(profile?.label??'سفارشی؛ برای انتخاب ولوم را حرکت بده')+'" '+(locked?'disabled':'')+'></div><div class="risk-volume-stops">'+profiles.map((p,i)=>'<button data-action="risk-select" data-index="'+i+'" aria-pressed="'+(p.id===profile?.id)+'" '+(locked?'disabled':'')+'><i></i><span>'+e(p.label)+'</span></button>').join('')+'</div>')+
    '<div id="risk-profile-details" aria-live="polite">'+riskProfileDetails(snapshot,state)+'</div></section>';
}

// Update only the details while dragging: replacing the range would lose pointer capture and keyboard focus.
export function updateRiskControls(snapshot,state) {
  const profiles=state?.profiles??[],{profile}=selectedRisk(snapshot,state),index=Math.max(0,profiles.findIndex(p=>p.id===profile?.id));
  const locked=!!(snapshot.entriesEnabled||state?.saving||state?.operationPending||!profiles.length),range=document.querySelector('#risk-volume');
  if(range){range.disabled=locked;range.value=String(index);range.style.setProperty('--risk-progress',index/Math.max(1,profiles.length-1)*100+'%');range.setAttribute('aria-valuetext',profile?.label??'تنظیمات سفارشی');}
  document.querySelectorAll('[data-action="risk-select"]').forEach(button=>{button.disabled=locked;button.setAttribute('aria-pressed',String(profiles[Number(button.dataset.index)]?.id===profile?.id));});
  const label=document.querySelector('#risk-lock-label');if(label){label.textContent=snapshot.entriesEnabled?'قفل؛ ورود فعال است':'قابل تنظیم؛ ورود متوقف';label.className='badge '+(snapshot.entriesEnabled?'danger':'success');}
  const details=document.querySelector('#risk-profile-details');if(details){
    const template=document.createElement('template');template.innerHTML=riskProfileDetails(snapshot,state);
    if(details.innerHTML!==template.innerHTML)details.replaceChildren(template.content);
  }
  document.querySelectorAll('[data-risk-chart-note]').forEach(note=>{
    const template=document.createElement('template');template.innerHTML=riskChartNote(snapshot,state);
    if(note.outerHTML!==template.innerHTML)note.replaceWith(template.content);
  });
}
