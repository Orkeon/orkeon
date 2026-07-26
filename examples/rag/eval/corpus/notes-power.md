# Tech notes — Extending the runtime of S-series nodes

Most of the energy drawn by an S-series sensing node goes into radio polling,
not into measurement itself.

Switching a node to eco mode in the companion app lowers the polling frequency
from every two seconds to every thirty seconds. This single change stretches
the runtime of the cell from roughly six months to about eighteen months.

Two further tweaks reduce the draw: dim the status LED to 20% in the node
settings, and disable the hourly telemetry upload if you do not use the
history graphs. Both together add another two to three months of runtime.
