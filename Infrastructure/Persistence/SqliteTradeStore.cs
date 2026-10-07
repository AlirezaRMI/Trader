using System.Text.Json;
using Application.Runtime;
using Microsoft.Data.Sqlite;

namespace Infrastructure.Persistence;

public sealed class SqliteTradeStore : ITradeStore
{
    private readonly object _sync = new();
    private readonly SqliteConnection _connection;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public SqliteTradeStore(string path)
    {
        var fullPath = Path.GetFullPath(path);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        _connection = new(new SqliteConnectionStringBuilder { DataSource = fullPath, Pooling = false, DefaultTimeout = 5 }.ToString());
        _connection.Open();
        using var command = Command("""
            PRAGMA journal_mode=WAL;
            PRAGMA synchronous=FULL;
            PRAGMA busy_timeout=5000;
            CREATE TABLE IF NOT EXISTS settings (id INTEGER PRIMARY KEY CHECK(id=1), json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS intents (
                id TEXT PRIMARY KEY, account_id INTEGER NOT NULL, symbol TEXT NOT NULL,
                candle_time INTEGER NOT NULL, side TEXT NOT NULL, created_at TEXT NOT NULL,
                status TEXT NOT NULL, ticket INTEGER, detail TEXT, confirmed_at TEXT,
                UNIQUE(account_id, symbol, candle_time));
            CREATE TABLE IF NOT EXISTS journal (
                id INTEGER PRIMARY KEY AUTOINCREMENT, timestamp TEXT NOT NULL, level TEXT NOT NULL,
                event_type TEXT NOT NULL, message TEXT NOT NULL, symbol TEXT, detail TEXT);
            CREATE TABLE IF NOT EXISTS positions (account_id INTEGER PRIMARY KEY, json TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS notifications (
                id INTEGER PRIMARY KEY AUTOINCREMENT, text TEXT NOT NULL, attempts INTEGER NOT NULL DEFAULT 0,
                delivered INTEGER NOT NULL DEFAULT 0, error TEXT, retry_at TEXT);
            CREATE TABLE IF NOT EXISTS notification_receipts (
                notification_id INTEGER NOT NULL, recipient TEXT NOT NULL,
                PRIMARY KEY(notification_id, recipient));
            CREATE TABLE IF NOT EXISTS notification_channels (channel TEXT PRIMARY KEY, retry_at TEXT NOT NULL);
            CREATE TABLE IF NOT EXISTS broker_positions (
                account_id INTEGER NOT NULL, broker_server TEXT NOT NULL, json TEXT NOT NULL,
                PRIMARY KEY(account_id,broker_server));
            CREATE TABLE IF NOT EXISTS closures (
                account_id INTEGER NOT NULL, broker_server TEXT NOT NULL, ticket INTEGER NOT NULL,
                json TEXT NOT NULL, profit REAL NOT NULL, closed_at INTEGER NOT NULL,
                PRIMARY KEY(account_id,broker_server,ticket));
            CREATE TABLE IF NOT EXISTS trade_contexts (
                account_id INTEGER NOT NULL, broker_server TEXT NOT NULL, ticket INTEGER NOT NULL,
                json TEXT NOT NULL, PRIMARY KEY(account_id,broker_server,ticket));
            CREATE INDEX IF NOT EXISTS ix_closures_history ON closures(closed_at DESC,ticket DESC,account_id DESC,broker_server DESC);
            CREATE INDEX IF NOT EXISTS ix_intents_account_status ON intents(account_id, status);
            """);
        command.ExecuteNonQuery();
        using var notificationColumns = Command("SELECT COUNT(*) FROM pragma_table_info('notifications') WHERE name='retry_at'");
        if (Convert.ToInt32(notificationColumns.ExecuteScalar()) == 0)
        {
            using var notificationMigration = Command("ALTER TABLE notifications ADD COLUMN retry_at TEXT");
            notificationMigration.ExecuteNonQuery();
        }
        using var notificationIndex = Command("CREATE INDEX IF NOT EXISTS ix_notifications_pending ON notifications(delivered,retry_at,id)");
        notificationIndex.ExecuteNonQuery();
        using var columns = Command("SELECT COUNT(*) FROM pragma_table_info('intents') WHERE name='broker_server'");
        if (Convert.ToInt32(columns.ExecuteScalar()) == 0)
        {
            using var migration = Command("""
                BEGIN IMMEDIATE;
                CREATE TABLE intents_v2 (
                    id TEXT PRIMARY KEY, account_id INTEGER NOT NULL, broker_server TEXT NOT NULL DEFAULT '',
                    symbol TEXT NOT NULL, candle_time INTEGER NOT NULL, side TEXT NOT NULL, created_at TEXT NOT NULL,
                    status TEXT NOT NULL, ticket INTEGER, detail TEXT, confirmed_at TEXT,
                    UNIQUE(account_id,broker_server,symbol,candle_time));
                INSERT INTO intents_v2(id,account_id,symbol,candle_time,side,created_at,status,ticket,detail,confirmed_at)
                    SELECT id,account_id,symbol,candle_time,side,created_at,status,ticket,
                        CASE WHEN status='Confirmed' THEN 'Legacy confirmed order awaits broker identity proof' ELSE detail END,
                        confirmed_at FROM intents;
                DROP TABLE intents;
                ALTER TABLE intents_v2 RENAME TO intents;
                CREATE INDEX ix_intents_account_status ON intents(account_id,broker_server,status);
                INSERT OR IGNORE INTO broker_positions SELECT account_id,'',json FROM positions;
                PRAGMA user_version=2;
                COMMIT;
                """);
            migration.ExecuteNonQuery();
        }
    }

    public RuntimeSettings LoadSettings()
    {
        lock (_sync)
        {
            using var command = Command("SELECT json FROM settings WHERE id=1");
            if (command.ExecuteScalar() is not string json) return new();
            var settings = JsonSerializer.Deserialize<RuntimeSettings>(json, JsonOptions) ?? new();
            using var document = JsonDocument.Parse(json);
            var strategyJson = document.RootElement.ValueKind == JsonValueKind.Object
                ? document.RootElement.EnumerateObject().FirstOrDefault(p => p.Name.Equals("strategy", StringComparison.OrdinalIgnoreCase)).Value
                : default;
            if (settings.Strategy is {Mode: "Auto", MinimumEntryAdx: >= 45 and <= 100} strategy &&
                strategyJson.ValueKind == JsonValueKind.Object && !strategyJson.EnumerateObject().Any(p => p.Name.Equals("autoSwitchAdx", StringComparison.OrdinalIgnoreCase)))
            {
                // Read-time compatibility only: retain every old entry/risk threshold and leave execution paused.
                // At the mathematical ADX ceiling of 100, only the indicator boundary is reachable.
                settings = settings with {Strategy = strategy with {AutoSwitchAdx = Math.Min(100, strategy.MinimumEntryAdx + 1)}};
            }
            return settings;
        }
    }

    public JournalEntry? ReadLatestJournal(string eventType, long accountId, string server)
    {
        lock (_sync)
        {
            using var command = Command("""
                SELECT id,timestamp,level,event_type,message,symbol,detail FROM journal
                WHERE event_type=$event AND json_valid(detail)=1
                  AND json_extract(detail,'$.accountId')=$account
                  AND json_extract(detail,'$.brokerServer')=$server
                ORDER BY id DESC LIMIT 1
                """, ("$event", eventType), ("$account", accountId), ("$server", server));
            using var reader = command.ExecuteReader();
            return reader.Read() ? new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
                reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5), reader.IsDBNull(6) ? null : reader.GetString(6)) : null;
        }
    }

    public void SaveSettings(RuntimeSettings settings)
    {
        if (SettingsValidator.Validate(settings) is { } error) throw new ArgumentException(error);
        lock (_sync)
        {
            using var command = Command("INSERT INTO settings(id,json) VALUES(1,$json) ON CONFLICT(id) DO UPDATE SET json=excluded.json",
                ("$json", JsonSerializer.Serialize(settings, JsonOptions)));
            command.ExecuteNonQuery();
        }
    }

    public bool TryCreateIntent(TradeIntent intent, int dailyLimit)
    {
        if (intent.Id == Guid.Empty || intent.AccountId <= 0 || dailyLimit <= 0 || intent.CandleTime <= 0 ||
            string.IsNullOrWhiteSpace(intent.Symbol) || intent.Side is not ("Buy" or "Sell"))
            throw new ArgumentException("Invalid execution intent");
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            using var count = Command("""
                SELECT COUNT(*) FROM intents WHERE account_id=$account AND (broker_server=$server OR broker_server='') AND
                (status IN ('Pending','Unknown') OR (status='Confirmed' AND confirmed_at >= $day AND confirmed_at < $next));
                """, ("$account", intent.AccountId), ("$server", intent.BrokerServer), ("$day", Day(intent.CreatedAt)), ("$next", Day(intent.CreatedAt.AddDays(1))));
            count.Transaction = transaction;
            if (Convert.ToInt32(count.ExecuteScalar()) >= dailyLimit) return false;
            using var insert = Command("""
                INSERT OR IGNORE INTO intents(id,account_id,broker_server,symbol,candle_time,side,created_at,status)
                VALUES($id,$account,$server,$symbol,$candle,$side,$created,'Pending');
                """, ("$id", intent.Id.ToString("N")), ("$account", intent.AccountId), ("$server", intent.BrokerServer), ("$symbol", intent.Symbol),
                ("$candle", intent.CandleTime), ("$side", intent.Side), ("$created", Timestamp(intent.CreatedAt)));
            insert.Transaction = transaction;
            var inserted = insert.ExecuteNonQuery() == 1;
            transaction.Commit();
            return inserted;
        }
    }

    public void SetOutcome(Guid id, string status, long? ticket, string? detail)
    {
        if (status is not ("Confirmed" or "Failed" or "Unknown") || (status == "Confirmed" && ticket is not > 0))
            throw new ArgumentException("Invalid execution outcome");
        lock (_sync)
        {
            using var command = Command("""
                UPDATE intents SET status=$status,ticket=$ticket,detail=$detail,
                    confirmed_at=CASE WHEN $status='Confirmed' THEN COALESCE(confirmed_at,$now) ELSE confirmed_at END
                WHERE id=$id AND status IN ('Pending','Unknown');
                """, ("$status", status), ("$ticket", ticket), ("$detail", detail),
                ("$now", Timestamp(DateTimeOffset.UtcNow)), ("$id", id.ToString("N")));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<TradeIntent> Unresolved(long accountId, string server = "")
    {
        lock (_sync)
        {
            using var command = Command("""
                SELECT id,account_id,symbol,candle_time,side,created_at,status,ticket,detail,broker_server
                FROM intents WHERE account_id=$account AND (broker_server=$server OR broker_server='') AND
                    (status IN ('Pending','Unknown') OR (status='Confirmed' AND broker_server='')) ORDER BY created_at;
                """, ("$account", accountId), ("$server", server));
            using var reader = command.ExecuteReader();
            var result = new List<TradeIntent>();
            while (reader.Read())
                result.Add(new(Guid.ParseExact(reader.GetString(0), "N"), reader.GetInt64(1), reader.GetString(2),
                    reader.GetInt64(3), reader.GetString(4), DateTimeOffset.Parse(reader.GetString(5)),
                    reader.GetString(6), reader.IsDBNull(7) ? null : reader.GetInt64(7),
                    reader.IsDBNull(8) ? null : reader.GetString(8)) { BrokerServer = reader.GetString(9) });
            return result;
        }
    }

    public bool TryBindLegacyConfirmed(Guid id, string server, long ticket)
    {
        if (string.IsNullOrWhiteSpace(server) || ticket <= 0) throw new ArgumentException("Proven broker identity is required");
        lock (_sync)
        {
            using var command = Command("""
                UPDATE intents AS legacy SET broker_server=$server,detail='Legacy order broker identity verified'
                WHERE id=$id AND broker_server='' AND status='Confirmed' AND ticket=$ticket
                    AND NOT EXISTS(SELECT 1 FROM intents other WHERE other.id<>legacy.id
                        AND other.account_id=legacy.account_id AND other.broker_server=$server
                        AND other.symbol=legacy.symbol AND other.candle_time=legacy.candle_time);
                """, ("$id", id.ToString("N")), ("$server", server), ("$ticket", ticket));
            return command.ExecuteNonQuery() == 1;
        }
    }

    public int ConfirmedToday(long accountId, DateTimeOffset now, string server = "")
    {
        lock (_sync)
        {
            using var command = Command("SELECT COUNT(*) FROM intents WHERE account_id=$account AND (broker_server=$server OR broker_server='') AND status='Confirmed' AND confirmed_at >= $day AND confirmed_at < $next",
                ("$account", accountId), ("$server", server), ("$day", Day(now)), ("$next", Day(now.AddDays(1))));
            return Convert.ToInt32(command.ExecuteScalar());
        }
    }

    public long LastConfirmedSignal(long accountId, string symbol, string server = "")
    {
        lock (_sync)
        {
            using var command = Command("SELECT COALESCE(MAX(candle_time),0) FROM intents WHERE account_id=$account AND (broker_server=$server OR broker_server='') AND symbol=$symbol AND status='Confirmed'",
                ("$account", accountId), ("$server", server), ("$symbol", symbol));
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    public JournalEntry AppendJournal(string level, string eventType, string message, string? symbol = null, string? detail = null)
    {
        lock (_sync)
        {
            var now = DateTimeOffset.UtcNow;
            using var command = Command("""
                INSERT INTO journal(timestamp,level,event_type,message,symbol,detail) VALUES($time,$level,$event,$message,$symbol,$detail);
                SELECT last_insert_rowid();
                """, ("$time", Timestamp(now)), ("$level", level), ("$event", eventType),
                ("$message", message), ("$symbol", symbol), ("$detail", detail));
            var id = Convert.ToInt64(command.ExecuteScalar());
            return new(id, now, level, eventType, message, symbol, detail);
        }
    }

    public IReadOnlyList<JournalEntry> ReadJournal(int limit = 200)
    {
        lock (_sync)
        {
            using var command = Command("SELECT id,timestamp,level,event_type,message,symbol,detail FROM journal ORDER BY id DESC LIMIT $limit",
                ("$limit", Math.Clamp(limit, 1, 1000)));
            using var reader = command.ExecuteReader();
            var result = new List<JournalEntry>();
            while (reader.Read())
                result.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)), reader.GetString(2),
                    reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                    reader.IsDBNull(6) ? null : reader.GetString(6)));
            return result;
        }
    }

    public IReadOnlyList<BrokerPosition> ReadPositions(long accountId, string server = "")
    {
        lock (_sync)
        {
            using var command = Command("SELECT json FROM broker_positions WHERE account_id=$account AND broker_server=$server", ("$account", accountId), ("$server", server));
            return command.ExecuteScalar() is string json
                ? JsonSerializer.Deserialize<List<BrokerPosition>>(json, JsonOptions) ?? [] : [];
        }
    }

    public void SavePositions(long accountId, IReadOnlyList<BrokerPosition> positions, string server = "")
    {
        lock (_sync)
        {
            using var command = Command("""
                INSERT INTO broker_positions(account_id,broker_server,json) VALUES($account,$server,$json)
                ON CONFLICT(account_id,broker_server) DO UPDATE SET json=excluded.json;
                """, ("$account", accountId), ("$server", server), ("$json", JsonSerializer.Serialize(positions, JsonOptions)));
            command.ExecuteNonQuery();
        }
    }

    public IReadOnlyList<(long Ticket, string Symbol)> OutstandingTickets(long accountId, string server)
    {
        lock (_sync)
        {
            using var command = Command("""
                SELECT i.ticket,i.symbol FROM intents i WHERE i.account_id=$account AND i.broker_server=$server
                AND i.status='Confirmed' AND NOT EXISTS(SELECT 1 FROM closures c
                    WHERE c.account_id=i.account_id AND c.broker_server=i.broker_server AND c.ticket=i.ticket);
                """, ("$account", accountId), ("$server", server));
            using var reader = command.ExecuteReader();
            var result = new List<(long, string)>();
            while (reader.Read()) result.Add((reader.GetInt64(0), reader.GetString(1)));
            return result;
        }
    }

    public bool RecordClosure(long accountId, string server, BrokerPosition position, string currency, string? notification)
    {
        if (!BrokerPositionValidation.IsValid(position, closed: true))
            throw new ArgumentException("Invalid confirmed closure");
        lock (_sync)
        {
            using var transaction = _connection.BeginTransaction();
            var json = JsonSerializer.Serialize(position, JsonOptions);
            using var insert = Command("""
                INSERT OR IGNORE INTO closures(account_id,broker_server,ticket,json,profit,closed_at)
                VALUES($account,$server,$ticket,$json,$profit,$closed);
                """, ("$account", accountId), ("$server", server), ("$ticket", position.Ticket),
                ("$json", json), ("$profit", position.Profit), ("$closed", position.CloseTime));
            insert.Transaction = transaction;
            if (insert.ExecuteNonQuery() == 0)
            {
                // A newer EA can enrich legacy records with actual exit price and fee breakdown.
                // Missing optional fields from an older EA must not erase previously confirmed data.
                using var existing = Command("SELECT json FROM closures WHERE account_id=$account AND broker_server=$server AND ticket=$ticket",
                    ("$account", accountId), ("$server", server), ("$ticket", position.Ticket));
                existing.Transaction = transaction;
                var old = JsonSerializer.Deserialize<BrokerPosition>((string)existing.ExecuteScalar()!, JsonOptions)!;
                if (old.Symbol != position.Symbol || old.Side != position.Side || old.CloseTime != position.CloseTime)
                    throw new ArgumentException("Conflicting broker closure identity");
                var enriched = old with
                {
                    ClosePrice = position.ClosePrice ?? old.ClosePrice,
                    GrossProfit = position.GrossProfit ?? old.GrossProfit,
                    Commission = position.Commission ?? old.Commission,
                    Swap = position.Swap ?? old.Swap,
                    PriceDigits = position.PriceDigits ?? old.PriceDigits
                };
                if (enriched != old)
                {
                    using var update = Command("UPDATE closures SET json=$json WHERE account_id=$account AND broker_server=$server AND ticket=$ticket",
                        ("$json", JsonSerializer.Serialize(enriched, JsonOptions)), ("$account", accountId),
                        ("$server", server), ("$ticket", position.Ticket));
                    update.Transaction = transaction;
                    update.ExecuteNonQuery();
                }
                transaction.Commit();
                return false;
            }
            using var journal = Command("""
                INSERT INTO journal(timestamp,level,event_type,message,symbol,detail)
                VALUES($time,'Information','TradeClosed',$message,$symbol,$json);
                """, ("$time", Timestamp(DateTimeOffset.UtcNow)), ("$message", string.Create(System.Globalization.CultureInfo.InvariantCulture,
                    $"ثبت بسته‌شدن معامله #{position.Ticket}؛ سود/زیان خالص {position.Profit:G10} {currency}")),
                ("$symbol", position.Symbol), ("$json", JsonSerializer.Serialize(new
                {accountId, brokerServer = server, currency, ticket = position.Ticket, position}, JsonOptions)));
            journal.Transaction = transaction;
            journal.ExecuteNonQuery();
            if (notification is not null)
            {
                using var outbox = Command("INSERT INTO notifications(text) VALUES($text)", ("$text", notification));
                outbox.Transaction = transaction;
                outbox.ExecuteNonQuery();
            }
            transaction.Commit();
            return true;
        }
    }

    public void RecordTradeContext(TradeContext context)
    {
        if (context.AccountId <= 0 || context.Ticket <= 0 || string.IsNullOrWhiteSpace(context.BrokerServer))
            throw new ArgumentException("Invalid trade context");
        lock (_sync)
        {
            using var command = Command("""
                INSERT OR IGNORE INTO trade_contexts(account_id,broker_server,ticket,json) VALUES($account,$server,$ticket,$json)
                """, ("$account", context.AccountId), ("$server", context.BrokerServer), ("$ticket", context.Ticket),
                ("$json", JsonSerializer.Serialize(context, JsonOptions)));
            command.ExecuteNonQuery();
        }
    }

    private sealed record HistoryCursor(long ClosedAt, long Ticket, long AccountId, string BrokerServer);

    public TradeHistoryPage ReadTradeHistory(int limit = 50, string? cursor = null)
    {
        limit = Math.Clamp(limit, 1, 100);
        HistoryCursor? before = null;
        if (!string.IsNullOrEmpty(cursor))
        {
            try
            {
                if (cursor.Length > 2048) throw new FormatException();
                before = JsonSerializer.Deserialize<HistoryCursor>(Convert.FromBase64String(cursor), JsonOptions);
                if (before is null || before.ClosedAt <= 0 || before.Ticket <= 0 || before.AccountId <= 0 ||
                    before.BrokerServer is null || before.BrokerServer.Length > 512) throw new FormatException();
            }
            catch (Exception error) when (error is FormatException or JsonException)
            { throw new ArgumentException("Invalid history cursor"); }
        }
        lock (_sync)
        {
            using var command = Command("""
                SELECT c.account_id,c.broker_server,c.json,t.json FROM closures c
                LEFT JOIN trade_contexts t ON t.account_id=c.account_id AND t.broker_server=c.broker_server AND t.ticket=c.ticket
                WHERE $time IS NULL OR (c.closed_at,c.ticket,c.account_id,c.broker_server) < ($time,$ticket,$account,$server)
                ORDER BY c.closed_at DESC,c.ticket DESC,c.account_id DESC,c.broker_server DESC LIMIT $limit
                """, ("$time", before?.ClosedAt), ("$ticket", before?.Ticket), ("$account", before?.AccountId),
                ("$server", before?.BrokerServer), ("$limit", limit + 1));
            var items = new List<ClosedTrade>();
            using (var reader = command.ExecuteReader())
                while (reader.Read())
                    items.Add(new(reader.GetInt64(0), reader.GetString(1),
                        JsonSerializer.Deserialize<BrokerPosition>(reader.GetString(2), JsonOptions)!,
                        reader.IsDBNull(3) ? null : JsonSerializer.Deserialize<TradeContext>(reader.GetString(3), JsonOptions)));
            string? next = null;
            if (items.Count > limit)
            {
                items.RemoveAt(items.Count - 1);
                var last = items[^1];
                next = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(
                    new HistoryCursor(last.Position.CloseTime, last.Position.Ticket, last.AccountId, last.BrokerServer), JsonOptions));
            }

            using var summary = Command("""
                SELECT c.account_id,c.broker_server,json_extract(t.json,'$.currency'),json_extract(t.json,'$.isDemo'),
                    COUNT(*),SUM(CASE WHEN c.profit>0 THEN 1 ELSE 0 END),SUM(CASE WHEN c.profit<0 THEN 1 ELSE 0 END),SUM(c.profit)
                FROM closures c LEFT JOIN trade_contexts t
                    ON t.account_id=c.account_id AND t.broker_server=c.broker_server AND t.ticket=c.ticket
                GROUP BY c.account_id,c.broker_server,json_extract(t.json,'$.currency'),json_extract(t.json,'$.isDemo')
                ORDER BY c.account_id,c.broker_server
                """);
            var accounts = new List<TradeAccountPerformance>();
            using var totals = summary.ExecuteReader();
            while (totals.Read()) accounts.Add(new(totals.GetInt64(0), totals.GetString(1),
                totals.IsDBNull(2) ? null : totals.GetString(2), totals.IsDBNull(3) ? null : totals.GetInt32(3) != 0,
                totals.GetInt32(4), totals.GetInt32(5), totals.GetInt32(6), totals.GetDouble(7)));
            return new(items, next, accounts);
        }
    }

    public JournalPage ReadJournalPage(int limit = 200, long? before = null)
    {
        limit = Math.Clamp(limit, 1, 200);
        if (before <= 0) throw new ArgumentException("Invalid journal cursor");
        lock (_sync)
        {
            using var command = Command("""
                SELECT id,timestamp,level,event_type,message,symbol,detail FROM journal
                WHERE $before IS NULL OR id < $before ORDER BY id DESC LIMIT $limit
                """, ("$before", before), ("$limit", limit + 1));
            var items = new List<JournalEntry>();
            using var reader = command.ExecuteReader();
            while (reader.Read()) items.Add(new(reader.GetInt64(0), DateTimeOffset.Parse(reader.GetString(1)),
                reader.GetString(2), reader.GetString(3), reader.GetString(4), reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6)));
            var more = items.Count > limit;
            if (more) items.RemoveAt(items.Count - 1);
            return new(items, more ? items[^1].Id : null);
        }
    }

    public long EnqueueNotification(string text)
    {
        lock (_sync)
        {
            using var command = Command("INSERT INTO notifications(text) VALUES($text); SELECT last_insert_rowid();", ("$text", text));
            return Convert.ToInt64(command.ExecuteScalar());
        }
    }

    public IReadOnlyList<(long Id, string Text, int Attempts)> PendingNotifications(int limit = 10)
    {
        lock (_sync)
        {
            using var command = Command("""
                SELECT id,text,attempts FROM notifications
                WHERE delivered=0 AND (retry_at IS NULL OR retry_at <= $now) ORDER BY id LIMIT $limit
                """, ("$now", Timestamp(DateTimeOffset.UtcNow)), ("$limit", Math.Clamp(limit, 1, 50)));
            using var reader = command.ExecuteReader();
            var result = new List<(long, string, int)>();
            while (reader.Read()) result.Add((reader.GetInt64(0), reader.GetString(1), reader.GetInt32(2)));
            return result;
        }
    }

    public void MarkNotification(long id, bool delivered, string? error)
    {
        lock (_sync)
        {
            using var command = Command("UPDATE notifications SET delivered=$delivered,error=$error,retry_at=NULL,attempts=attempts+1 WHERE id=$id AND delivered=0",
                ("$delivered", delivered ? 1 : 0), ("$error", error), ("$id", id));
            command.ExecuteNonQuery();
        }
    }

    public int NotificationPendingCount()
    {
        lock (_sync)
        {
            using var command = Command("SELECT COUNT(*) FROM notifications WHERE delivered=0");
            return (int)Math.Min(int.MaxValue, Convert.ToInt64(command.ExecuteScalar()));
        }
    }

    public void DeferNotification(long id, string error, DateTimeOffset retryAt)
    {
        lock (_sync)
        {
            using var command = Command("""
                UPDATE notifications SET error=$error,retry_at=$retry,attempts=attempts+1
                WHERE id=$id AND delivered=0
                """, ("$error", error), ("$retry", Timestamp(retryAt)), ("$id", id));
            command.ExecuteNonQuery();
        }
    }

    public DateTimeOffset NotificationChannelRetryAt(string channel)
    {
        lock (_sync)
        {
            using var command = Command("SELECT retry_at FROM notification_channels WHERE channel=$channel", ("$channel", channel));
            return command.ExecuteScalar() is string value ? DateTimeOffset.Parse(value,
                System.Globalization.CultureInfo.InvariantCulture) : DateTimeOffset.MinValue;
        }
    }

    public void DeferNotificationChannel(string channel, DateTimeOffset retryAt)
    {
        lock (_sync)
        {
            using var command = Command("""
                INSERT INTO notification_channels(channel,retry_at) VALUES($channel,$retry)
                ON CONFLICT(channel) DO UPDATE SET retry_at=MAX(notification_channels.retry_at,excluded.retry_at)
                """, ("$channel", channel), ("$retry", Timestamp(retryAt)));
            command.ExecuteNonQuery();
        }
    }

    public bool RecipientDelivered(long id, string recipient)
    {
        lock (_sync)
        {
            using var command = Command("SELECT COUNT(*) FROM notification_receipts WHERE notification_id=$id AND recipient=$recipient",
                ("$id", id), ("$recipient", recipient));
            return Convert.ToInt32(command.ExecuteScalar()) != 0;
        }
    }

    public void MarkRecipientDelivered(long id, string recipient)
    {
        lock (_sync)
        {
            using var command = Command("INSERT OR IGNORE INTO notification_receipts(notification_id,recipient) VALUES($id,$recipient)",
                ("$id", id), ("$recipient", recipient));
            command.ExecuteNonQuery();
        }
    }

    private SqliteCommand Command(string sql, params (string Name, object? Value)[] parameters)
    {
        var command = _connection.CreateCommand();
        command.CommandText = sql;
        foreach (var parameter in parameters) command.Parameters.AddWithValue(parameter.Name, parameter.Value ?? DBNull.Value);
        return command;
    }
    private static string Timestamp(DateTimeOffset time) => time.UtcDateTime.ToString("O", System.Globalization.CultureInfo.InvariantCulture);
    private static string Day(DateTimeOffset time) => Timestamp(new DateTimeOffset(time.UtcDateTime.Date, TimeSpan.Zero));
    public void Dispose() { lock (_sync) _connection.Dispose(); }
}
