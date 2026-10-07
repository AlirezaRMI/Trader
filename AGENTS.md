# Repository instructions

## Owner preference: no test scaffolding

- Do not create or recreate test projects, test files, test layers, test fixtures or test-framework dependencies unless the owner explicitly requests them.
- This explicit owner instruction overrides workflows that would otherwise require adding automated tests, including TDD. Do not move test-only code into production layers as a workaround.
- Verify changes with builds, read-only inspection and appropriate manual/runtime checks without adding persistent test files.
- The removed `Trader.Tests` project must not be reintroduced. References to earlier test runs in historical documentation describe past verification, not instructions to recreate that project.
- Strategy backtesting and parameter validation are production trading/research features, not software test scaffolding; preserve them unless the owner requests their removal.
