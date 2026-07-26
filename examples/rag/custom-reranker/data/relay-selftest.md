# SunCore inverter — Relay self-test reference

Every night the unit runs a relay self-test before disconnecting from the
grid. The outcome is recorded in the service journal: R-100 when the test
passed, R-101 when it was skipped because the grid was down, and R-102 when
the grid relay is sticking and should be inspected. A unit recording R-102
three nights in a row locks itself out until the relay is serviced.
