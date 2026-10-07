# SDD ledger — plan: docs/superpowers/plans/2026-10-05-quant-engine.md

> Historical verification record. On 2026-10-05, the owner subsequently requested deletion of the test project/files and staging of implementation changes without commit or push. `Trader.Tests`, its solution entries, test instructions and generated QA output were removed. The prior test results below describe checks performed before deletion; they are not current commands or instructions to recreate tests. The no-test-scaffolding preference is recorded in `AGENTS.md`.

## Scope and continuation rulings

- Continue inline under the user's explicit request to finish the interrupted project. Preserve `feat/trader-control-plane` and all earlier user changes in `D:\Trader`; no commit, branch reset, push, merge or external deployment was requested.
- Research is offline and never starts trading or automatically applies optimized parameters. Runtime startup remains paused. Verification uses synthetic broker data with no real orders or Telegram delivery.
- The latest user asks why .NET 10 was chosen and references `Application/Behaviors/LoggingBehavior.cs`. The choice is support longevity, not a claim of faster trading or better returns. The current official support policy lists .NET 10 LTS through November 14, 2028, and .NET 8/9 through November 10, 2026: https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core.
- SDK 10.0.401 is project-local under `.artifacts/dotnet`, downloaded from Microsoft metadata and SHA512 verified. `global.json` and `scripts/dotnet.cmd` select it. The installed global SDK and machine PowerShell execution policy were not changed.
- Two external IDE saves reverted portions of `TradingRuntime.cs` and `ExecutionTests.cs` during continuation. Their exact externally saved contents were preserved under `.artifacts/runtime-conflict-20261005`; targeted merges restored the interrupted work without resetting the working tree. A stale editor buffer should be reloaded before saving these files again.

## Implemented and verified scope

- Domain parameters/functions, Application strategy/research and Infrastructure deployment remain separated. Replay and live analysis share causal closed-candle EMA/ATR/ADX, zone/strategy selection and risk sizing. Parameters, risk budgets, timeframes and expiry are external; strategy expiry uses market time.
- Bound account/server/session requests and acknowledgements fail closed. Runtime commands use a bounded single-reader Channel; polling revokes readiness before broker work. Invalid or stale market/context data does not permit a new order.
- SQLite persists execution intent before dispatch. Unknown acknowledgement is reconciled by durable identity, not blindly retried. Server-isolated closures, journal and outbox are atomic and once-only, including trades closed between polls/restarts.
- Backtest costs, conservative stop/target ambiguity, opening-gap fills and chronological validation are covered. Bounded parameter search ranks training data and reports later validation separately; it never changes live settings.
- Authenticated research endpoints validate input, body/work limits, cancellation and lane contention. Malformed candle arrays return a client error rather than crashing the host. Research CPU work is outside the execution lane.
- All eight supplied Modam TTF weights are served locally. RTL glass panels, responsive styling, animations/reduced-motion support, parameter forms, research/import/results/equity/export and reliable snapshot/chart requests are implemented.
- `LoggingBehavior` logs only request name, elapsed time and exception type, not request/response objects or credential-bearing exception messages. Cancellation is not logged as an application failure. Three regressions were observed RED before the change and passed after it.
- `Presentation` had been omitted from the solution. Restoring it separately exposed a real NU1107 conflict: obsolete `MediatR.Extensions.Microsoft.DependencyInjection` 11 required MediatR <12 while Application uses 13. The unused extension was removed, restore succeeded, and Presentation was added to the eight-project solution build.

## Single fresh-context review and fix pass

The authorized reviewer was read-only. There were no implementer agents or second review loop. Its four Important findings and the promoted UI minor were addressed in one regression-backed fix pass:

1. Quote expiry during sequential history requests: fetch and verify a fresh bound quote after history, verify the entry candle is unchanged, account for elapsed request time, and transmit a bounded quote-age deadline to the EA. Two runtime regressions were observed RED then GREEN.
2. Stale/future/mixed higher-timeframe candles: validate closed-candle alignment and causal cutoff, reject missed completed higher bars while accepting legitimate session/weekend gaps. Timeline regressions were observed RED then GREEN.
3. Export reflecting edited forms rather than the completed research run: retain a deep-frozen request/result record and export that exact record. The JavaScript regression was observed RED then GREEN.
4. Legacy confirmed orders lacking server identity: bind only after contextual GUID/ticket/symbol/side proof, then resume durable closure recovery. No proof means no replay and a visible unresolved block. Two migration regressions were observed RED then GREEN.
5. Hard-coded analysis labels: labels now use actual configured timeframes and EMA period. This minor was promoted to Important because incorrectly labeled market analysis is misleading; its JavaScript regression was observed RED then GREEN.

Additional regressions cover malformed reconciliation, EMA equality, nonfinite computed indicators and invalid research payloads. Configured higher-budget sizing, event-time expiry, queued shutdown, research lane limits and maximum body size were verified after implementation; those checks are not represented as having first failed against the old code.

## Final command evidence — 2026-10-05

- `scripts\dotnet.cmd build Trader.sln --no-restore --configuration Release --verbosity minimal --disable-build-servers`: eight projects built, **0 warnings, 0 errors**, including Presentation.
- `scripts\dotnet.cmd test Trader.sln --no-restore --no-build --configuration Release --verbosity minimal --disable-build-servers`: **85 passed, 0 failed, 0 skipped**. The final solution-level run includes the newly added Presentation project.
- `node --test Trader.Tests/ui.test.mjs`: **8 passed, 0 failed**. All dashboard `.mjs` modules also pass `node --check`.
- Release publish of Api and Bridge succeeded into `.artifacts/delivery/api` and `.artifacts/delivery/bridge`; these are local build artifacts, not an external deployment.
- An authenticated smoke check ran the published API on isolated loopback port 5185, with synthetic credentials, isolated data, no Bridge/terminal and Telegram delivery disabled. Health was `alive`; the Modam font response contained **54,814 bytes**; a 240-bar synthetic replay reported **10 simulated trades**; optimization returned **4 candidates** and a separate validation result. Snapshot reported `entriesEnabled=false` and `terminalConnected=false`.
- That exact temporary API process was identified by its published DLL and isolated QA data path, stopped and verified exited. No user's normal service was targeted.
- `git diff --check` passes. Existing `.env` and generated files remain on disk; the earlier security cleanup removed them only from Git's index. Previous exposed credentials still require owner rotation; history was not rewritten.

## External gates and explicit limitations

- Browser inventory is empty (`apps=[]`, `browsers=[]`), so screenshot/desktop/mobile visual inspection is unavailable. Font delivery, static assets, syntax and behavior tests do not replace this gate.
- MetaEditor was not found in the inspected standard locations. The revised `TraderBridgeEA.mq4` must be recompiled and tested on demo with this backend; OPEN now includes the quote-age argument. Native compilation, broker timing and real-account execution are unverified.
- Docker daemon is unavailable. Earlier Compose configuration validation passed, but the .NET 10 image build and container runtime were not run. The deployment recipe is not claimed as a certified running container.
- Shared strategy semantics do not guarantee identical fills. Offline simulation does not reproduce tick-level EA trailing, dynamic spread/tick value, swaps, margin or every account-currency conversion/broker rule. Conservative synthetic replay is not evidence of future profitability.
- The final integration ruling is **keep as-is**: preserve the feature branch and shared working tree. Do not clean the uncommitted work, push, merge, enable entries or send messages as part of delivery.

Run instructions: `README.md`; broker/demo checklist: `docs/mt4-bridge.md`; simulation assumptions: `docs/research.md`; deployment requirements: `docs/deployment.md`.
