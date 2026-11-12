---
name: india-technical-analysis
description: "Technical analysis for NSE/BSE equities and indices: trend, support/resistance, moving averages, VWAP, RSI, MACD, ATR, volume, pivots and multi-timeframe context."
triggers: technical analysis India, chart analysis, support resistance, VWAP, RSI, MACD, ATR, moving averages, pivot levels, stock chart, Nifty chart
---
# Indian technical analysis

- Confirm the symbol, exchange, interval, timezone and candle timestamp.
- Describe trend, swing structure, support/resistance, gaps, moving averages, momentum, volatility and volume only when the relevant series is available.
- For intraday charts, require actual intraday candles; never derive VWAP or opening-range levels from EOD-only data.
- Use adjusted history for long-term returns when splits, bonuses and dividends matter. Raw EOD prices can distort historical comparisons.
- Give conditional scenarios and invalidation levels rather than certainty. Avoid false precision and avoid presenting an indicator threshold as a guaranteed signal.
- The current JARVIS native market connector provides daily EOD OHLCV only; use it for daily context and do not claim 1m/5m/15m live chart analysis.
