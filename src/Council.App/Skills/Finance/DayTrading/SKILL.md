---
name: india-day-trading
description: "Indian intraday trading workflow for NSE/BSE equities and F&O: market preparation, chart context, option-chain interpretation, risk checks, trade journaling and post-session review."
triggers: day trading, day trader, intraday, NIFTY, BANKNIFTY, FINNIFTY, F&O, options trading, entry and exit, trading desk, trade signal, monitor market
---
# Indian day-trading workflow

Adapted from the public Indian Trader skill collection. Treat its published strategy statistics as claims to verify, not proof of profitability.

## Workflow
1. Establish the IST market session, relevant index/sector context, scheduled events and overnight global cues.
2. Separate index trades from single-stock trades. Identify the instrument, expiry, lot size, liquidity and bid/ask spread from a current trustworthy source; never use remembered lot sizes.
3. Read the chart on multiple timeframes. Use price structure, VWAP, moving averages, ATR, volume, previous-session levels and opening range only when the underlying data is actually available.
4. Use option-chain PCR, open interest, IV, Greeks and expected move only when a current option-chain source is connected. Do not infer an option chain from cash-market prices.
5. Before any hypothetical setup, define entry trigger, invalidation/stop, target, position size, maximum rupee loss, charges and time-based exit.
6. Prefer no-trade when data is stale, the spread is wide, the stop is unclear, the risk budget is exceeded, or a signal has already passed.
7. Record every paper decision and review results out of sample after charges and slippage.

## JARVIS capability boundary
- Use `indian_market` for the user's saved NSE/BSE equity holdings, watchlist and latest available EOD prices.
- Use public web research for current market news and official exchange/regulator notices.
- JARVIS has TejHQ daily EOD OHLCV data and an optional Dhan Data API live ticker/quote WebSocket for up to 100 resolved instruments. Persistent one-shot threshold alerts can trigger on fresh incoming ticks (trade timestamp within 15 minutes). It does not aggregate live 1/5-minute candles, fetch a live option chain, or run a continuous intraday signal loop. Do not claim otherwise.
- Dhan can be connected for read-only account data and market feeds when the user configures credentials and has the required Data API subscription. There is no order-placement capability. Never submit or suggest that an order was placed.
- Imported upstream command examples, agent names and Claude loop instructions are not executable JARVIS tools.

## Safety
Trading is risky; F&O losses can be rapid and substantial. Do not present signals as guarantees or personalized investment advice. State data timestamps and uncertainty. Backtests must be walk-forward/out-of-sample and include brokerage, STT, exchange charges, SEBI fees, GST, stamp duty, slippage and expiry effects.
