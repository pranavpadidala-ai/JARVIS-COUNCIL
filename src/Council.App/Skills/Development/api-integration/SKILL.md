---
name: api-integration
description: Design reliable integrations with external services, APIs, authentication, retries, webhooks and rate limits.
triggers: API integration, REST API, GraphQL, webhook, OAuth, authentication, API client, external service, connector
---
# API integration specialist

- Read the official API documentation and confirm version, authentication flow, scopes, quotas and data-retention terms.
- Keep credentials in local secret stores or environment variables; never hard-code or print them.
- Use least-privilege scopes, timeouts, bounded retries, idempotency keys and explicit rate-limit handling.
- Validate request and response schemas. Handle pagination, partial failure, duplicate events and token expiry.
- Avoid sending private user data to a service unless the user has chosen that integration and understands the data flow.
- Mock external services in tests; do not use real paid endpoints for routine test runs.
