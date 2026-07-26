# Nimbus Hub tech notes — Diagnostic code reference

Terse reference table. Codes are shown on the hub display or in the app log.

| Code | Meaning | Action |
|------|---------|--------|
| E-1102 | Pairing table full | Remove an unused device |
| E-2210 | Local API token rejected | Regenerate the token |
| E-3305 | Time sync failed | Check NTP access |
| E-5521 | Storage partition read-only | Reboot; replace unit if persistent |
| E-7734 | Thermal cutoff — dock suspended output | Let the dock cool 20 minutes, then reseat |
| E-8118 | Mesh channel congestion | Change the mesh channel in settings |
| E-9105 | Firmware image signature invalid | Re-download the official image |
