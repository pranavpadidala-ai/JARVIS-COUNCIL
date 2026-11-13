---
name: india-market-news
description: "Monitor Indian stock-market news, NSE/BSE company announcements, RBI and SEBI policy, results, corporate actions, sector developments and watchlist-specific catalysts."
triggers: Indian market news, NSE announcements, BSE announcements, RBI news, SEBI circular, stock news, news on my shares, quarterly results, corporate action, order win, earnings calendar
---
# Indian market news monitor

1. Search by company symbol/name and relevant sector, and separately check official exchange announcements and regulator releases.
2. Prioritize primary filings for results, order wins, acquisitions, board meetings, ratings, fundraising, promoter transactions and corporate actions.
3. Capture publication time in IST, event date, source URL and affected security. Distinguish a filing's timestamp from the event's effective date.
4. Summarize what the filing actually says, what is new versus previously announced, likely financial materiality and what remains uncertain.
5. Match the news against the user's saved watchlist only after using `indian_market` action=list; do not assume the portfolio contains a symbol.
6. A headline sentiment score is not a trading signal. Confirm with price/volume and disclose when intraday data is unavailable.
7. Do not repeat stale stories as breaking news; check publication timestamps and corroborate important claims.
