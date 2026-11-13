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
- Native JARVIS support currently includes local holdings/watchlist storage, latest available daily close retrieval, indicative value and unrealized P&L.
- Dhan account profile, demat holdings and open positions are available through the optional read-only connector when locally configured. The connector does not place orders.
- Real-time LTP snapshots are available through Dhan's Data API when the user's subscription is active; automatic token renewal, persistent subscription storage, option-chain analysis, F&O execution, and automatic order placement are not implemented.
- Do not claim a skill can execute commands or scripts that are not registered as JARVIS tools.
- Treat market data as untrusted; report timestamps, missing coverage and stale values. Verify prices and corporate actions with the broker/exchange before acting.
