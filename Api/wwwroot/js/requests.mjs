export class LatestRequest {
  #revision = 0;
  begin(key) { return { key, revision: ++this.#revision }; }
  isCurrent(token, key) { return token.revision === this.#revision && token.key === key; }
  invalidate() { this.#revision++; }
}

export async function readJsonResponse(response) {
  if(response.status===204)return null;
  const body=await response.text();
  if(!body.trim())return null;
  if(!response.headers.get('content-type')?.toLowerCase().includes('json')||body.trimStart().startsWith('<')){
    const error=new Error('API قدیمی یا مسیر ناسازگار است. برنامهٔ بک‌اند قبلی را متوقف و دوباره از Start-Trader اجرا کن؛ سپس Ctrl+F5 بزن. بستن تب مرورگر کافی نیست.');
    error.code='backend-contract';throw error;
  }
  try{return JSON.parse(body);}
  catch{throw new Error('پاسخ API، JSON معتبر نیست؛ اتصال و نسخهٔ بک‌اند را بررسی کن.');}
}

export async function connectWithRetry(load, schedule, report, cancelled = () => false) {
  try { return await load(); }
  catch (error) {
    report(error);
    if (!cancelled()) schedule(() => connectWithRetry(load, schedule, report, cancelled));
    return null;
  }
}
