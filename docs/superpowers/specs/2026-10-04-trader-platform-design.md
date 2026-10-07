# Trader platform foundation

The user has authorized implementation now. The requested outcome is a reliable, modular MT4 trading robot with a comprehensive glassmorphism dashboard, Modam typography, and retained Telegram notifications. Both MT4 and the robot initially run on the user's Windows laptop. The backend must subsequently run in a Linux container. A forthcoming user prompt will specify the final strategy engines; this foundation repairs existing defects without inventing new trading strategies.

## Boundaries

- Domain: market models, strategy contracts, validated risk sizing, price-zone analysis.
- Application: broker gateway and runtime contracts, lifecycle controls, observable snapshots.
- Infrastructure: durable SQLite journal, remote bridge sessions, runtime coordination, Telegram delivery.
- Bridge: a separate .NET agent beside MT4; maintains an outbound authenticated WebSocket to the backend and a local TCP connection with the EA.
- MQL4 EA: a nonblocking Winsock client. Timer work is bounded; fragmented requests and responses are buffered; broker commands remain on the EA thread. No blocking accept/read loop, Sleep, or FlushFileBuffers.
- Presentation: a modular browser dashboard served by the API, with native ES modules, glass CSS, live events and explicit disconnected/empty states.

MT4 does not become a native Linux application by containerizing the backend. The agent stays alongside a Windows MT4 terminal. Any future Wine deployment requires separate validation. Only the local MT4 transport is unauthenticated loopback TCP; the remote agent connection requires a secret and production TLS.

## Required behavior

1. Size orders from the actual execution quote to stop loss, using account-currency tick values where available and rounding volume down. Reject nonfinite values, invalid stops, invalid volume steps and risk below minimum lot.
2. Correct support/resistance crossing conditions, isolate retained setup state by symbol, and require a later candle for a retest. Keep these engines replaceable for the forthcoming prompt.
3. Use versioned request IDs and complete framed messages. A timed-out write has an uncertain outcome; reconcile it before considering another order. Never blindly replay an order.
4. Persist order intents, signals, successful executions, settings and operational events. Recover broker positions on reconnect/restart. Failed orders do not consume daily trade quota.
5. Default startup is paused. Dashboard controls starting and pausing new entries. Existing broker stop protection remains active when the backend disconnects. Broker operations enforce account identity, symbol/magic ownership, lot/stop limits and trade permissions.
6. Show connection health, last update, market candles, selected strategy, decision reasons, equity/balance/margin, open positions, journal, daily risk and runtime errors. Do not present fabricated account values as connected data.
7. Keep Telegram recipients from the local environment configuration. Never send test messages to real recipients during automated verification. Escape user/broker text before HTML formatting.
8. Load Modam from local browser installation or a supplied licensed WOFF2 file. Until supplied, clearly record fallback use; do not obtain third-party copies.
9. Remove credentials from distributable configuration; production refuses missing bridge/authentication configuration. Do not rewrite existing Git history or revoke credentials without separate authority.

## Verification

Regression tests cover real sizing and retest behavior. Transport tests cover large, fragmented and malformed messages, request correlation, timeout/disconnect, and duplicate order handling. Persistence tests use temporary SQLite files and a new store instance to exercise restart behavior. API/bridge tests use a synthetic broker rather than an actual trading account. Browser inspection exercises layout, navigation, empty states and lifecycle controls. MT4 compilation and an actual demo-terminal session are separate checks if MetaEditor/MT4 are unavailable locally.
