// Keep the complete module graph on one release; old cached dependencies must not mix with new exports.
import { views, renderView, journalRows } from './views.mjs?v=20261007-ui2';
import { icon, hydrateIcons } from './icons.mjs?v=20261007-ui2';
import { escapeHtml as e, formatNumber, formatTime, actionLabel, reasonLabel, canStart } from './model.mjs?v=20261007-ui2';
import { LatestRequest, connectWithRetry, readJsonResponse } from './requests.mjs?v=20261007-ui2';
import { recordResearchRun } from './research-record.mjs?v=20261007-ui2';
import { tradeKey, renderPositionDetail } from './trade-history.mjs?v=20261007-ui2';
import { createRiskState, riskPending, settingsKey, discardRiskDraft, selectedRisk, updateRiskControls } from './risk-profiles.mjs?v=20261007-ui2';
let snapshot={settings:{},positions:[],journal:[],broker:{},unresolvedIntents:[]};
let view=views[location.hash.slice(1)]?location.hash.slice(1):'overview';
const ui={timeframe:5,chartHistory:[],chartLoading:false,query:'',level:'all',journal:null};
ui.research={request:null,result:null,optimization:null,completedRun:null,busy:false,error:null,source:''};
ui.risk=createRiskState();
const chartRequests=new LatestRequest();
const tradeRequests=new LatestRequest(),journalRequests=new LatestRequest();
ui.trades={items:[],accounts:[],nextCursor:null,loading:false,loaded:false,loadedAt:0,error:null,query:'',exporting:false};
let stream,retryTimer,confirmCallback,settingsDirty=false,settingsBaseline=null,busy=false,passwordRequired=false,sessionRevision=0;
let renderedView=null,riskCatalogMarkupKey=null;
const $=selector=>document.querySelector(selector);
hydrateIcons();
function toast(message,error=false){
  const item=document.createElement('div');item.className='toast'+(error?' error':'');item.textContent=reasonLabel(message);
  $('#toasts').append(item);while($('#toasts').children.length>3)$('#toasts').firstElementChild.remove();setTimeout(()=>item.remove(),6000);
}
async function api(path,method='GET',body){
  const revision=sessionRevision;
  const controller=new AbortController(),timer=setTimeout(()=>controller.abort(),path.startsWith('/api/research/')?35000:20000);
  try{
    const response=await fetch(path,{method,credentials:'same-origin',signal:controller.signal,
      ...(body===undefined?{}:{headers:{'Content-Type':'application/json'},body:JSON.stringify(body)})});
    if(response.status===401){showLogin();throw new Error('برای ادامه دوباره وارد شوید.');}
    const result=await readJsonResponse(response);
    if(!response.ok)throw new Error(result?.error??(response.status===429?'درخواست‌های زیادی ارسال شده؛ کمی صبر کنید.':'درخواست انجام نشد.'));
    if(revision!==sessionRevision)throw new Error('نشست تغییر کرده است؛ دوباره وارد شوید.');
    return result;
  }finally{clearTimeout(timer);}
}
function setFeed(live){$('#feed-status').classList.toggle('live',live);$('#feed-status').innerHTML='<i></i>'+(live?'دریافت زنده':'ارتباط پنل قطع است');}
function commitViewMarkup(html){
  const main=$('#main'),catalogKey=settingsKey([ui.risk.profiles,...(ui.risk.profiles.length?[]:[ui.risk.loading,ui.risk.error])]);
  if(view!=='positions'||renderedView!==view||!main.querySelector('#risk-profile-card'))main.innerHTML=html;
  else{
    const template=document.createElement('template');template.innerHTML=html;
    for(const incoming of template.content.children){
      const current=incoming.id==='risk-profile-card'?main.querySelector('#risk-profile-card'):
        main.querySelector('[data-live-section="'+incoming.dataset.liveSection+'"]');
      if(!current){main.append(incoming.cloneNode(true));continue;}
      if(incoming.id==='risk-profile-card'){if(catalogKey!==riskCatalogMarkupKey)current.replaceWith(incoming.cloneNode(true));}
      else if(current.innerHTML!==incoming.innerHTML)current.replaceChildren(...incoming.cloneNode(true).childNodes);
    }
  }
  renderedView=view;riskCatalogMarkupKey=catalogKey;
}
function render(force=false){
  $('#breadcrumb-view').textContent=views[view];
  $('#navigation').querySelectorAll('[data-view]').forEach(a=>{a.classList.toggle('active',a.dataset.view===view);if(a.dataset.view===view)a.setAttribute('aria-current','page');else a.removeAttribute('aria-current');});
  $('#position-count').textContent=snapshot.account?String(snapshot.positions?.length??0):'—';
  $('#agent-dot').classList.toggle('connected',!!snapshot.broker?.agentConnected);
  $('#agent-label').textContent=snapshot.broker?.agentConnected?'Agent متصل است':'منتظر Agent';
  $('#last-update').textContent=snapshot.lastCycleAt?'آخرین وضعیت بروکر: '+formatTime(snapshot.lastCycleAt,true):'منتظر اولین وضعیت بروکر';
  updateRiskControls(snapshot,ui.risk);
  document.querySelectorAll('[data-action="start"]').forEach(button=>button.disabled=busy||riskPending(ui.risk)||!canStart(snapshot));
  const editing=$('#main').contains(document.activeElement)&&document.activeElement?.matches('input,select,textarea');
  if(!force&&(editing||(view==='settings'&&settingsDirty)||(view==='research'&&ui.research.busy)))return;
  commitViewMarkup(renderView(view,snapshot,ui));$('#main').setAttribute('aria-busy','false');
  updateRiskControls(snapshot,ui.risk);
  if(view==='settings'){settingsBaseline=structuredClone(snapshot.settings);settingsDirty=false;}
  if($('#journal-level'))$('#journal-level').value=ui.level;
  document.querySelectorAll('[data-action="start"]').forEach(button=>button.disabled=busy||riskPending(ui.risk)||!canStart(snapshot));
  document.querySelectorAll('[data-action="refresh"],[data-action="pause"]').forEach(button=>button.disabled=busy||ui.risk.saving);
}
function acceptSnapshot(next){
  const historyChanged=next.historySync?.lastSyncedAt!==snapshot.historySync?.lastSyncedAt;
  const removedPosition=(snapshot.positions??[]).some(p=>!(next.positions??[]).some(n=>n.ticket===p.ticket));
  if(snapshot.analysis?.symbol!==next.analysis?.symbol){chartRequests.invalidate();ui.timeframe=next.settings?.strategy?.entryTimeframeMinutes??5;ui.chartHistory=[];ui.chartLoading=false;}
  if(ui.risk.draftId&&next.entriesEnabled)discardRiskDraft(ui.risk);
  else if(ui.risk.baseline&&!ui.risk.saving&&settingsKey(ui.risk.baseline)!==settingsKey(next.settings))ui.risk.stale=true;
  snapshot=next;
  if(ui.journal)ui.journal=[...new Map([...(next.journal??[]),...ui.journal].map(r=>[r.id,r])).values()].sort((a,b)=>b.id-a.id);
  render();
  if(view==='positions'&&!ui.trades.loading&&(historyChanged||removedPosition||Date.now()-(ui.trades.attemptedAt??0)>10000))void loadTrades();
}
function showLogin(){
  sessionRevision++;stream?.close();clearTimeout(retryTimer);chartRequests.invalidate();tradeRequests.invalidate();journalRequests.invalidate();settingsDirty=false;settingsBaseline=null;
  ui.chartHistory=[];ui.chartLoading=false;ui.journal=null;ui.query='';
  ui.research={request:null,result:null,optimization:null,completedRun:null,busy:false,error:null,source:''};
  ui.risk=createRiskState();
  ui.trades={items:[],accounts:[],nextCursor:null,loading:false,loaded:false,loadedAt:0,error:null,query:'',exporting:false};
  ui.journalNextBefore=null;ui.journalLoading=false;
  snapshot={settings:{},positions:[],journal:[],broker:{},unresolvedIntents:[]};render(true);setFeed(false);
  if(!$('#login-dialog').open)$('#login-dialog').showModal();
}
async function loadFont(){
  try{const config=await api('/api/ui');if(config.modamAvailable&&!$('#modam-stylesheet')){
    const link=document.createElement('link');link.id='modam-stylesheet';link.rel='stylesheet';link.href='/css/modam.css?v=20261007-ui2';document.head.append(link);
  }}catch{/* Optional font loading never blocks controls. */}
}
async function connect(){
  clearTimeout(retryTimer);stream?.close();
  const loaded=await connectWithRetry(async()=>{const next=await api('/api/snapshot');acceptSnapshot(next);return true;},
    ()=>{clearTimeout(retryTimer);retryTimer=setTimeout(connect,5000);},error=>{setFeed(false);toast(error.message,true);},()=>$('#login-dialog').open);
  if(!loaded)return;
  if(!ui.risk.profiles.length&&!ui.risk.loading)void loadRiskProfiles();
  stream=new EventSource('/api/events');
  stream.addEventListener('snapshot',event=>{try{acceptSnapshot(JSON.parse(event.data));setFeed(true);}catch{toast('پاسخ وضعیت قابل خواندن نبود.',true);}});
  stream.onerror=()=>{
    setFeed(false);clearTimeout(retryTimer);retryTimer=setTimeout(async()=>{
      try{const session=await api('/api/auth/session');if(!session.authenticated)showLogin();else await connect();}
      catch{retryTimer=setTimeout(connect,5000);}
    },5000);
  };
}
function confirm(title,message,callback){$('#confirm-title').textContent=title;$('#confirm-message').textContent=message;confirmCallback=callback;$('#confirm-dialog').showModal();}
function closeDialogs(){document.querySelectorAll('dialog[open]:not(#login-dialog)').forEach(d=>d.close());confirmCallback=null;}
async function control(action){
  if(busy||ui.risk.saving)return;
  if(action==='start'&&(riskPending(ui.risk)||!canStart(snapshot))){toast('ابتدا پیش‌نمایش ریسک را اعمال یا لغو کن و آمادگی اتصال را بررسی کن.',true);return;}
  busy=true;ui.risk.operationPending=true;updateRiskControls(snapshot,ui.risk);document.querySelectorAll('[data-action="refresh"],[data-action="start"],[data-action="pause"]').forEach(b=>b.disabled=true);
  try{await api('/api/control/'+action,'POST');acceptSnapshot(await api('/api/snapshot'));toast(action==='start'?'ورود خودکار فعال شد.':action==='pause'?'ورود جدید متوقف شد. محافظت معاملات موجود ادامه دارد.':action==='reset-risk'?'مرجع بک‌اند ریست شد؛ ورود همچنان متوقف است.':'وضعیت با بروکر تطبیق داده شد.');}
  catch(error){toast(error.message,true);}finally{busy=false;ui.risk.operationPending=false;render(true);}
}
async function chooseTimeframe(timeframe){
  const symbol=snapshot.analysis?.symbol;if(!symbol)return;
  ui.timeframe=timeframe;ui.chartHistory=[];
  const key=symbol+':'+timeframe,token=chartRequests.begin(key);
  if(timeframe===(snapshot.settings.strategy?.entryTimeframeMinutes??5)){ui.chartLoading=false;render(true);return;}
  ui.chartLoading=true;render(true);
  try{const data=await api('/api/market/history?timeframe='+timeframe+'&symbol='+encodeURIComponent(symbol));
    if(chartRequests.isCurrent(token,snapshot.analysis?.symbol+':'+ui.timeframe))ui.chartHistory=data;
  }catch(error){if(chartRequests.isCurrent(token,snapshot.analysis?.symbol+':'+ui.timeframe))toast(error.message,true);}
  finally{if(chartRequests.isCurrent(token,snapshot.analysis?.symbol+':'+ui.timeframe)){ui.chartLoading=false;render(true);}}
}
async function saveSettings(settings,expectedSettings){
  if(busy||ui.risk.saving)return;
  if(!expectedSettings||settingsKey(expectedSettings)!==settingsKey(snapshot.settings)){toast('تنظیمات از زمان بازشدن فرم تغییر کرده‌اند؛ صفحه را دوباره باز کن و مقادیر تازه را بررسی کن.',true);return;}
  busy=true;ui.risk.operationPending=true;
  let saved=false;
  try{await api('/api/settings','POST',{settings,expectedSettings});saved=true;settingsDirty=false;acceptSnapshot(await api('/api/snapshot'));render(true);toast('تنظیمات ذخیره شد.');}
  catch(error){toast(error.message,true);}
  finally{busy=false;ui.risk.operationPending=false;render(saved);}
}
async function loadRiskProfiles(){
  if(ui.risk.loading)return;
  const state=ui.risk;state.loading=true;state.error=null;render();
  try{const profiles=await api('/api/risk-profiles');if(state!==ui.risk)return;
    if(!Array.isArray(profiles)||profiles.length<2||profiles.length>10)throw new Error('فهرست حالت‌های ریسک معتبر نیست.');
    state.profiles=profiles;
  }catch(error){if(state===ui.risk)state.error=error.message;}
  finally{if(state===ui.risk){state.loading=false;render();}}
}
function chooseRisk(index){
  if(snapshot.entriesEnabled||busy||ui.risk.saving)return;
  const profile=ui.risk.profiles[Math.round(Number(index))];if(!profile)return;
  const saved=selectedRisk(snapshot,{...ui.risk,draftId:null}).profile;
  if(saved?.id===profile.id)discardRiskDraft(ui.risk);
  else{ui.risk.baseline??=structuredClone(snapshot.settings);ui.risk.draftId=profile.id;ui.risk.error=null;}
  updateRiskControls(snapshot,ui.risk);
}
async function applyRiskProfile(){
  const state=ui.risk;
  if(snapshot.entriesEnabled||busy||state.saving||!state.draftId||state.stale)return;
  const id=state.draftId,baseline=state.baseline;
  if(!baseline||settingsKey(baseline)!==settingsKey(snapshot.settings)){state.stale=true;render(true);return;}
  state.saving=true;render(true);
  try{const saved=await api('/api/risk-profiles/'+encodeURIComponent(id)+'/apply','POST',baseline);
    if(state!==ui.risk)return;
    discardRiskDraft(state);acceptSnapshot({...snapshot,settings:saved,analysis:null,lastCycleAt:null});
    toast('حالت ریسک ذخیره شد؛ ورود خودکار همچنان متوقف است.');
    acceptSnapshot(await api('/api/snapshot'));
  }catch(error){if(state===ui.risk){state.error=error.message;toast(error.message,true);}}
  finally{if(state===ui.risk){state.saving=false;render(true);}}
}
function rememberResearchFields(){
  const form=$('#research-form');if(!form)return;
  ui.research.fields=Object.fromEntries(new FormData(form));
}
async function importResearch(){
  if(ui.research.busy)return;
  ui.research.busy=true;ui.research.error=null;render(true);
  try{ui.research.request=await api('/api/research/market?count=3000');ui.research.fields={};
    ui.research.source='بروکر / '+ui.research.request.symbol.symbolName;ui.research.result=null;ui.research.optimization=null;ui.research.completedRun=null;}
  catch(error){ui.research.error=error.message;}
  finally{ui.research.busy=false;render(true);}
}
async function runResearch(optimize=false){
  if(ui.research.busy||!ui.research.request)return;
  const form=$('#research-form');if(!form?.reportValidity())return;
  rememberResearchFields();
  const f=ui.research.fields;
  const request={...ui.research.request,parameters:{...ui.research.request.parameters,mode:f.mode,
    stopAtrMultiplier:Number(f.stopAtrMultiplier),rewardRiskRatio:Number(f.rewardRiskRatio)}};
  for(const key of ['initialEquity','riskPercent','maximumRiskAmount','dailyTradeLimit','spreadPoints','slippagePoints','commissionPerLot'])request[key]=Number(f[key]);
  ui.research.busy=true;ui.research.error=null;render(true);
  try{
    const submitted=recordResearchRun(ui.research.source,optimize?'optimization':'backtest',optimize?{backtest:request,
      trainingFraction:Number(f.trainingFraction),maximumDrawdownPercent:Number(f.maximumDrawdownPercent),minimumTrainingTrades:Number(f.minimumTrainingTrades),
      stopAtrMultipliers:[1,1.5,2],rewardRiskRatios:[1.5,2,3]}:request,null);
    const result=await api(optimize?'/api/research/optimize':'/api/research/backtest','POST',submitted.request);
    ui.research.completedRun=recordResearchRun(submitted.source,submitted.kind,submitted.request,result);
    ui.research.optimization=optimize?ui.research.completedRun.result:null;
    ui.research.result=optimize?null:ui.research.completedRun.result;
    toast('پژوهش پایان یافت؛ تنظیمات زنده تغییر نکرد.');
  }catch(error){ui.research.error=error.message;}
  finally{ui.research.busy=false;render(true);}
}
function exportResearch(){
  if(!ui.research.completedRun)return;
  const url=URL.createObjectURL(new Blob([JSON.stringify(ui.research.completedRun,null,2)],{type:'application/json'}));
  const link=document.createElement('a');link.href=url;link.download='trader-research-'+new Date().toISOString().slice(0,10)+'.json';link.click();
  setTimeout(()=>URL.revokeObjectURL(url),1000);
}
function commands(){
  $('#command-list').innerHTML=Object.entries(views).map(([key,title])=>'<button data-action="navigate" data-view="'+key+'">'+icon(({overview:'grid',market:'chart',research:'chart',positions:'wallet',journal:'activity',connections:'signal',settings:'sliders'})[key])+title+'</button>').join('');
  $('#command-dialog').showModal();
}
function positionDetail(ticket){
  const p=snapshot.positions.find(p=>p.ticket===ticket);if(!p)return;
  $('#detail-content').innerHTML=renderPositionDetail(p,null,snapshot.account);
  $('#detail-dialog').showModal();
}
async function loadTrades(more=false){
  if(ui.trades.loading||$('#login-dialog').open)return;
  const cursor=more?ui.trades.nextCursor:null;if(more&&!cursor)return;
  const token=tradeRequests.begin('history');ui.trades.attemptedAt=Date.now();ui.trades.loading=true;ui.trades.error=null;render();
  try{
    const page=await api('/api/trades/history?limit=50'+(cursor?'&cursor='+encodeURIComponent(cursor):''));
    if(!tradeRequests.isCurrent(token,'history'))return;
    const wasLoaded=ui.trades.loaded;
    const loadedKeys=new Set((ui.trades.items??[]).map(tradeKey));
    const overlapsLoaded=page.items.some(t=>loadedKeys.has(tradeKey(t)));
    ui.trades.items=[...new Map([...(ui.trades.items??[]),...page.items].map(t=>[tradeKey(t),t])).values()]
      .sort((a,b)=>b.position.closeTime-a.position.closeTime||b.position.ticket-a.position.ticket);
    ui.trades.accounts=page.accounts;
    const unseenRecords=page.accounts.reduce((total,account)=>total+account.trades,0)>ui.trades.items.length;
    // Keep older-page progress on overlapping live refreshes; restart only to bridge a new gap.
    if(more||!wasLoaded||!overlapsLoaded||(!ui.trades.nextCursor&&unseenRecords))ui.trades.nextCursor=page.nextCursor;
    ui.trades.loaded=true;ui.trades.loadedAt=Date.now();
  }catch(error){if(tradeRequests.isCurrent(token,'history'))ui.trades.error=error.message;}
  finally{if(tradeRequests.isCurrent(token,'history')){ui.trades.loading=false;render();}}
}
async function loadJournal(more=false){
  if(ui.journalLoading||$('#login-dialog').open)return;
  const before=more?ui.journalNextBefore:null;if(more&&!before)return;
  const token=journalRequests.begin('journal');ui.journalLoading=true;render();
  try{
    const page=await api('/api/journal/page?limit=200'+(before?'&before='+before:''));
    if(!journalRequests.isCurrent(token,'journal'))return;
    ui.journal=[...new Map([...(ui.journal??[]),...page.items].map(r=>[r.id,r])).values()].sort((a,b)=>b.id-a.id);
    ui.journalNextBefore=page.nextBefore;
  }catch(error){if(journalRequests.isCurrent(token,'journal'))toast(error.message,true);}
  finally{if(journalRequests.isCurrent(token,'journal')){ui.journalLoading=false;render();}}
}
async function exportTradeHistory(){
  if(ui.trades.exporting)return;
  const revision=sessionRevision;ui.trades.exporting=true;render();
  try{
    const records=new Map(),cursors=new Set();let cursor=null;
    do{
      const page=await api('/api/trades/history?limit=100'+(cursor?'&cursor='+encodeURIComponent(cursor):''));
      if(revision!==sessionRevision)throw new Error('نشست تغییر کرد؛ خروجی لغو شد.');
      for(const trade of page.items)records.set(tradeKey(trade),trade);
      cursor=page.nextCursor;
      if(cursor&&cursors.has(cursor))throw new Error('صفحه‌بندی سابقه معتبر نیست.');
      if(cursor)cursors.add(cursor);
    }while(cursor);
    const trades=[...records.values()];
    const result={exportedAt:new Date().toISOString(),coverage:'Confirmed records in local SQLite; limited to the MT4 Account History range loaded during synchronization.',
      brokerTimestamps:'Broker wall clock; not converted to UTC.',historySync:snapshot.historySync,recordCount:trades.length,trades};
    const url=URL.createObjectURL(new Blob([JSON.stringify(result,null,2)],{type:'application/json'}));
    const link=document.createElement('a');link.href=url;link.download='trader-trade-history-'+new Date().toISOString().slice(0,10)+'.json';link.click();
    setTimeout(()=>URL.revokeObjectURL(url),1000);toast('خروجی سابقه آماده شد؛ شامل اطلاعات مالی حساب است، عمومی منتشرش نکن.');
  }catch(error){if(revision===sessionRevision)toast(error.message,true);}
  finally{if(revision===sessionRevision){ui.trades.exporting=false;render();}}
}
document.addEventListener('click',async event=>{
  const b=event.target.closest('[data-action]');if(!b||b.disabled)return;const action=b.dataset.action;
  try{
    if(action==='risk-select')chooseRisk(b.dataset.index);
    else if(action==='risk-reload')await loadRiskProfiles();
    else if(action==='risk-cancel'){if(!ui.risk.saving){discardRiskDraft(ui.risk);render(true);}}
    else if(action==='risk-apply')await applyRiskProfile();
    else if(action==='history-refresh')await loadTrades();
    else if(action==='history-more')await loadTrades(true);
    else if(action==='history-export')await exportTradeHistory();
    else if(action==='journal-more')await loadJournal(true);
    else if(action==='history-detail'){
      const trade=ui.trades.items.find(t=>tradeKey(t)===b.dataset.tradeKey);if(!trade)return;
      $('#detail-content').innerHTML=renderPositionDetail(trade.position,trade.context??{accountId:trade.accountId,brokerServer:trade.brokerServer});
      $('#detail-dialog').showModal();
    }
    else if(action==='menu'){$('.sidebar').classList.toggle('open');$('.menu-backdrop').classList.toggle('visible');}
    else if(action==='commands')commands();
    else if(action==='navigate'){closeDialogs();location.hash=b.dataset.view;}
    else if(action==='journal')location.hash='journal';
    else if(action==='close-dialog')closeDialogs();
    else if(action==='confirm'){const callback=confirmCallback;closeDialogs();await callback?.();}
    else if(action==='refresh'||action==='pause')await control(action);
    else if(action==='start'){
      if(riskPending(ui.risk)||!canStart(snapshot)){toast('ابتدا پیش‌نمایش ریسک را اعمال یا لغو و اتصال، حساب و مجوز EA را بررسی کن.',true);return;}
      confirm('فعال‌سازی ورود خودکار','ربات می‌تواند روی حساب متصل سفارش ارسال کند. توقف بعدی فقط ورودهای جدید را می‌بندد؛ سفارش در حال ارسال ممکن است تکمیل شود.',()=>control('start'));
    }else if(action==='timeframe')await chooseTimeframe(Number(b.dataset.timeframe));
    else if(action==='reset-risk')confirm('ریست دستی حفاظت سرمایه','مرجع افت سرمایه بک‌اند با Equity فعلی جایگزین می‌شود. ورود باید متوقف باشد؛ این کار حفاظت مستقل EA را ریست نمی‌کند. ادامه می‌دهی؟',()=>control('reset-risk'));
    else if(action==='research-import')await importResearch();
    else if(action==='research-optimize')await runResearch(true);
    else if(action==='research-export')exportResearch();
    else if(action==='position')positionDetail(Number(b.dataset.ticket));
    else if(action==='session'){
      $('#detail-content').innerHTML='<p class="eyebrow">OPERATOR SESSION</p><h2>نشست پنل کنترل</h2><p class="muted">'+(passwordRequired?'نشست امن اپراتور فعال است.':'دسترسی محلی روی لپ‌تاپ؛ برای استفاده‌ی سرور، رمز قوی الزامی است.')+'</p>'+(passwordRequired?'<button class="button secondary" data-action="logout">خروج از نشست</button>':'');$('#detail-dialog').showModal();
    }else if(action==='logout'){await api('/api/auth/logout','POST');closeDialogs();showLogin();}
    else if(action==='export'){
      const url=URL.createObjectURL(new Blob([JSON.stringify(ui.journal??snapshot.journal,null,2)],{type:'application/json'}));
      const link=document.createElement('a');link.href=url;link.download='trader-journal-'+new Date().toISOString().slice(0,10)+'.json';link.click();setTimeout(()=>URL.revokeObjectURL(url),1000);
    }
  }catch(error){toast(error.message,true);}
});
document.addEventListener('input',event=>{
  if(event.target.id==='risk-volume')chooseRisk(event.target.value);
  if(event.target.closest('#settings-form'))settingsDirty=true;
  if(event.target.id==='journal-search'){ui.query=event.target.value;$('#journal-rows').innerHTML=journalRows(snapshot,ui);}
  if(event.target.id==='trade-search'){ui.trades.query=event.target.value;render(true);$('#trade-search')?.focus();}
  if(event.target.closest('#research-form')&&event.target.id!=='research-file')rememberResearchFields();
});
document.addEventListener('change',async event=>{
  if(event.target.closest('#settings-form'))settingsDirty=true;
  if(event.target.id==='journal-level'){ui.level=event.target.value;$('#journal-rows').innerHTML=journalRows(snapshot,ui);}
  if(event.target.closest('#research-form')&&event.target.id!=='research-file')rememberResearchFields();
  if(event.target.id==='research-file'){
    const file=event.target.files?.[0];if(!file)return;
    const revision=sessionRevision;
    try{
      if(file.size>2*1024*1024)throw new Error('حجم فایل باید کمتر از ۲ مگابایت باشد.');
      const input=JSON.parse(await file.text());
      if(revision!==sessionRevision)return;
      if(!Array.isArray(input.candles)||input.candles.length<2||input.candles.length>5000||!input.symbol)
        throw new Error('فایل باید شامل candles صعودی و مشخصات کامل symbol باشد.');
      ui.research.request={...input,parameters:input.parameters??snapshot.settings.strategy};
      ui.research.fields={};ui.research.source=file.name;ui.research.error=null;ui.research.result=null;ui.research.optimization=null;ui.research.completedRun=null;
    }catch(error){ui.research.error=error.message;}
    render(true);
  }
});
document.addEventListener('submit',async event=>{
  if(event.target.id==='login-form'){
    event.preventDefault();$('#login-error').textContent='';
    try{
      const response=await fetch('/api/auth/login',{method:'POST',headers:{'Content-Type':'application/json'},body:JSON.stringify({password:$('#password').value})});
      if(!response.ok)throw new Error(response.status===429?'کمی صبر کنید و دوباره تلاش کنید.':'رمز واردشده صحیح نیست.');
      $('#password').value='';$('#login-dialog').close();await connect();await loadFont();
    }catch(error){$('#login-error').textContent=error.message;}
  }else if(event.target.id==='settings-form'){
    event.preventDefault();if(busy||ui.risk.saving||snapshot.entriesEnabled)return;const form=new FormData(event.target);
    const baseline=structuredClone(settingsBaseline);
    if(!baseline||settingsKey(baseline)!==settingsKey(snapshot.settings)){toast('این فرم قدیمی است؛ صفحهٔ تنظیمات را دوباره باز کن و تغییرات تازه را بررسی کن.',true);return;}
    const settings={...baseline,symbol:String(form.get('symbol')??'').trim(),telegramEnabled:form.has('telegramEnabled'),allowLiveAccount:form.has('allowLiveAccount')};
    for(const key of ['dailyTradeLimit','riskPercent','maximumRiskAmount','maximumSpreadPoints','analysisIntervalSeconds','positionIntervalSeconds','dailyDrawdownLimitPercent','totalDrawdownLimitPercent','drawdownCooldownHours'])if(form.has(key))settings[key]=Number(form.get(key));
    settings.strategy={...baseline.strategy};
    const switches=['requireMacroTrend','requireBreakoutRetest','trailingEnabled'];
    for(const [key,value] of form)if(key.startsWith('strategy.')){const field=key.slice(9);if(!switches.includes(field))settings.strategy[field]=field==='mode'?value:Number(value);}
    for(const field of switches)settings.strategy[field]=form.has('strategy.'+field);
    if(settings.allowLiveAccount&&!baseline.allowLiveAccount)confirm('اجازه‌ی حساب واقعی','این گزینه مجوز بک‌اند را تغییر می‌دهد. اجرای واقعی به تأیید جداگانه در EA وابسته است.',()=>saveSettings(settings,baseline));
    else await saveSettings(settings,baseline);
  }else if(event.target.id==='research-form'){event.preventDefault();await runResearch();}
});
window.addEventListener('hashchange',async()=>{
  view=views[location.hash.slice(1)]?location.hash.slice(1):'overview';$('.sidebar').classList.remove('open');$('.menu-backdrop').classList.remove('visible');render(true);
  if(view==='journal')await loadJournal();
  if(view==='positions')await loadTrades();
});
document.addEventListener('keydown',event=>{
  if(event.key==='/'&&!event.target.matches('input,textarea,select')&&!document.querySelector('dialog[open]')){event.preventDefault();commands();}
});
$('#login-dialog').addEventListener('cancel',event=>event.preventDefault());
window.addEventListener('pagehide',()=>{stream?.close();clearTimeout(retryTimer);});
async function boot(){
  try{const session=await api('/api/auth/session');passwordRequired=session.passwordRequired;if(!session.authenticated){showLogin();return;}await connect();await loadFont();if(view==='journal')await loadJournal();}
  catch(error){render();setFeed(false);toast(error.message,true);retryTimer=setTimeout(boot,5000);}
}
await boot();
