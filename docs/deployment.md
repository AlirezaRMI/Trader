# بک‌اند لینوکس، ترمینال ویندوز

کانتینر فقط API، تحلیل، SQLite و UI را اجرا می‌کند. MT4 ویندوزی است؛ Wine بخشی از این تحویل نیست و باید مستقل آزموده شود.

## سرور

1. .env خصوصی: DASHBOARD_PASSWORD حداقل ۱۲ کاراکتر و BRIDGE_TOKEN تصادفی حداقل ۳۲ کاراکتر؛ تلگرام هم فقط در همین فایل.
2. اجرا:

    docker compose build api
    docker compose up -d api

3. پورت 5005 فقط loopback سرور است. reverse proxy با HTTPS، WebSocket و SSE قرار دهید.
4. اگر proxy از IP غیرloopback به container وصل می‌شود، فقط IP دقیق آن را در TRUSTED_PROXY قرار دهید.
5. proxy باید X-Forwarded-Proto صحیح بدهد. ورود production بدون HTTPS رد می‌شود؛ cookie امن است.
6. volume trader-data را backup کنید. SQLite، outbox، intents و کلیدهای نشست داخل آن هستند؛ data و backup باید خصوصی و دارای مجوز فایل محدود باشند.

کانتینر non-root، filesystem فقط‌خواندنی و بدون Docker socket mount است. Health check زنده‌بودن API را بررسی می‌کند؛ اتصال بروکر جدا در داشبورد دیده می‌شود.

## Agent ویندوزی

در .env میزبان ویندوز، token برابر سرور و URL دارای WSS باشد:

    BRIDGE_TOKEN=<server-token>
    Bridge__BackendUrl=wss://your-domain/bridge/ws
    Bridge__LocalPort=8766

Agent را با dotnet run --project Bridge --no-launch-profile اجرا کنید. پورت TCP متاتریدر فقط loopback است؛ آن را روی اینترنت باز نکنید. ارتباط از Agent شروع می‌شود و TLS اعتبارسنجی معمول دارد.

پس از ری‌استارت، ورود جدید همیشه متوقف است؛ اول بازیابی حساب، معاملات و سفارش‌های نامشخص را بررسی کنید.

.env، data، artifacts و secrets از build context خارج‌اند. Bot Token/PAT قدیمی که در Git بوده باید توسط مالک تعویض شوند؛ این تغییرات تاریخچه را پاک نمی‌کنند.
