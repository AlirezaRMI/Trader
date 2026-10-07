# SDD ledger — plan: docs/superpowers/plans/2026-10-04-trader-platform.md

> Historical record: the owner later requested removing the added test project/files. `Trader.Tests` is no longer part of the repository; past verification results below do not authorize recreating tests. Follow the current `AGENTS.md` owner preference.

- User explicitly requested immediate implementation after supplying laptop-test/Linux-backend topology; no additional design-approval pause.
- Existing source is clean; user changes are IDE/generated build artifacts and are preserved.
- Work uses feature branch `feat/trader-control-plane` in the existing shared workspace. No worktree, history rewrite, push or deployment requested.
- Shell helper scripts are Bash-based; use native PowerShell/.NET verification with this tracked ledger.
- Pre-flight: Task 1 preserves strategy interface consumed by Task 3; Tasks 2 and 3 share `IBrokerGateway`; Task 4 consumes runtime snapshots from Task 3; Task 5 packages Task 4 and runs the separate Task 2 agent.
- Modam asset is not present. A text question for its local path is pending; independent implementation continues.
- Task 1 RED: all six regression cases failed against the original implementation (risk cap, minimum lot, NaN equity, support retest, resistance retest, ADX routing).
- Restore discovery: the harness default NuGet folder is relative. Project-local artifact/package paths isolate generated outputs and avoid modifying tracked bin/obj files.
## Task 1 verified

Six strategy regressions failed against the original behavior and now pass (0 failed, 6 passed). The shared sizing uses actual entry-to-stop distance and rounds volume down. PA state is instance/symbol-scoped and requires a later candle. ADX selection uses the engine's threshold. No live orders were sent.

## Task 2 in progress

Added a dependency-free shared `Trader.Protocol` project so the Windows agent and Linux backend use the same framing and request contracts. This is an interface-boundary refinement of the planned broker transport. Framing tests are being run against deliberate stub behavior before implementation.
## Task 2 verified (.NET boundary)

Seven framing regressions and three gateway regressions first failed against deliberate stub behavior; the full suite passed 16/16 after implementation. Tests use actual fragmented streams and paired WebSockets over local TCP, including lost acknowledgements and replacement sessions. Agent compilation is included in the final solution build. Native MT4 compilation/runtime remains a separate verification gate.

## Task 3 in progress

Five persistence tests failed before SQLite implementation and passed afterward. Four execution/notification regressions failed before implementation and passed afterward. Runtime recovery/disconnection tests were then observed failing before the orchestration implementation. SQLite persists execution intents, confirmed quota, settings, position snapshots, journal and a per-recipient Telegram outbox.
## Task 3 verified

Full .NET suite passed 29/29 after durable execution and runtime orchestration. Restart recovery, unknown-order blocking, failed-order quota, HTML escaping and failed-position-poll behavior are covered. No real trades or notifications were executed.

## Task 4 verified at API/model boundary

Four HTTP integration tests and four JavaScript tests failed before implementation, then passed. Full .NET suite passed 33/33; JS suite 4/4. Login initially failed on the user's system-wide DPAPI ring in this sandbox; app-specific managed keys under data fixed the observed failure.

Ruling: native browser QA cannot run because the CUA inventory has no apps or browsers. Continue API/static/module checks and report visual verification outstanding; do not claim screenshot-based QA.

Ruling: MetaEditor was not found in the standard installation paths inspected. Provide EA and an explicit demo checklist; do not claim native compilation or elimination of synchronous broker execution delay.

## Task 5 in progress

Docker daemon is unavailable (no docker_engine pipe), so the recipe is provided but container build/run is not claimed. Linux backend and Windows terminal are separated. Existing .env is preserved. No server deployment or live trading was enabled.
Security cleanup: .env and the eight existing bin/obj directories were removed from Git's index only. Files on disk, including pre-existing generated changes, were preserved. Legacy source credentials were redacted without history rewriting. Rider semantic safe-delete removed the obsolete TradingJob, PositionManagementJob and MetaTraderPipeClient after no-reference previews; their emptied files were then removed.

Additional regression checks cover unexecuted PA setup reuse, account isolation, unrepresentable volume steps and null settings. The first attempt was blocked by a DLL lock from our own temporary QA server; that server was stopped before the actual RED run.

## Final verification and review preparation

Additional RED-to-GREEN regressions correct following the currently active chart when the symbol setting is blank, locate static assets when hosted from test/output directories, and give each host ownership of its logger instead of disposing a process-global logger. The logging failure was reproduced deterministically with two concurrently alive hosts before the fix.

Full .NET suite: dotnet test Trader.Tests/Trader.Tests.csproj --no-restore --disable-build-servers --nologo -v:minimal -> 40 passed, 0 failed. Includes an authenticated real agent/backend/TCP synthetic-terminal integration exchanging 300 fragmented candles, with no live OPEN_ORDER invocation.

Final review is read-only against the current feature-branch working tree (no implementation commits yet). Executing-plans/requesting-code-review authorize the single fresh-context reviewer; there are no implementer or second-review subagents.

Fresh solution build: 0 warnings, 0 errors. JavaScript behavior checks: 4/4; app/views/chart modules pass node --check. Release publish succeeds. Compose config --no-interpolate --quiet succeeds with the example environment and an isolated Docker config directory; this validates configuration syntax, NOT an image build/run. Credential-pattern scan reports no source matches, and Git no longer tracks .env/bin/obj. The private .env remains on disk.

## Fresh review findings and one fix pass

The read-only reviewer identified account/server/session binding, stale readiness after management failure, non-atomic repeated closures, missing lifecycle tracking for trades closed between polls, zero initial tick-count deadlines, engine sizing that ignores configured higher budgets, and unbounded historical identity lookup. Current code inspection confirms these scenarios.

Ruling: promote both reported UI minors (initial connection retry and superseded timeframe responses) to Important — unattended recovery and correctly labeled market data are user-facing reliability requirements — leaving them would require manual reload or display the wrong timeframe.

The single fix pass covers all nine findings with failing regressions first. Native MQL behavior will still require MetaEditor/demo validation; source-contract and deterministic algorithm checks do not substitute for that gate.

## 2026-10-05 continuation

The user requests immediate completion and supplies the quantitative-engine prompt. Continue inline in this feature branch; preserve all prior changes. New spec/plan: `2026-10-05-quant-engine`. Baseline `dotnet test Trader.sln --no-restore --verbosity minimal`: 40 passed, 5 failed. Failures are the three mismatched execution acknowledgements, account switch before dispatch, and terminal replacement between account/position reads. Modam TTF assets are now present; the existing WOFF2-only probe does not recognize them. No real orders, notifications, or external deployment are authorized by implementation/testing.

### Continuation task results

- Broker context, readiness and closure regressions were observed RED (9 failures) and verified GREEN (49/49). Closure identity, journal and outbox are atomic; immediately closed confirmed tickets are recovered. Server-isolated intent/position persistence migrates the old schema.
- Causal indicator/replay/validation tests were observed RED (5/5) against declared stubs, then GREEN (5/5). Live analysis now uses raw closed histories and the same StrategyPipeline. Parameters, timeframes, ATR stops/targets and sizing budgets are external.
- SDK 10.0.401 was downloaded from official Microsoft release metadata, SHA512 checked and extracted only to `.artifacts/dotnet`. Targets are net10.0. .NET 10 asynchronous BackgroundService startup exposed the terminal BoundPort race; listener startup moved into StartAsync. Full suite after that: 57/57, zero warnings/errors after upgrading SQLitePCLRaw.bundle_e_sqlite3 to 3.0.5.
- SQLite 2.1.10 audit warning was verified against GHSA-2m69-gcr7-jv3q. Upgrade did not disable audit or signature verification. Sandbox TLS restrictions were resolved by reviewed restore outside the sandbox; packages remained project-local.
- Bounded runtime Channel and isolated research lane are installed; all research endpoints use existing authentication/origin checks. Research never enables entries or applies selected settings.
- Browser ordering/reconnection checks were RED (2 failures), then GREEN (6/6 total JS tests). Modam TTF/font endpoint test passes. Glass animation, parameter form, research import/backtest/validation/results/export are wired.
- Browser inventory is empty; actual visual desktop/mobile inspection remains unverified. Native MQL and container runtime still need their external tools.
- PowerShell script execution is disabled by existing machine policy. It was not changed. Added a normal Windows .cmd launcher for the local SDK; test/start instructions can use it without changing that policy.

### Continuation final handoff

The current plan's final review, fix pass, SDK rationale, Presentation dependency correction and actual delivery evidence are recorded in [the quant engine ledger](progress-quant-engine.md). Final Release solution build includes all eight projects with zero warnings/errors; final solution tests pass 85/85 and JavaScript tests pass 8/8. Local Api/Bridge publish and an authenticated synthetic published-API smoke check succeeded. Native MT4, browser visual and Docker runtime gates remain explicitly unverified; no real trades, notifications or external deployment were performed.
