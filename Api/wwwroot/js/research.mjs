import { formatNumber as n, escapeHtml as e, actionLabel, formatTime } from './model.mjs?v=20261007-ui2';
import { icon } from './icons.mjs?v=20261007-ui2';

export function strategySettings(s) {
  const p=s.settings?.strategy??{},disabled=s.entriesEnabled?'disabled':'';
  const fields=[['fastEmaPeriod','دوره EMA سریع',2,199,1],['slowEmaPeriod','دوره EMA کند',3,200,1],
    ['atrPeriod','دوره ATR',2,100,1],['atrAveragePeriod','میانگین ATR',2,100,1],['adxPeriod','دوره ADX',2,100,1],
    ['strongTrendAdx','حد روند قوی موتور اندیکاتوری',1,100,.1],['autoSwitchAdx','مرز مستقل انتخاب Auto / ADX',1,100,.1],['weakTrendAdx','حد روند ضعیف ADX',0,99,.1],
    ['momentumAdx','حد ورود مومنتوم',1,100,.1],['atrSpikeMultiplier','ضریب جهش ATR',.1,10,.1],
    ['stopAtrMultiplier','ضریب ATR استاپ',.1,10,.1],['rewardRiskRatio','نسبت سود به ریسک',.1,10,.1],
    ['targetAtrMultiplier','حداقل هدف / ATR',.1,20,.1],['swingLookbackBars','پنجره ساختار / کندل بسته',2,200,1],
    ['structuralStopBufferAtr','حاشیه استاپ ساختاری / ATR',0,5,.01],
    ['macroFastEmaPeriod','EMA سریع فاز کلان',2,199,1],['macroSlowEmaPeriod','EMA کند فاز کلان',3,200,1],
    ['minimumEntryAdx','حداقل ADX ورود',0,100,.1],['minimumBodyRatio','حداقل نسبت بدنه؛ صفر = خاموش',0,1,.00001],
    ['retestToleranceAtr','تلورانس ری‌تست / ATR',0,5,.01],['breakoutBufferAtr','حاشیه شکست / ATR',0,5,.01],
    ['commissionPerLot','کارمزد رفت‌وبرگشت / لات / ارز حساب',0,10000,.01],['slippageBufferPoints','ذخیره لغزش هر پرشدن / Point',0,10000,1],
    ['trailingActivationAtr','شروع تریلینگ / ATR',.1,20,.1],['trailingDistanceAtr','فاصله تریلینگ / ATR',.1,20,.1],
    ['trailingStepPoints','حداقل بهبود استاپ / Point',0,10000,1],['spreadLookbackSamples','نمونه‌های اسپرد مشاهده‌شده',2,1000,1],
    ['spreadSpikeMultiplier','سقف اسپرد / میانگین قبلی',.1,10,.1],
    ['zoneMergeAtr','فاصله ادغام ناحیه / ATR',.01,5,.01],['minimumZoneStrength','حداقل قدرت ناحیه',2,20,1],
    ['retestExpiryBars','انقضای بازآزمایی / کندل',1,1000,1],['historyBars','کندل‌های محاسبات',30,1000,1],
    ['maximumQuoteAgeSeconds','حداکثر عمر قیمت / ثانیه',1,3600,1]];
  const modes=[['Auto','انتخاب خودکار'],['Indicator','اندیکاتوری'],['PriceAction','پرایس‌اکشن']];
  return '<details class="strategy-details" open><summary>'+icon('sliders')+'پارامترهای استراتژی و محاسبات</summary><p class="muted">همین پارامترها در تحلیل زنده و پژوهش استفاده می‌شوند. دوره EMA کند باید از سریع بزرگ‌تر باشد.</p><div class="settings-grid">'+
    '<label class="setting-field"><span>موتور تصمیم</span><select name="strategy.mode" '+disabled+'>'+modes.map(([v,t])=>'<option value="'+v+'" '+(p.mode===v?'selected':'')+'>'+t+'</option>').join('')+'</select></label>'+
    ['entry','pattern','phase'].map((kind,i)=>'<label class="setting-field"><span>'+['تایم‌فریم ورود','تایم‌فریم الگو','تایم‌فریم فاز'][i]+'</span><select name="strategy.'+kind+'TimeframeMinutes" '+disabled+'>'+[1,5,15,30,60,240,1440].map(tf=>'<option value="'+tf+'" '+(p[kind+'TimeframeMinutes']===tf?'selected':'')+'>'+tf+' دقیقه</option>').join('')+'</select></label>').join('')+
    fields.map(([key,label,min,max,step])=>'<label class="setting-field"><span>'+label+'</span><input name="strategy.'+key+'" type="number" value="'+e(p[key]??'')+'" min="'+min+'" max="'+max+'" step="'+step+'" required '+disabled+'></label>').join('')+'</div><div class="switch-settings">'+
    [['requireMacroTrend','تأیید EMA کلان در هر دو موتور'],['requireBreakoutRetest','الزام شکست و ری‌تست کندل بعدی'],['trailingEnabled','مدل تریلینگ کندل‌بسته؛ ورودی EA باید یکسان باشد']]
      .map(([key,label])=>'<label class="switch-row"><strong>'+label+'</strong><input type="checkbox" name="strategy.'+key+'" '+(p[key]?'checked':'')+' '+disabled+'></label>').join('')+
    '</div><p class="muted">در Auto، زیر حداقل ADX ورود معامله‌ای باز نمی‌شود؛ از آن حد تا مرز Auto، پرایس‌اکشن در صورت وجود ناحیهٔ معتبر بررسی می‌شود و از مرز Auto به بالا موتور اندیکاتوری انتخاب می‌شود. مرز Auto باید از حداقل ADX ورود بزرگ‌تر باشد. آستانه‌ها فرضیه‌اند، نه اثبات سودآوری. Kelly، خروج MAE و Mark Price مستقل فعال نیستند.</p></details>';
}

function equityChart(curve) {
  const data=(curve??[]).filter(p=>Number.isFinite(p.equity)&&Number.isFinite(p.time));
  if(data.length<2)return '<div class="chart-empty"><p>نمودار پس از اجرای بک‌تست نمایش داده می‌شود.</p></div>';
  const low=Math.min(...data.map(p=>p.equity)),high=Math.max(...data.map(p=>p.equity)),range=Math.max(high-low,1);
  const points=data.map((p,i)=>(20+i/(data.length-1)*860).toFixed(2)+','+(190-(p.equity-low)/range*155).toFixed(2)).join(' ');
  return '<svg class="equity-chart" viewBox="0 0 900 220" role="img" aria-label="منحنی ارزش شبیه‌سازی‌شده حساب"><defs><linearGradient id="equity-fill" x1="0" y1="0" x2="0" y2="1"><stop offset="0%" stop-color="#65d6bc" stop-opacity=".2"/><stop offset="100%" stop-color="#65d6bc" stop-opacity="0"/></linearGradient></defs><polygon points="20,200 '+points+' 880,200" fill="url(#equity-fill)"/><polyline points="'+points+'" fill="none" stroke="#65d6bc" stroke-width="2.2" vector-effect="non-scaling-stroke"/><text x="20" y="215">'+n(low)+'</text><text x="880" y="18" text-anchor="end">'+n(high)+'</text></svg>';
}

function metrics(result,title) {
  if(!result)return '';
  const factor=result.profitFactor==null?(result.trades.length?'بدون زیان ثبت‌شده':'—'):n(result.profitFactor);
  return '<section class="glass research-result"><div class="section-heading"><div><h2>'+title+'</h2><p>نتایج شبیه‌سازی با مفروضات ثبت‌شده</p></div><span class="badge purple">'+result.trades.length+' معامله</span></div><div class="research-metrics">'+
    [['بازده',n(result.returnPercent)+'%'],['سود و زیان خالص',n(result.netProfit)],['بیشترین افت سرمایه',n(result.maximumDrawdownPercent)+'%'],['نرخ برد',n(result.winRatePercent)+'%'],['ضریب سود',factor],['امید ریاضی هر معامله',n(result.expectancy)]].map(([k,v])=>'<div><span>'+k+'</span><strong class="ltr">'+e(v)+'</strong></div>').join('')+'</div>'+equityChart(result.equityCurve)+
    (result.trades.length===0?'<div class="guard-note">ورودی کافی برای گرم‌شدن تمام تایم‌فریم‌ها و شرایط ورود لازم است؛ نبود معامله، نشانه سودآوری نیست.</div>':'')+
    '<details class="research-assumptions"><summary>مفروضات و محدودیت شبیه‌سازی</summary><p dir="ltr">'+e(result.assumptions)+'</p></details></section>';
}

export function renderResearch(s,ui) {
  const r=ui.research??{},request=r.request??{},fields=r.fields??{},p=request.parameters??s.settings?.strategy??{};
  const field=(key,label,value,min,max,step='.01')=>'<label class="setting-field"><span>'+label+'</span><input name="'+key+'" type="number" value="'+e(fields[key]??value)+'" min="'+min+'" max="'+max+'" step="'+step+'" required '+(r.busy?'disabled':'')+'></label>';
  const mode=fields.mode??p.mode??'Auto';
  let html='<section class="glass research-intro"><span class="badge purple">POLICY → REFLECTION → DEPLOYMENT</span><h2>پیش از ورود، فرضیه را امتحان کن.</h2><p>داده تاریخی را وارد کنید، هزینه‌ها را مشخص کنید و نتیجه را روی بازه جداگانه بسنجید. پارامترهای منتخب، خودکار وارد حساب زنده نمی‌شوند.</p></section>'+
    '<form id="research-form"><section class="glass settings-panel"><div class="section-heading"><div><h2>داده و مفروضات پژوهش</h2><p>JSON شامل candles و symbol؛ برای H4، phaseCandles با پیش‌تاریخچه لازم است. سقف هر سری ۵۰۰۰ کندل.</p></div><span class="badge '+(r.request?'success':'neutral')+'">'+(r.request?r.request.candles.length+' کندل':'منتظر داده')+'</span></div>'+
    '<div class="research-sources"><label class="button secondary" for="research-file">'+icon('cloud')+'ورود فایل JSON</label><input id="research-file" type="file" accept=".json,application/json" '+(r.busy?'disabled':'')+'><button type="button" class="button secondary" data-action="research-import" '+(!s.analysis||r.busy?'disabled':'')+'>'+icon('refresh')+'دریافت از بروکر</button><small>'+e(r.source||'داده‌ای بارگذاری نشده است')+'</small></div>'+
    (s.settings?.observationOnly?'<p class="guard-note">حالت پایش، ریسک زنده و سهمیه را صفر کرده است. برای پژوهش، ریسک، مبلغ و سهمیهٔ فرضی مثبت وارد کن؛ تنظیمات حساب زنده تغییر نمی‌کنند.</p>':'')+
    '<div class="settings-grid"><label class="setting-field"><span>موتور پژوهش</span><select name="mode" '+(r.busy?'disabled':'')+'>'+[['Auto','خودکار'],['Indicator','اندیکاتوری'],['PriceAction','پرایس‌اکشن']].map(([v,t])=>'<option value="'+v+'" '+(v===mode?'selected':'')+'>'+t+'</option>').join('')+'</select></label>'+
    field('initialEquity','سرمایه فرضی / واحد حساب',request.initialEquity??1000,.01,1e9)+field('riskPercent','ریسک هر ورود (%)',request.riskPercent??1,.01,5)+
    field('maximumRiskAmount','سقف مبلغ ریسک',request.maximumRiskAmount??5,.01,10000)+field('dailyTradeLimit','سقف ورود روزانهٔ فرضی',request.dailyTradeLimit??10,1,100,1)+field('spreadPoints','اسپرد فرضی / Point',request.spreadPoints??10,0,10000)+
    field('slippagePoints','لغزش نامطلوب / Point',request.slippagePoints??1,0,10000)+field('commissionPerLot','کارمزد رفت‌وبرگشت / لات',request.commissionPerLot??7,0,10000)+
    field('stopAtrMultiplier','ضریب ATR استاپ',p.stopAtrMultiplier??1.5,.1,10,.1)+field('rewardRiskRatio','نسبت سود به ریسک',p.rewardRiskRatio??2,.1,10,.1)+
    field('trainingFraction','سهم آموزش / باقی برای اعتبارسنجی',.7,.5,.85,.05)+field('maximumDrawdownPercent','سقف افت سرمایه آموزش (%)',15,.1,100,.1)+
    field('minimumTrainingTrades','حداقل تعداد معاملات آموزش',5,1,1000,1)+'</div>'+
    '<div class="settings-footer"><p>'+icon('shield')+'ورود با کندل بعدی؛ لحاظ اسپرد و هزینه؛ استاپ در ابهام اولویت دارد.</p><div class="heading-actions"><button type="submit" class="button primary" '+(!r.request||r.busy?'disabled':'')+'>'+icon('play')+(r.busy?'در حال محاسبه…':'اجرای بک‌تست')+'</button><button type="button" class="button secondary" data-action="research-optimize" '+(!r.request||r.busy?'disabled':'')+'>'+icon('sliders')+'جست‌وجو و اعتبارسنجی</button></div></div></section></form>'+
    (r.error?'<div class="warning-strip" role="alert">'+icon('info')+e(r.error)+'</div>':'');
  if(r.optimization){
    const o=r.optimization,selected=o.selected;
    html+='<section class="glass research-selection"><div class="section-heading"><div><h2>گزینش روی داده آموزش</h2><p>'+o.candidatesEvaluated+' ترکیب بررسی شد؛ آزمون از '+new Date(o.validationStartTime*1000).toLocaleDateString('fa-IR')+' آغاز می‌شود.</p></div>'+
      '<button class="button secondary compact" data-action="research-export">'+icon('cloud')+'خروجی پژوهش</button></div>'+
      (selected?'<div class="analysis-summary"><span>ضریب استاپ <strong>'+n(selected.parameters.stopAtrMultiplier,1)+'</strong></span><span>نسبت سود به ریسک <strong>'+n(selected.parameters.rewardRiskRatio,1)+'</strong></span><span>امتیاز آموزش <strong>'+n(selected.score)+'</strong></span></div>':'<div class="guard-note">هیچ ترکیبی، حداقل معاملات و سقف افت سرمایه را رعایت نکرد؛ پارامتری انتخاب نشده است.</div>')+'</section>'+
      metrics(selected?.training,'نتیجه آموزش')+metrics(o.validation,'اعتبارسنجی خارج از نمونه');
  }else if(r.result)html+=metrics(r.result,'نتیجه بک‌تست');
  const result=r.optimization?.validation??r.result;
  if(result?.trades.length)html+='<section class="glass positions-panel"><div class="section-heading"><h2>معاملات شبیه‌سازی‌شده</h2><button class="button secondary compact" data-action="research-export">'+icon('cloud')+'خروجی JSON</button></div><div class="table-scroll"><table><thead><tr><th>زمان ورود</th><th>جهت</th><th>لات</th><th>ورود</th><th>خروج</th><th>سود خالص</th><th>کارمزد</th><th>علت خروج</th></tr></thead><tbody>'+result.trades.slice(-100).reverse().map(t=>'<tr><td>'+formatTime(t.entryTime)+'</td><td>'+actionLabel(t.side)+'</td><td class="ltr">'+n(t.lots)+'</td><td class="ltr">'+n(t.entryPrice,5)+'</td><td class="ltr">'+n(t.exitPrice,5)+'</td><td class="ltr '+(t.profit>=0?'text-green':'text-red')+'">'+n(t.profit)+'</td><td class="ltr">'+n(t.costs)+'</td><td>'+e(t.exitReason)+'</td></tr>').join('')+'</tbody></table></div></section>';
  return html;
}
