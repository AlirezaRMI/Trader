# Trader Platform Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Repair verified defects and deliver the modular MT4 bridge, durable runtime, live dashboard and Telegram integration requested by the user.

**Architecture:** Keep broker-independent analysis in Domain and orchestration contracts in Application. Connect a Linux-compatible backend to a separate local MT4 agent through authenticated WebSockets; connect the EA to that agent over nonblocking loopback TCP. Serve the browser dashboard from the API.

**Tech Stack:** .NET 9, existing SQLite dependencies, xUnit, native browser ES modules/CSS, MQL4 and Windows Winsock.

**Spec:** `docs/superpowers/specs/2026-10-04-trader-platform-design.md`

## Global Constraints

- Both MT4 and the robot initially run on the user's Windows laptop.
- The backend must subsequently run in a Linux container.
- A forthcoming user prompt will specify the final strategy engines.
- Default startup is paused.
- Never send test messages to real recipients during automated verification.
- Do not rewrite existing Git history or revoke credentials without separate authority.

## Review Focus

- An order succeeds but its response is lost: persist uncertainty and reconcile, never open a second order blindly.
- An agent reconnects while an older session is closing: an older disconnect must not remove the replacement session.
- Broker prices or tick values are zero/NaN or volume minimum exceeds the risk budget: produce Hold rather than invalid/oversized orders.
- Backend restarts with broker positions already open: restore ownership and position state without duplicate entries.
- Remote control, broker text and Telegram failures: enforce authentication/origin, encode untrusted text, and retain operational visibility.

### Task 1: Financial regression fixes

**Files:** `Trader.Tests/StrategyRegressionTests.cs`, `Domain/Services/PositionSizer.cs`, existing engines and market models.

**Interfaces:** Preserve `IStrategyEngine.Evaluate(...) -> TradeDecision`. Produce shared `PositionSizer.CalculateLots(...) -> double`, with zero meaning not tradable.

- [ ] Write tests for risk cap, invalid inputs, true support/resistance retests and ADX routing.
- [ ] Run `dotnet test Trader.Tests/Trader.Tests.csproj`; observe the existing defects.
- [ ] Implement actual-distance sizing, downward volume normalization and symbol-scoped later-candle retests.
- [ ] Run the complete test project; require zero failures.

### Task 2: Framed broker transport and MT4 agent

**Files:** `Application/Broker/*`, `Infrastructure/Bridge/*`, `Bridge/*`, `Api/ClientInformation/TraderBridgeEA.mq4`, transport tests.

**Interfaces:** `IBrokerGateway.InvokeAsync(string method, string arguments, CancellationToken) -> Task<JsonElement>`; request/response envelope carries version, request ID, method and data/error.

- [ ] Write correlation/fragmentation/oversize/timeout tests against real streams and synthetic endpoints.
- [ ] Observe failures before transport implementation.
- [ ] Implement bounded UTF-8 frames, authenticated outbound agent sessions, local agent TCP and nonblocking EA command handling.
- [ ] Verify stream tests and the full .NET suite; record MetaEditor availability separately.

### Task 3: Durable runtime and notifications

**Files:** `Application/Runtime/*`, `Infrastructure/Persistence/*`, `Infrastructure/Runtime/*`, existing Telegram code, persistence/runtime tests.

**Interfaces:** Runtime snapshot contains broker status, settings, account, market analysis, positions and journal. Durable store exposes atomic intent registration and confirmed execution/reconciliation updates.

- [ ] Write restart, failed-order quota and uncertain-order reconciliation tests.
- [ ] Observe failures, implement durable SQLite state and non-overlapping cancellable cycles.
- [ ] Wire replaceable strategy engines and persist decision reasons; retain Telegram delivery with escaped text and visible failures.
- [ ] Verify the complete suite without calling a real broker or real Telegram recipients.

### Task 4: API and glass dashboard

**Files:** `Api/Program.cs`, `Api/Endpoints/*`, `Api/wwwroot/*`, browser/API tests.

**Interfaces:** Authenticated snapshot/settings/journal/positions/lifecycle endpoints and a live event stream. Browser uses these endpoints rather than invented live data.

- [ ] Define API authorization and lifecycle integration tests before handlers.
- [ ] Implement REST/event routes, development loopback restrictions and required production credentials.
- [ ] Build responsive Persian RTL glass UI with Modam loading, charts, filtering, detailed status and working controls.
- [ ] Run API tests, JS behavioral checks and visual browser QA on desktop/mobile widths.

### Task 5: Deployment and complete verification

**Files:** `compose.yaml`, `Api/Dockerfile`, launch scripts, `.env.example`, README and operational documentation.

**Interfaces:** Docker hosts only the backend; local launch starts backend and MT4 agent with shared configuration. Secrets remain outside distributable files.

- [ ] Remove obsolete exposed configuration and document replacement/rotation requirements without rewriting history.
- [ ] Provide local and Linux backend startup instructions and the updated EA installation guide.
- [ ] Run solution build, complete tests and API/browser/bridge integration checks.
- [ ] Obtain a fresh code review, fix important findings with regression tests and report actual verification limits.
