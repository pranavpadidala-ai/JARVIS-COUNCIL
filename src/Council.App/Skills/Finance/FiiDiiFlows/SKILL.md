---
name: india-institutional-flows
description: "Interpret Indian FII/FPI and DII cash flows, index-futures positioning, participant open interest, block deals and institutional ownership trends."
triggers: FII flows, FPI flows, DII flows, institutional flows, participant OI, FII long short, block deals, bulk deals, smart money, foreign investors
---
# FII/DII and institutional-flow analysis

- Keep cash-market net flows separate from derivatives positioning and shareholding disclosures; they describe different things.
- State whether values are provisional/final, the publication date and units (usually ₹ crore for cash flows).
- Read several sessions together. One-day FII selling can be absorbed by DII buying and does not mechanically predict the next session.
- Participant-wise open interest is positioning, not a direct measure of fresh directional conviction. Check gross long/short and contract expiry context.
- Block/bulk deals and insider transactions are context; identify disclosed party, transaction date, quantity and filing source.
- Cross-check official NSE/NSDL/CDSL disclosures where available. Do not infer missing values or call delayed data live.
- Current `indian_market` provides EOD equity quotes only; flow and participant-OI analysis requires a separate verified data source.
