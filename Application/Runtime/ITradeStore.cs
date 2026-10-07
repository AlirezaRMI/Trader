namespace Application.Runtime;

public interface ITradeStore : IDisposable
{
    RuntimeSettings LoadSettings();
    void SaveSettings(RuntimeSettings settings);
    bool TryCreateIntent(TradeIntent intent, int dailyLimit);
    void SetOutcome(Guid id, string status, long? ticket, string? detail);
    bool TryBindLegacyConfirmed(Guid id, string server, long ticket);
    IReadOnlyList<TradeIntent> Unresolved(long accountId, string server = "");
    int ConfirmedToday(long accountId, DateTimeOffset now, string server = "");
    long LastConfirmedSignal(long accountId, string symbol, string server = "");
    IReadOnlyList<(long Ticket, string Symbol)> OutstandingTickets(long accountId, string server);
    bool RecordClosure(long accountId, string server, BrokerPosition position, string currency, string? notification);
    void RecordTradeContext(TradeContext context);
    TradeHistoryPage ReadTradeHistory(int limit = 50, string? cursor = null);
    JournalPage ReadJournalPage(int limit = 200, long? before = null);
    JournalEntry AppendJournal(string level, string eventType, string message, string? symbol = null, string? detail = null);
    IReadOnlyList<JournalEntry> ReadJournal(int limit = 200);
    JournalEntry? ReadLatestJournal(string eventType, long accountId, string server);
    IReadOnlyList<BrokerPosition> ReadPositions(long accountId, string server = "");
    void SavePositions(long accountId, IReadOnlyList<BrokerPosition> positions, string server = "");
    long EnqueueNotification(string text);
    IReadOnlyList<(long Id, string Text, int Attempts)> PendingNotifications(int limit = 10);
    void MarkNotification(long id, bool delivered, string? error);
}
