// Orkeon Scripting DSL — Version directive convention
//
// Place one of these directives at the top of every `.ork.ts` file:
//
//   /// <reference orkeon-script="1.0" />
//   // @orkeon-script 1.0
//
// The second is the form `tsc` accepts: it rejects the first (TS1084) and then skips its
// semantic pass. Both are comments from TypeScript's perspective; they are parsed at runtime
// by `VersionDirectiveParser`. When the declared version is not supported by the
// host runtime, a `ScriptVersionMismatchError` is thrown before execution.
// When omitted, the runtime assumes the current default version (1.0).

export { };
