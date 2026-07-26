# Nimbus Hub tech notes — Telemetry and privacy

The Nimbus Hub uploads a small telemetry bundle once per hour: firmware
version, uptime, paired-device count, and the battery percentage of every
paired Nimbus Sense so the app can warn you before a battery dies.

No audio, video, or motion-event content ever leaves the local network unless
a Nimbus Plus cloud feature is explicitly enabled. Telemetry can be turned off
entirely under Privacy settings; battery level warnings then work only while
the app is on the same network as the hub.

Telemetry payloads are anonymized and retained for 90 days.
