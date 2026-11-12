---
name: india-options-analysis
description: "Educational analysis of NSE/BSE F&O instruments: option-chain structure, PCR, open interest, max pain, IV, Greeks, expiry, lot sizes, payoff and Indian transaction costs."
triggers: Indian options, Nifty options, Bank Nifty options, option chain, PCR, max pain, open interest, implied volatility, Greeks, theta, expiry, F&O strategy
---
# Indian options analysis

- Confirm exchange, underlying, contract symbol, expiry, strike, lot size and quote timestamp from a current source. Contract specifications and expiry conventions change; never rely on remembered values.
- Explain calls/puts, intrinsic/time value, IV, delta, gamma, theta and vega with units and assumptions.
- For chain interpretation, separate total OI from change in OI, volume from OI, and spot/futures context. PCR and max pain are descriptive, not predictive.
- Calculate payoff at expiry and before expiry separately. Include spread, liquidity, slippage, brokerage, STT, exchange transaction charges, SEBI fees, GST and stamp duty where relevant.
- Discuss defined-risk structures and worst-case loss. Avoid recommending uncovered option selling or leverage without explicit risk context.
- JARVIS currently has no native live option-chain or broker feed. Do not invent premiums, Greeks, lot sizes or expiry data. Request a current chain export/screenshot or a verified data connection.
