# Finance skill sources and limitations

These JARVIS-native skill instructions adapt selected public workflows and methodology topics. They are not model fine-tuning, financial advice, or proof that the upstream code/data integrations exist in JARVIS.

## Upstream projects reviewed
- https://github.com/Ritik-Sharma-dev/indian-trader-skill — MIT-licensed Indian intraday trading toolkit and 22 Claude Code skills. JARVIS adapted the workflow ideas for the trading desk, live-monitor concepts, risk checks, chart/option-chain analysis, scanners, paper journals, Indian news and institutional flows. No upstream Python runtime, scripts, agents, MCP server or broker connection was copied or executed.
- https://github.com/kishorkukreja/finance-trading-skills — MIT-licensed Indian equity research skill collection. JARVIS adapted the topics of technical analysis, VCP screening, stock research, macro scenarios, FII/DII flows, options education, backtest validation, market breadth, news and weekly F&O planning. Claude-specific commands and scripts are not part of JARVIS.
- https://tejhq.dev/docs and https://tejhq.dev/data-terms — TejHQ keyless daily EOD OHLCV API used by the native portfolio monitor. Data is normally ready the next morning IST; it is not an intraday feed. The exchange's data rights and terms apply independently. JARVIS stores only user-entered holdings/watchlist data, not raw market-data history.
- https://dhanhq.co/docs/v2/authentication/ — official DhanHQ v2 authentication, access-token validity, and profile endpoint documentation.
- https://dhanhq.co/docs/v2/portfolio/ — official DhanHQ holdings and open-positions endpoints.
- https://dhanhq.co/docs/v2/market-quote/ — official DhanHQ LTP/quote snapshot endpoint and instrument batching/rate limits.
- https://dhanhq.co/docs/v2/instruments/ — official Dhan instrument master CSV and exchange-segment reference used by `dhan_instrument_lookup`.
- https://dhanhq.co/docs/v2/live-market-feed/ — official DhanHQ WebSocket protocol, binary packet layouts, subscription limits and ping/pong behaviour used by `dhan_live_feed`.
- https://github.com/dhan-oss/DhanHQ-py — official Dhan SDK reference for v2 ticker (request code 15) and quote (request code 17) subscription modes.
- https://dhan.co/support/platforms/dhanhq-api/how-to-access-dhan-api/ — Dhan support page for Data API subscription access and current pricing.

## Capability boundaries
- Native JARVIS support includes local holdings/watchlist storage, daily EOD close retrieval, indicative value and unrealized P&L, plus optional Dhan live ticker/quote streaming and persistent one-shot price-threshold alerts on incoming ticks.
- Dhan account profile, demat holdings and open positions are available through the optional read-only connector when locally configured. The connector does not place orders.
- Real-time LTP snapshots and WebSocket tick/quote packets are available through Dhan's Data API when the user's subscription is active. Persistent one-shot price alerts are stored locally; live subscriptions and tick history are memory-only. Automatic token renewal, live candle aggregation, option-chain analysis, F&O execution, and automatic order placement are not implemented.
- Do not claim a skill can execute commands or scripts that are not registered as JARVIS tools.
- Treat market data as untrusted; report timestamps, missing coverage and stale values. Verify prices and corporate actions with the broker/exchange before acting.


## Direct runtime integration
- **Indian Trader runtime:** the MIT-licensed `Ritik-Sharma-dev/indian-trader-skill` repository is pinned as the `integrations/indian-trader` Git submodule. JARVIS exposes its CLI through the `indian_trader` tool using a fixed read-only command/argument allowlist and argument-safe process invocation. It does not enable the upstream MCP server, broker access, order placement, config mutation, paper-trade mutation, or arbitrary command execution. Initialize with `scripts/Initialize-ExternalIntegrations.ps1`; Python 3.8+ is required. Run the upstream offline tests before relying on the tool.
- **Existing Dhan integration:** JARVIS keeps its native read-only Dhan account, instrument lookup and optional market-feed tools. The upstream DhanHQ skill repository is not imported because it also documents live order placement; only read-only native operations are exposed.
- **OmniRoute:** the native .NET provider and exact-model/free-model allowlist are already part of JARVIS; the large gateway repository is not vendored as a submodule.
