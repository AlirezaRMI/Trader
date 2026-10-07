// Trader Bridge EA 3.10 — MT4 Windows adapter. See docs/mt4-bridge.md.
#property strict
#property version "3.10"
#include <stdlib.mqh>

#import "ws2_32.dll"
int WSAStartup(int version, uchar &data[]);
int WSACleanup();
int WSAGetLastError();
int socket(int family, int type, int protocol);
int ioctlsocket(int handle, int command, int &value[]);
int connect(int handle, uchar &address[], int length);
int getpeername(int handle, uchar &address[], int &length[]);
int recv(int handle, uchar &buffer[], int length, int flags);
int send(int handle, uchar &buffer[], int length, int flags);
int closesocket(int handle);
#import

input int AgentPort = 8766;
input int MagicNumber = 12345;
input bool EnableOrders = false;
input bool AllowLiveAccount = false;
input bool EnableTrailing = true;
input double TrailingAtrMultiplier = 1.2;
input double TrailingActivationAtr = 1.0;
input int TrailingAtrPeriod = 14;
input int TrailingHistoryBars = 500; // Same bounded Wilder seed/window as C# IndicatorCalculator.
input double TrailingStepPoints = 2;
input int TrailingTimeframe = PERIOD_M5;
input double CommissionPerLot = 0;
input int SpreadLookbackSamples = 50;
input double SpreadSpikeMultiplier = 2.0;
input int SpreadSamplingSeconds = 30;
input double DailyDrawdownLimitPercent = 3.0;
input double TotalDrawdownLimitPercent = 10.0;
input int DrawdownCooldownHours = 24;
input bool CloseOwnedOnMaxDrawdown = false; // Explicit opt-in; never closes another MagicNumber.
input int MaximumSpreadPoints = 30;
input int SlippagePoints = 10;
input double MaximumRiskPercent = 1.0;
input double MaximumRiskAmount = 5.0;
input int MaximumHistoryLookup = 2000;
input int MaximumQuoteAgeSeconds = 120;

#define MAX_FRAME 1048576
#define WOULD_BLOCK 10035
int handle = -1;
bool connected = false;
bool winsockStarted = false;
uint retryAt = 0;
uint connectStarted = 0;
uint trailAt = 0;
datetime lastTickLocal = 0;
uchar incoming[];
int incomingCount = 0;
uchar outgoing[];
int outgoingOffset = 0;
string cachedIds[64];
string cachedReplies[64];
string cachedCommands[64];
int cacheCursor = 0;
string terminalSession = "";
bool contextBound = false;
string cacheAccount = "";
string riskPrefix = "";
string sampledSymbol = "";
double observedSpreads[1000];
int spreadCount = 0, spreadCursor = 0;
datetime spreadSampleAt = 0;
int trailingTickets[256];
datetime trailingBars[256];
int trailingCursor = 0;

bool SaveRiskValue(string key, double value)
{
   return GlobalVariableSet(riskPrefix + key, value) > 0;
}
string CurrentRiskPrefix()
{
   uint serverHash = 5381;
   string brokerServer = AccountServer();
   for(int h = 0; h < StringLen(brokerServer); h++) serverHash = (serverHash * 33) ^ StringGetCharacter(brokerServer, h);
   return "TRRisk-" + IntegerToString(AccountNumber()) + "-" + IntegerToString(MagicNumber) + "-" + StringFormat("%u", serverHash) + "-";
}
bool EquityRiskBlocked()
{
   riskPrefix = CurrentRiskPrefix();
   double equity = AccountEquity();
   if(!MathIsValidNumber(equity)) return true;
   datetime now = TimeGMT();
   if(now <= 0) return true;
   double day = MathFloor((double)now / 86400.0);
   double peak = GlobalVariableCheck(riskPrefix + "peak") ? GlobalVariableGet(riskPrefix + "peak") : equity;
   double dailyPeak = GlobalVariableCheck(riskPrefix + "daily") ? GlobalVariableGet(riskPrefix + "daily") : equity;
   bool newDay = !GlobalVariableCheck(riskPrefix + "day") || GlobalVariableGet(riskPrefix + "day") != day;
   if(newDay) dailyPeak = MathMax(equity, 0.00000001);
   peak = MathMax(peak, equity); dailyPeak = MathMax(dailyPeak, equity);
   if(peak <= 0 || !SaveRiskValue("peak", peak) || !SaveRiskValue("daily", dailyPeak) || !SaveRiskValue("day", day)) return true;
   double until = GlobalVariableCheck(riskPrefix + "until") ? GlobalVariableGet(riskPrefix + "until") : 0;
   bool halted = GlobalVariableCheck(riskPrefix + "halt") && GlobalVariableGet(riskPrefix + "halt") != 0;
   bool totalBreach = 100.0 * (peak - equity) / peak >= TotalDrawdownLimitPercent;
   bool dailyBreach = 100.0 * (dailyPeak - equity) / dailyPeak >= DailyDrawdownLimitPercent;
   if(totalBreach && !halted)
   {
      if(!SaveRiskValue("halt", 1)) return true;
      halted = true; GlobalVariablesFlush();
      Print("Risk: maximum equity drawdown latched; new entries halted.");
   }
   if(dailyBreach && until <= now)
   {
      until = (double)now + DrawdownCooldownHours * 3600;
      if(!SaveRiskValue("until", until)) return true;
      GlobalVariablesFlush(); Print("Risk: daily equity drawdown cooldown started.");
   }
   return halted || until > now;
}
void SampleSpread(string symbol)
{
   if(sampledSymbol != symbol) {sampledSymbol = symbol; spreadCount = 0; spreadCursor = 0; spreadSampleAt = 0;}
   datetime now = TimeLocal();
   if(now < spreadSampleAt + SpreadSamplingSeconds) return;
   double point = MarketInfo(symbol, MODE_POINT), bid = MarketInfo(symbol, MODE_BID), ask = MarketInfo(symbol, MODE_ASK);
   if(point <= 0 || bid <= 0 || ask < bid || TimeCurrent() - MarketInfo(symbol, MODE_TIME) > MaximumQuoteAgeSeconds) return;
   double value = (ask - bid) / point;
   if(!MathIsValidNumber(value)) return;
   observedSpreads[spreadCursor] = value;
   spreadCursor = (spreadCursor + 1) % SpreadLookbackSamples;
   spreadCount = MathMin(spreadCount + 1, SpreadLookbackSamples); spreadSampleAt = now;
}
bool SpreadAllowed(string symbol, double spread)
{
   if(spread > MaximumSpreadPoints) return false;
   if(sampledSymbol != symbol || spreadCount < SpreadLookbackSamples) return true;
   double total = 0;
   for(int i = 0; i < spreadCount; i++) total += observedSpreads[i];
   return spread <= total / spreadCount * SpreadSpikeMultiplier;
}

string J(string value)
{
   StringReplace(value, "\\", "\\\\");
   StringReplace(value, "\"", "\\\"");
   StringReplace(value, "\r", "\\r");
   StringReplace(value, "\n", "\\n");
   StringReplace(value, "\t", "\\t");
   return "\"" + value + "\"";
}
string N(double value, int digits = 8)
{
   if(!MathIsValidNumber(value)) return "null";
   return DoubleToString(value, digits);
}
string B(bool value) { return value ? "true" : "false"; }
string ContextJson()
{
   return "{\"accountId\":" + IntegerToString(AccountNumber()) + ",\"server\":" + J(AccountServer()) +
          ",\"terminalSession\":" + J(terminalSession) + "}";
}
string Ok(string id, string json)
{
   if(contextBound) json = "{\"context\":" + ContextJson() + ",\"value\":" + json + "}";
   return "1|" + id + "|OK|" + json;
}
string Utf8Hex(string value)
{
   uchar bytes[]; int count = StringToCharArray(value, bytes, 0, WHOLE_ARRAY, CP_UTF8);
   string hex = "";
   for(int i = 0; i < count - 1; i++) hex += StringFormat("%02x", (int)bytes[i]);
   return hex;
}
string Fail(string id, string message, int code = 0, bool uncertain = false)
{
   return "1|" + id + "|ERROR|{\"message\":" + J(message) + ",\"code\":" +
          IntegerToString(code) + ",\"indeterminate\":" + B(uncertain) + "}";
}

void Disconnect()
{
   if(handle != -1) closesocket(handle);
   handle = -1;
   connected = false;
   incomingCount = 0;
   ArrayResize(incoming, 0);
   ArrayResize(outgoing, 0);
   outgoingOffset = 0;
   retryAt = GetTickCount() + 2000;
}
bool Due(uint now, uint deadline) { return (int)(now - deadline) >= 0; }

int OnInit()
{
   if(!IsDllsAllowed() || AgentPort < 1024 || AgentPort > 65535 || MagicNumber <= 0 ||
      !MathIsValidNumber(TrailingAtrMultiplier) || TrailingAtrMultiplier <= 0 ||
      !MathIsValidNumber(TrailingActivationAtr) || TrailingActivationAtr <= 0 || TrailingAtrPeriod < 2 || TrailingAtrPeriod > 100 ||
      TrailingHistoryBars <= TrailingAtrPeriod || TrailingHistoryBars > 1000 ||
      !MathIsValidNumber(TrailingStepPoints) || TrailingStepPoints < 0 || !ValidTimeframe(TrailingTimeframe) || CommissionPerLot < 0 || !MathIsValidNumber(CommissionPerLot) ||
      SpreadLookbackSamples < 2 || SpreadLookbackSamples > 1000 || SpreadSamplingSeconds < 1 ||
      !MathIsValidNumber(SpreadSpikeMultiplier) || SpreadSpikeMultiplier <= 0 || MaximumSpreadPoints <= 0 || SlippagePoints < 0 ||
      !MathIsValidNumber(DailyDrawdownLimitPercent) || DailyDrawdownLimitPercent <= 0 || DailyDrawdownLimitPercent >= 100 ||
      !MathIsValidNumber(TotalDrawdownLimitPercent) || TotalDrawdownLimitPercent <= 0 || TotalDrawdownLimitPercent >= 100 ||
      DrawdownCooldownHours < 1 || DrawdownCooldownHours > 720 || !MathIsValidNumber(MaximumRiskPercent) ||
      MaximumRiskPercent <= 0 || !MathIsValidNumber(MaximumRiskAmount) || MaximumRiskAmount <= 0 || MaximumHistoryLookup < 1)
   {
      Print("Bridge: invalid inputs, or DLL imports disabled.");
      return INIT_PARAMETERS_INCORRECT;
   }
   uchar data[512];
   if(WSAStartup(0x0202, data) != 0) return INIT_FAILED;
   winsockStarted = true;
   retryAt = GetTickCount();
   trailAt = GetTickCount();
   terminalSession = IntegerToString((int)TimeLocal()) + "-" + IntegerToString((int)GetTickCount()) + "-" + StringFormat("%I64d", ChartID());
   lastTickLocal = 0; // Do not treat attaching the EA as a fresh market tick.
   riskPrefix = CurrentRiskPrefix();
   sampledSymbol = Symbol();
   if(!EventSetMillisecondTimer(50))
   {
      WSACleanup(); winsockStarted = false;
      return INIT_FAILED;
   }
   Print("Trader Bridge initialized. Orders enabled: ", EnableOrders, ". Listener: 127.0.0.1:", AgentPort);
   return INIT_SUCCEEDED;
}
void OnDeinit(const int reason)
{
   EventKillTimer();
   Disconnect();
   if(winsockStarted) WSACleanup();
   GlobalVariablesFlush();
   Comment("");
}

// No blocking accept/read, Sleep, FlushFileBuffers, or reconnect loop in any event handler.
void PollConnection()
{
   uint now = GetTickCount();
   if(handle == -1)
   {
      if(!Due(now, retryAt)) return;
      handle = socket(2, 1, 6);
      if(handle == -1) { Disconnect(); return; }
      int nonblocking[1]; nonblocking[0] = 1;
      if(ioctlsocket(handle, (int)0x8004667E, nonblocking) != 0) { Disconnect(); return; }
      uchar address[16]; ArrayInitialize(address, 0);
      address[0] = 2;
      address[2] = (uchar)(AgentPort / 256); address[3] = (uchar)(AgentPort % 256);
      address[4] = 127; address[7] = 1;
      connectStarted = now;
      int result = connect(handle, address, 16);
      if(result == 0) connected = true;
      else if(WSAGetLastError() != WOULD_BLOCK) { Disconnect(); return; }
   }
   if(!connected)
   {
      uchar peer[16]; int length[1]; length[0] = 16;
      if(getpeername(handle, peer, length) == 0) connected = true;
      else if(now - connectStarted > 2000) Disconnect();
   }
}
bool SendPending()
{
   int total = ArraySize(outgoing);
   for(int attempt = 0; attempt < 4 && outgoingOffset < total; attempt++)
   {
      int count = MathMin(4096, total - outgoingOffset);
      uchar chunk[]; ArrayResize(chunk, count);
      ArrayCopy(chunk, outgoing, 0, outgoingOffset, count);
      int written = send(handle, chunk, count, 0);
      if(written > 0) outgoingOffset += written;
      else
      {
         if(written < 0 && WSAGetLastError() == WOULD_BLOCK) return false;
         Disconnect(); return false;
      }
   }
   if(outgoingOffset < total) return false;
   ArrayResize(outgoing, 0); outgoingOffset = 0;
   return true;
}
void OnTimer()
{
   PollConnection();
   Comment("TRADER / ", connected ? "CONNECTED" : "WAITING FOR LOCAL AGENT",
           "\nOrders: ", EnableOrders ? "ENABLED" : "DISABLED",
           " | Autonomous trailing: ", EnableTrailing ? "ON" : "OFF");
   if(!connected || !SendPending()) return;
   uint started = GetTickCount();
   for(int attempt = 0; attempt < 4 && GetTickCount() - started < 8; attempt++)
   {
      uchar chunk[4096];
      int count = recv(handle, chunk, 4096, 0);
      if(count == 0) { Disconnect(); return; }
      if(count < 0)
      {
         if(WSAGetLastError() != WOULD_BLOCK) Disconnect();
         break;
      }
      if(incomingCount + count > MAX_FRAME) { Disconnect(); return; }
      ArrayResize(incoming, incomingCount + count);
      ArrayCopy(incoming, chunk, incomingCount, 0, count);
      incomingCount += count;
   }
   // Process at most one command per timer tick. Preserve remaining coalesced frames.
   for(int index = 0; index < incomingCount; index++)
   {
      if(incoming[index] != 10) continue;
      string frame = CharArrayToString(incoming, 0, index, CP_UTF8);
      int remainder = incomingCount - index - 1;
      if(remainder > 0) ArrayCopy(incoming, incoming, 0, index + 1, remainder);
      incomingCount = remainder; ArrayResize(incoming, remainder);
      string response = ProcessFrame(frame) + "\n";
      if(!connected) return;
      int bytes = StringToCharArray(response, outgoing, 0, WHOLE_ARRAY, CP_UTF8);
      ArrayResize(outgoing, MathMax(0, bytes - 1)); // Never transmit a trailing NUL.
      outgoingOffset = 0;
      SendPending();
      return;
   }
}

bool ValidId(string id)
{
   if(StringLen(id) != 32) return false;
   for(int i = 0; i < 32; i++)
   {
      ushort c = StringGetCharacter(id, i);
      if(!((c >= 48 && c <= 57) || (c >= 97 && c <= 102))) return false;
   }
   return true;
}
bool SelectSymbol(string symbol)
{
   return StringLen(symbol) > 0 && StringLen(symbol) <= 64 && SymbolSelect(symbol, true);
}
bool ValidTimeframe(int tf)
{
   return tf == 1 || tf == 5 || tf == 15 || tf == 30 || tf == 60 || tf == 240 || tf == 1440;
}
string PositionJson()
{
   return "{\"ticket\":" + IntegerToString(OrderTicket()) +
          ",\"symbol\":" + J(OrderSymbol()) + ",\"side\":" + J(OrderType() == OP_BUY ? "Buy" : "Sell") +
          ",\"lots\":" + N(OrderLots()) + ",\"entryPrice\":" + N(OrderOpenPrice()) +
          ",\"stopLoss\":" + N(OrderStopLoss()) + ",\"takeProfit\":" + N(OrderTakeProfit()) +
          ",\"profit\":" + N(OrderProfit() + OrderSwap() + OrderCommission()) +
          ",\"grossProfit\":" + N(OrderProfit()) + ",\"commission\":" + N(OrderCommission()) +
          ",\"swap\":" + N(OrderSwap()) + ",\"priceDigits\":" +
          (MarketInfo(OrderSymbol(), MODE_POINT) > 0 ? IntegerToString((int)MarketInfo(OrderSymbol(), MODE_DIGITS)) : "null") +
          ",\"closePrice\":" + (OrderCloseTime() > 0 ? N(OrderClosePrice()) : "null") +
          ",\"openTime\":" + IntegerToString((int)OrderOpenTime()) +
          ",\"closeTime\":" + IntegerToString((int)OrderCloseTime()) +
          ",\"comment\":" + J(OrderComment()) + "}";
}
int FindOrder(string id)
{
   string tag = "TR:" + StringSubstr(id, 0, 24);
   for(int pool = 0; pool < 2; pool++)
   {
      int total = pool == 0 ? OrdersTotal() : OrdersHistoryTotal();
      // Only market orders owned by this EA, independent of the currently active chart.
      int lowest = pool == 0 ? 0 : MathMax(0, total - MaximumHistoryLookup);
      for(int i = total - 1; i >= lowest; i--)
         if(OrderSelect(i, SELECT_BY_POS, pool == 0 ? MODE_TRADES : MODE_HISTORY) &&
            OrderMagicNumber() == MagicNumber && OrderType() <= OP_SELL &&
            StringFind(OrderComment(), tag) == 0) return OrderTicket();
   }
   return -1;
}
string OpenOrder(string id, string &args[])
{
   if(ArraySize(args) != 8) return Fail(id, "OPEN_ORDER requires 8 arguments");
   int known = FindOrder(id);
   if(known > 0) return Ok(id, "{\"ticket\":" + IntegerToString(known) + ",\"recovered\":true}");
   if(!EnableOrders) return Fail(id, "EA order execution is disabled");
   bool demo = AccountInfoInteger(ACCOUNT_TRADE_MODE) == ACCOUNT_TRADE_MODE_DEMO;
   if(!demo && !AllowLiveAccount) return Fail(id, "Live account execution is disabled");
   if(!IsConnected() || !IsTradeAllowed() || IsTradeContextBusy()) return Fail(id, "Trading not ready");
   if(EquityRiskBlocked()) return Fail(id, "Equity drawdown circuit breaker blocks new entries");
   string symbol = args[0];
   if(!SelectSymbol(symbol) || (args[1] != "BUY" && args[1] != "SELL")) return Fail(id, "Invalid symbol or side");
   int side = args[1] == "BUY" ? OP_BUY : OP_SELL;
   double lots = StringToDouble(args[2]), stop = StringToDouble(args[3]), target = StringToDouble(args[4]);
   double riskFraction = StringToDouble(args[5]), riskAmount = StringToDouble(args[6]);
   double step = MarketInfo(symbol, MODE_LOTSTEP), minimum = MarketInfo(symbol, MODE_MINLOT), maximum = MarketInfo(symbol, MODE_MAXLOT);
   if(step <= 0 || lots < minimum || lots > maximum || MathAbs(lots / step - MathRound(lots / step)) > 0.00001)
      return Fail(id, "Invalid broker volume");
   for(int i = OrdersTotal() - 1; i >= 0; i--)
      if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES) && OrderMagicNumber() == MagicNumber && OrderSymbol() == symbol)
         return Fail(id, "An owned position already exists on this symbol");
   RefreshRates();
   int requestedQuoteAge = (int)StringToInteger(args[7]);
   if(requestedQuoteAge < 1 || requestedQuoteAge > 3600 || MaximumQuoteAgeSeconds < 1)
      return Fail(id, "Invalid quote freshness limit");
   int quoteAge = (int)MathMax(TimeLocal() - lastTickLocal, TimeCurrent() - MarketInfo(symbol, MODE_TIME));
   if(quoteAge > MathMin(requestedQuoteAge, MaximumQuoteAgeSeconds)) return Fail(id, "Market quote is stale");
   double ask = MarketInfo(symbol, MODE_ASK), bid = MarketInfo(symbol, MODE_BID), point = MarketInfo(symbol, MODE_POINT);
   double tick = SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE), tickValue = MarketInfo(symbol, MODE_TICKVALUE);
   int digits = (int)MarketInfo(symbol, MODE_DIGITS);
   if(ask <= 0 || bid <= 0 || ask < bid || point <= 0 || tick <= 0 || tickValue <= 0 || stop <= 0)
      return Fail(id, "Invalid quote, tick value or stop");
   if(!SpreadAllowed(symbol, (ask - bid) / point)) return Fail(id, "Spread exceeds EA absolute or observed relative limit");
   stop = NormalizeDouble((side == OP_BUY ? MathFloor(stop / tick) : MathCeil(stop / tick)) * tick, digits);
   if(target > 0) target = NormalizeDouble((side == OP_BUY ? MathCeil(target / tick) : MathFloor(target / tick)) * tick, digits);
   double price = side == OP_BUY ? ask : bid;
   double distance = MathMax(1, MarketInfo(symbol, MODE_STOPLEVEL)) * point;
   if((side == OP_BUY && (stop > bid - distance || (target > 0 && target < bid + distance))) ||
      (side == OP_SELL && (stop < ask + distance || (target > 0 && target > ask - distance))))
      return Fail(id, "Stop/target violates broker distance");
   if(riskFraction <= 0 || riskFraction > MaximumRiskPercent / 100.0 || riskAmount <= 0 || riskAmount > MaximumRiskAmount)
      return Fail(id, "Requested risk exceeds EA hard limits");
   double budget = MathMin(AccountEquity() * riskFraction, riskAmount);
   double actualRisk = ((MathAbs(price - stop) + 2 * SlippagePoints * point) / tick * tickValue + CommissionPerLot) * lots;
   if(!MathIsValidNumber(actualRisk) || actualRisk > budget + 0.000001)
      return Fail(id, "Current quote-to-stop risk exceeds budget");
   ResetLastError();
   if(AccountFreeMarginCheck(symbol, side, lots) <= 0 || GetLastError() == 134)
      return Fail(id, "Insufficient free margin", 134);
   ResetLastError();
   // MT4's broker execution itself is synchronous; transport does not busy-wait or blindly retry it.
   int ticket = OrderSend(symbol, side, lots, price, SlippagePoints, stop, target,
                          "TR:" + StringSubstr(id, 0, 24), MagicNumber, 0, clrNONE);
   if(ticket < 0)
   {
      int code = GetLastError();
      return Fail(id, ErrorDescription(code), code, code == 128);
   }
   return Ok(id, "{\"ticket\":" + IntegerToString(ticket) + ",\"entryPrice\":" + N(price) + "}");
}

string Dispatch(string id, string method, string arguments)
{
   string args[]; StringSplit(arguments, ',', args);
   if(method == "PING") return Ok(id, "{\"connected\":" + B(IsConnected()) + ",\"symbol\":" + J(Symbol()) +
      ",\"magicNumber\":" + IntegerToString(MagicNumber) + ",\"ordersEnabled\":" + B(EnableOrders) +
      ",\"trailingEnabled\":" + B(EnableTrailing) + ",\"tradeHistoryVersion\":1,\"loadedHistoryRows\":" +
      IntegerToString(OrdersHistoryTotal()) + ",\"tradingLogicVersion\":1,\"trailingAtrPeriod\":" + IntegerToString(TrailingAtrPeriod) +
      ",\"trailingHistoryBars\":" + IntegerToString(TrailingHistoryBars) +
      ",\"trailingTimeframe\":" + IntegerToString(TrailingTimeframe) + ",\"trailingActivationAtr\":" + N(TrailingActivationAtr) +
      ",\"trailingDistanceAtr\":" + N(TrailingAtrMultiplier) + ",\"trailingStepPoints\":" + N(TrailingStepPoints) +
      ",\"commissionPerLot\":" + N(CommissionPerLot) + ",\"slippagePoints\":" + IntegerToString(SlippagePoints) +
      ",\"dailyDrawdownLimitPercent\":" + N(DailyDrawdownLimitPercent) + ",\"totalDrawdownLimitPercent\":" + N(TotalDrawdownLimitPercent) +
      ",\"drawdownCooldownHours\":" + IntegerToString(DrawdownCooldownHours) + ",\"closeOwnedOnMaxDrawdown\":" + B(CloseOwnedOnMaxDrawdown) + "}");
   if(method == "GET_ACCOUNT") return Ok(id, "{\"accountId\":" + IntegerToString(AccountNumber()) +
      ",\"equity\":" + N(AccountEquity(), 2) + ",\"balance\":" + N(AccountBalance(), 2) +
      ",\"freeMargin\":" + N(AccountFreeMargin(), 2) + ",\"brokerName\":" + J(AccountCompany()) +
      ",\"server\":" + J(AccountServer()) + ",\"terminalSession\":" + J(terminalSession) +
      ",\"currency\":" + J(AccountCurrency()) + ",\"isDemo\":" +
      B(AccountInfoInteger(ACCOUNT_TRADE_MODE) == ACCOUNT_TRADE_MODE_DEMO) + "}");
   if(method == "GET_POSITIONS")
   {
      string positions = "["; int found = 0;
      for(int i = 0; i < OrdersTotal(); i++)
         if(OrderSelect(i, SELECT_BY_POS, MODE_TRADES) && OrderMagicNumber() == MagicNumber && OrderType() <= OP_SELL)
         { if(found++ > 0) positions += ","; positions += PositionJson(); }
      return Ok(id, positions + "]");
   }
   if(method == "FIND_ORDER")
   {
      if(!ValidId(arguments)) return Fail(id, "Invalid order identity");
      int ticket = FindOrder(arguments);
      return Ok(id, ticket > 0 ? "{\"found\":true,\"order\":" + PositionJson() + "}" : "{\"found\":false}");
   }
   if(method == "GET_CLOSED")
   {
      int ticket = (int)StringToInteger(arguments);
      if(!OrderSelect(ticket, SELECT_BY_TICKET) || OrderMagicNumber() != MagicNumber || OrderCloseTime() <= 0)
         return Ok(id, "{\"found\":false}");
      return Ok(id, "{\"found\":true,\"order\":" + PositionJson() + "}");
   }
   if(method == "GET_CLOSED_HISTORY")
   {
      if(ArraySize(args) != 2) return Fail(id, "History cursor and limit required");
      int before = (int)StringToInteger(args[0]), limit = (int)StringToInteger(args[1]);
      if(before < -1 || limit < 1 || limit > 100) return Fail(id, "Invalid history page");
      int total = OrdersHistoryTotal();
      int upper = before < 0 ? total : MathMin(before, total);
      int lower = MathMax(0, upper - limit);
      string orders = "["; int found = 0;
      // Bound work by inspected rows, not matches; unrelated orders cannot create an unbounded scan.
      for(int i = upper - 1; i >= lower; i--)
         if(OrderSelect(i, SELECT_BY_POS, MODE_HISTORY) && OrderMagicNumber() == MagicNumber &&
            OrderType() <= OP_SELL && OrderCloseTime() > 0)
         { if(found++ > 0) orders += ","; orders += PositionJson(); }
      return Ok(id, "{\"orders\":" + orders + "],\"nextIndex\":" + IntegerToString(lower) +
         ",\"total\":" + IntegerToString(total) + "}");
   }
   if(method == "OPEN_ORDER") return OpenOrder(id, args);
   if(ArraySize(args) < 1 || !SelectSymbol(args[0])) return Fail(id, "Invalid symbol");
   string symbol = args[0];
   if(method == "GET_SPEC")
   {
      int digits = (int)MarketInfo(symbol, MODE_DIGITS); double point = MarketInfo(symbol, MODE_POINT);
      return Ok(id, "{\"symbolName\":" + J(symbol) + ",\"accountCurrency\":" + J(AccountCurrency()) +
        ",\"lotSize\":" + N(MarketInfo(symbol, MODE_LOTSIZE)) + ",\"pipSize\":" + N(point * (digits == 3 || digits == 5 ? 10 : 1)) +
        ",\"stepVolume\":" + N(MarketInfo(symbol, MODE_LOTSTEP)) + ",\"minVolume\":" + N(MarketInfo(symbol, MODE_MINLOT)) +
        ",\"maxVolume\":" + N(MarketInfo(symbol, MODE_MAXLOT)) + ",\"tickSize\":" + N(SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE)) +
        ",\"tickValue\":" + N(MarketInfo(symbol, MODE_TICKVALUE)) + ",\"point\":" + N(point) +
        ",\"digits\":" + IntegerToString(digits) + ",\"stopsLevel\":" + IntegerToString((int)MarketInfo(symbol, MODE_STOPLEVEL)) +
        ",\"freezeLevel\":" + IntegerToString((int)MarketInfo(symbol, MODE_FREEZELEVEL)) + "}");
   }
   if(ArraySize(args) < 2) return Fail(id, "Timeframe required");
   int tf = (int)StringToInteger(args[1]);
   if(!ValidTimeframe(tf) || iBars(symbol, tf) < 50) return Fail(id, "Insufficient closed-candle history");
   if(method == "GET_HISTORY")
   {
      if(ArraySize(args) != 3) return Fail(id, "History count required");
      int count = MathMin(5000, MathMin((int)StringToInteger(args[2]), iBars(symbol, tf) - 1));
      if(count < 2) return Fail(id, "Invalid history count");
      string candles = "[";
      for(int shift = 1; shift <= count; shift++)
      {
         if(shift > 1) candles += ",";
         candles += "{\"openTime\":" + IntegerToString((int)iTime(symbol, tf, shift)) +
           ",\"open\":" + N(iOpen(symbol, tf, shift)) + ",\"high\":" + N(iHigh(symbol, tf, shift)) +
           ",\"low\":" + N(iLow(symbol, tf, shift)) + ",\"close\":" + N(iClose(symbol, tf, shift)) + "}";
      }
      return Ok(id, candles + "]");
   }
   if(method == "GET_MARKET")
   {
      if(sampledSymbol != symbol) SampleSpread(symbol);
      double sum = 0;
      for(int k = 1; k <= 20; k++) sum += iATR(symbol, tf, 14, k);
      return Ok(id, "{\"openTime\":" + IntegerToString((int)iTime(symbol, tf, 1)) +
        ",\"open\":" + N(iOpen(symbol, tf, 1)) + ",\"high\":" + N(iHigh(symbol, tf, 1)) +
        ",\"low\":" + N(iLow(symbol, tf, 1)) + ",\"close\":" + N(iClose(symbol, tf, 1)) +
        ",\"ask\":" + N(MarketInfo(symbol, MODE_ASK)) + ",\"bid\":" + N(MarketInfo(symbol, MODE_BID)) +
        ",\"emaFast\":" + N(iMA(symbol, tf, 15, 0, MODE_EMA, PRICE_CLOSE, 1)) +
        ",\"emaSlow\":" + N(iMA(symbol, tf, 30, 0, MODE_EMA, PRICE_CLOSE, 1)) +
        ",\"atr\":" + N(iATR(symbol, tf, 14, 1)) + ",\"atrSma\":" + N(sum / 20) +
        ",\"adx\":" + N(iADX(symbol, tf, 9, PRICE_CLOSE, MODE_MAIN, 1)) +
        ",\"quoteAgeSeconds\":" + IntegerToString((int)MathMax(TimeLocal() - lastTickLocal, TimeCurrent() - MarketInfo(symbol, MODE_TIME))) + "}");
   }
   return Fail(id, "Unknown command");
}
string ProcessFrame(string frame)
{
   int first = StringFind(frame, "|");
   int second = StringFind(frame, "|", first + 1);
   int third = StringFind(frame, "|", second + 1);
   string id = second > first ? StringSubstr(frame, first + 1, second - first - 1) : "";
   if(first != 1 || StringSubstr(frame, 0, 1) != "1" || second < 0 || third < 0 ||
      !ValidId(id) || StringFind(frame, "|", third + 1) >= 0)
   { Disconnect(); return ""; }
   string method = StringSubstr(frame, second + 1, third - second - 1);
   string arguments = StringSubstr(frame, third + 1);
   contextBound = false;
   string identity = IntegerToString(AccountNumber()) + ";" + Utf8Hex(AccountServer()) + ";" + terminalSession;
   if(identity != cacheAccount)
   {
      for(int clear = 0; clear < 64; clear++) { cachedIds[clear] = ""; cachedReplies[clear] = ""; cachedCommands[clear] = ""; }
      cacheAccount = identity;
   }
   if(method != "PING" && method != "GET_ACCOUNT")
   {
      string binding[];
      if(StringSplit(arguments, ';', binding) != 4 ||
         binding[0] != IntegerToString(AccountNumber()) || binding[1] != Utf8Hex(AccountServer()) || binding[2] != terminalSession)
         return Fail(id, "Broker account/server/session changed");
      arguments = binding[3];
      contextBound = true;
   }
   for(int i = 0; i < 64; i++) if(cachedIds[i] == id)
      return cachedCommands[i] == frame ? cachedReplies[i] : Fail(id, "Request identity reused with different arguments");
   string response = Dispatch(id, method, arguments);
   if(method == "OPEN_ORDER")
   {
      cachedIds[cacheCursor] = id; cachedReplies[cacheCursor] = response; cachedCommands[cacheCursor] = frame;
      cacheCursor = (cacheCursor + 1) % 64;
   }
   return response;
}
// Do not use iATR: the terminal's rolling-average ATR differs from the C# Wilder recurrence.
double ClosedWilderAtr(string symbol, datetime expectedCloseBar)
{
   int count = MathMin(TrailingHistoryBars, iBars(symbol, TrailingTimeframe) - 1);
   if(count <= TrailingAtrPeriod) return 0;
   MqlRates rates[];
   ArraySetAsSeries(rates, false);
   if(CopyRates(symbol, TrailingTimeframe, 1, count, rates) != count || rates[count - 1].time != expectedCloseBar) return 0;
   double atr = 0;
   for(int i = 1; i < count; i++)
   {
      if(!MathIsValidNumber(rates[i].high) || !MathIsValidNumber(rates[i].low) ||
         !MathIsValidNumber(rates[i - 1].close) || rates[i].low <= 0 || rates[i].high < rates[i].low || rates[i - 1].close <= 0) return 0;
      double tr = MathMax(rates[i].high - rates[i].low, MathMax(MathAbs(rates[i].high - rates[i - 1].close),
         MathAbs(rates[i].low - rates[i - 1].close)));
      atr = i <= TrailingAtrPeriod ? atr + tr / TrailingAtrPeriod : (atr * (TrailingAtrPeriod - 1) + tr) / TrailingAtrPeriod;
   }
   return MathIsValidNumber(atr) ? atr : 0;
}
void OnTick()
{
   lastTickLocal = TimeLocal();
   SampleSpread(sampledSymbol == "" ? Symbol() : sampledSymbol);
   bool riskBlocked = EquityRiskBlocked();
   uint now = GetTickCount();
   if(!Due(now, trailAt) || !IsTradeAllowed() || IsTradeContextBusy()) return;
   trailAt = now + 1000;
   for(int i = OrdersTotal() - 1; i >= 0; i--)
   {
      if(!OrderSelect(i, SELECT_BY_POS, MODE_TRADES) || OrderMagicNumber() != MagicNumber || OrderType() > OP_SELL) continue;
      string symbol = OrderSymbol();
      bool halted = GlobalVariableCheck(riskPrefix + "halt") && GlobalVariableGet(riskPrefix + "halt") != 0;
      if(riskBlocked && halted && CloseOwnedOnMaxDrawdown && EnableOrders &&
         (AccountInfoInteger(ACCOUNT_TRADE_MODE) == ACCOUNT_TRADE_MODE_DEMO || AllowLiveAccount))
      {
         double exitPrice = MarketInfo(symbol, OrderType() == OP_BUY ? MODE_BID : MODE_ASK);
         if(exitPrice > 0 && !OrderClose(OrderTicket(), OrderLots(), exitPrice, SlippagePoints, clrNONE))
            Print("Emergency owned-position close rejected: ticket=", OrderTicket(), " code=", GetLastError());
         continue;
      }
      if(!EnableTrailing) continue;
      datetime closedTime = iTime(symbol, TrailingTimeframe, 1);
      if(closedTime <= 0 || closedTime + TrailingTimeframe * 60 <= OrderOpenTime()) continue;
      int slot = -1;
      for(int j = 0; j < 256; j++) if(trailingTickets[j] == OrderTicket()) {slot = j; break;}
      if(slot < 0) {slot = trailingCursor; trailingCursor = (trailingCursor + 1) % 256; trailingTickets[slot] = OrderTicket(); trailingBars[slot] = 0;}
      if(trailingBars[slot] == closedTime) continue;
      double atr = ClosedWilderAtr(symbol, closedTime);
      double point = MarketInfo(symbol, MODE_POINT), tick = SymbolInfoDouble(symbol, SYMBOL_TRADE_TICK_SIZE);
      double bid = MarketInfo(symbol, MODE_BID), ask = MarketInfo(symbol, MODE_ASK);
      if(atr <= 0 || point <= 0 || tick <= 0 || bid <= 0 || ask < bid ||
         TimeCurrent() - MarketInfo(symbol, MODE_TIME) > MaximumQuoteAgeSeconds) continue;
      double distance = MathMax(MarketInfo(symbol, MODE_STOPLEVEL), MarketInfo(symbol, MODE_FREEZELEVEL)) * point + point;
      bool buy = OrderType() == OP_BUY;
      double reference = iClose(symbol, TrailingTimeframe, 1) + (buy ? 0 : ask - bid);
      if((reference - OrderOpenPrice()) * (buy ? 1 : -1) < atr * TrailingActivationAtr) {trailingBars[slot] = closedTime; continue;}
      double stop = buy ? MathMin(reference - MathMax(atr * TrailingAtrMultiplier, distance), bid - distance) :
         MathMax(reference + MathMax(atr * TrailingAtrMultiplier, distance), ask + distance);
      stop = NormalizeDouble((buy ? MathFloor(stop / tick) : MathCeil(stop / tick)) * tick, (int)MarketInfo(symbol, MODE_DIGITS));
      if(stop <= 0 || (OrderStopLoss() > 0 && (buy ? stop <= OrderStopLoss() + TrailingStepPoints * point :
         stop >= OrderStopLoss() - TrailingStepPoints * point))) {trailingBars[slot] = closedTime; continue;}
      if(!OrderModify(OrderTicket(), OrderOpenPrice(), stop, OrderTakeProfit(), 0, clrNONE))
         Print("Trailing update rejected: ticket=", OrderTicket(), " code=", GetLastError());
      else trailingBars[slot] = closedTime;
   }
}
