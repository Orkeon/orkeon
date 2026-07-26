# Nimbus Hub tech notes — Firmware updates and recovery mode

The Nimbus Hub checks for firmware updates nightly and installs them during the
maintenance window configured in the companion app.

To enter recovery mode before a manual firmware flash: unplug the hub, hold the
pairing button, reconnect power, and keep the button pressed for 10 seconds
until the status ring blinks amber. In recovery mode the hub exposes a minimal
web console on port 8080 for uploading a signed firmware image.

A failed update never bricks the device: the previous firmware slot stays
bootable and is selected automatically after three failed boot attempts.
