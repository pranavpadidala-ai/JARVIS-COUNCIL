---
name: india-market-breadth
description: "Assess Indian market internals beyond NIFTY and SENSEX using advance/decline, participation above moving averages, new highs/lows, sector breadth and index divergence."
triggers: market breadth, advance decline, market internals, stocks above 50 DMA, stocks above 200 DMA, new highs new lows, sector rotation, risk on risk off, broad market health
---
# Indian market breadth

A market index can rise while most constituents weaken. Assess participation across a defined, point-in-time universe.

- State the universe and date (NIFTY 50, NIFTY 500, F&O universe, or a user-provided list); do not mix universes.
- Measure advances/declines, percentage above 20/50/200-day averages, new 52-week highs/lows, sector participation and index/constituent divergence when the data is available.
- Compare breadth with its own history and note survivorship bias, missing symbols, suspensions and corporate actions.
- Separate breadth deterioration from a confirmed index reversal. Breadth is context, not a standalone entry signal.
- Report coverage counts and missing data. Never fabricate a 0-100 health score when the underlying universe data is incomplete.

Current JARVIS market tool can price a user's saved symbols at EOD; it does not yet download a full constituent universe or calculate an exchange-wide breadth dashboard. Ask for a dataset/source or state that the requested breadth measure is not currently available.
