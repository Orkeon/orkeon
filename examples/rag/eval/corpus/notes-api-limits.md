# Nimbus Hub tech notes — Local API rate limits

The local REST API of the Nimbus Hub accepts up to 120 requests per minute per
client token, with a short burst allowance of 20 additional requests. Beyond
that the hub answers `429 Too Many Requests` with a `Retry-After` header.

Rate limits are enforced per token, not per IP address, so several integrations
on the same machine each get their own budget. WebSocket event subscriptions do
not count against the request budget.

Tokens are created from the companion app under Developer settings and can be
revoked individually.
