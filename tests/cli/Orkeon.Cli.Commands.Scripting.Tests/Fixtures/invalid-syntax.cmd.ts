// Phase 1 fixture: deliberately broken — the loader must log Error and skip it.
defineCommand({
  name: "broken",
  description: "This script never completes evaluation."
  // intentional: missing comma + dangling brace
