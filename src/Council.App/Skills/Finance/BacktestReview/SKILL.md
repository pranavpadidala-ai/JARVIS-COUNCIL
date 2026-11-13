---
name: india-backtest-review
description: "Review Indian equity and F&O strategy backtests for lookahead, survivorship bias, overfitting, walk-forward robustness, liquidity, execution costs and Indian market taxes/charges."
triggers: backtest, strategy testing, backtest results, win rate, profit factor, maximum drawdown, walk forward, overfitting, slippage, STT, trading charges
---
# Indian-market backtest review

Audit the data and experiment before judging returns.

- Verify timestamp alignment, corporate-action adjustments, symbol delistings, index membership as-of each date and no lookahead in features or labels.
- Split development, validation and final test periods chronologically. Use walk-forward tests and report parameter-search breadth.
- Include brokerage, STT, exchange transaction charges, SEBI turnover fees, GST, stamp duty, slippage, bid/ask spread, impact, borrow/funding and expiry/roll costs as applicable.
- Report trade count, expectancy in rupees and R, profit factor, win/loss distribution, max drawdown, exposure, turnover, CAGR, volatility and benchmark comparison.
- Stress across regimes, sectors, market caps, volatility and liquidity; show sensitivity to delayed fills and worse slippage.
- Compare against simple baselines and include a paper/live forward period. Never treat in-sample optimisation as evidence of future profitability.
- Tax treatment depends on facts and current law; describe calculations as estimates, not tax advice.
