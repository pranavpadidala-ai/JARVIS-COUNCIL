# Finance skill sources and limitations

These JARVIS-native skill instructions adapt selected public workflows and methodology topics. They are not model fine-tuning, financial advice, or proof that the upstream code/data integrations exist in JARVIS.

## Upstream projects reviewed
- https://github.com/Ritik-Sharma-dev/indian-trader-skill — MIT-licensed Indian intraday trading toolkit and 22 Claude Code skills. JARVIS adapted the workflow ideas for the trading desk, live-monitor concepts, risk checks, chart/option-chain analysis, scanners, paper journals, Indian news and institutional flows. No upstream Python runtime, scripts, agents, MCP server or broker connection was copied or executed.
- https://github.com/kishorkukreja/finance-trading-skills — MIT-licensed Indian equity research skill collection. JARVIS adapted the topics of technical analysis, VCP screening, stock research, macro scenarios, FII/DII flows, options education, backtest validation, market breadth, news and weekly F&O planning. Claude-specific commands and scripts are not part of JARVIS.
- https://tejhq.dev/docs and https://tejhq.dev/data-terms — TejHQ keyless daily EOD OHLCV API used by the native portfolio monitor. Data is normally ready the next morning IST; it is not an intraday feed. The exchange's data rights and terms apply independently. JARVIS stores only user-entered holdings/watchlist data, not raw market-data history.

## Capability boundaries
- Native JARVIS support currently includes local holdings/watchlist storage, latest available daily close retrieval, indicative value and unrealized P&L.
- No broker login, account sync, real-time quote stream, option chain, F&O execution, or automatic order placement is implemented.
- Do not claim a skill can execute commands or scripts that are not registered as JARVIS tools.
- Treat market data as untrusted; report timestamps, missing coverage and stale values. Verify prices and corporate actions with the broker/exchange before acting.
