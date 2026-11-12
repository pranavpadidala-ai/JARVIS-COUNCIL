---
name: india-vcp-screening
description: "Screen Indian equities for volatility contraction patterns, trend template, relative strength, volume dry-up, pivot proximity and breakout risk."
triggers: VCP, volatility contraction pattern, Minervini, breakout stocks, trend template, relative strength, volume dry up, NSE screener
---
# NSE/BSE VCP screening

- Define the investable universe and its membership date. Avoid survivorship bias by using historical constituents for historical tests.
- Evaluate the trend template, sequence and depth of contractions, volatility compression, volume dry-up, relative strength and distance to pivot.
- Separate a technically valid setup from a triggered breakout. A close above a pivot with volume confirmation is different from an intraday wick.
- Report the score components and missing inputs; never hide a failed criterion inside a composite score.
- Define stop/invalidation, liquidity, position risk and event risk before considering a setup.
- JARVIS does not currently have a full NSE-500 screener; only analyze a user-supplied universe or a verified connected dataset.
