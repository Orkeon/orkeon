// Orkeon Scripting DSL — Version directive convention
//
// Place this triple-slash directive at the top of every `.ork.ts` file:
//
//   /// <reference orkeon-script="1.0" />
//
// The directive is a comment from TypeScript's perspective; it is parsed at runtime
// by `VersionDirectiveParser`. When the declared version is not supported by the
// host runtime, a `ScriptVersionMismatchError` is thrown before execution.
// When omitted, the runtime assumes the current default version (1.0).

export { };
