---
name: indian-portfolio-monitor
description: "Monitor the user's Indian equity holdings and NSE/BSE watchlist using stored share quantities, average cost, latest daily close and indicative unrealized P&L."
triggers: my shares, my stocks, stock portfolio, equity portfolio, portfolio value, monitor my holdings, monitor my stocks, share market, watchlist, stock P&L, holdings P&L
---
# Indian equity portfolio monitor

Use the registered `indian_market` tool for the local EOD ledger. When Dhan credentials are configured and the user asks for broker-held positions or current broker data, use `dhan_readonly`; do not invent portfolio positions.

## Actions
- `set_holding`: save or update one holding with the exact symbol, whole-share quantity and average buy price supplied by the user. Default exchange is NSE; accept NSE or BSE.
- `add_watch`: add a symbol without a quantity or cost basis.
- `remove`: remove a saved holding/watch item.
- `list`: show the locally saved list without network access.
- `quote`: retrieve the latest available daily EOD close for one symbol.
- `portfolio`: retrieve prices for saved symbols and calculate indicative market value and unrealized P&L for priced holdings.
- `set_alert`: save a one-shot EOD closing-price alert (condition=above|below, price).
- `alerts`: list saved alerts and their status.
- `remove_alert`: remove an alert using its listed ID.
- `check_alerts`: fetch latest closes and evaluate active alerts. Triggered alerts are one-shot and become disabled.

## Interpretation
- The watchlist is stored on this device under the user's local application data. It is not synced to a broker or cloud account.
- The TejHQ source provides end-of-day bhavcopy data, normally ready the next morning IST. It is not real-time, intraday or tick data. Always report the data date.
- While JARVIS is running, its background monitor checks the portfolio and active EOD alerts at 09:00 IST on weekdays by default. The time can be changed with JARVIS_MARKET_MONITOR_TIME. The monitor does not run if JARVIS is closed.
- Average buy price must be expressed per current share unit after any split/bonus. Ask the user to correct it if corporate actions changed the share count.
- Missing symbols or unavailable quotes must be marked unavailable; never estimate them or silently exclude them from coverage counts.
- Portfolio totals are indicative and exclude brokerage, taxes, dividends, cash, pledged shares, unsettled trades and corporate-action reconciliation unless the user supplies those details.
- For live account balances or current prices, ask which broker and use an explicitly authorized broker integration if available. Never ask for passwords, OTPs, API secrets or TOTP seeds in chat.
- The public `dhan_instrument_lookup` tool resolves exact NSE/BSE cash-equity symbols to current Dhan security IDs and exchange segments from Dhan's official instrument master. Use it before `dhan_readonly` action=quote; never guess IDs. It does not require credentials.
- The optional `dhan_readonly` tool can verify the Dhan profile, read demat holdings/open positions, and request an LTP snapshot by the resolved Dhan security ID. It is strictly read-only and does not submit, modify, cancel, convert or exit orders.
- Dhan access tokens are typically valid for 24 hours. Read `DHAN_CLIENT_ID` and `DHAN_ACCESS_TOKEN` only from local environment configuration; never ask the user to paste credentials into chat or echo them.
- Dhan LTP snapshots and WebSocket streaming are Data API services; Dhan currently lists its Data API subscription at ₹499 + applicable taxes/month. Do not assume a live-data subscription is active.
- The optional `dhan_live_feed` tool can start a read-only ticker or quote WebSocket for up to 100 resolved instruments, report recent ticks with `status`, and stop with `stop`. It reconnects with bounded backoff while JARVIS remains running. Start it only when the user explicitly asks for live monitoring. It does not persist subscriptions across restarts and does not place trades.
- The live feed uses ticker mode by default to reduce data volume; quote mode adds volume data. Automatic token renewal, persistent subscription storage, and order execution are not implemented.
- Never place, modify or cancel trades. The Dhan connector is read-only with respect to the broker account.
