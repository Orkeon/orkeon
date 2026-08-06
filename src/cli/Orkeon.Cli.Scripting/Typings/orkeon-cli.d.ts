// Orkeon CLI scripted-commands TypeScript declarations.
// Embedded as a resource in Orkeon.Cli.Scripting.dll; published to disk by the loader
// in Phase 5 (until then, copy this file manually next to your *.cmd.ts sources).
//
// Reference at the top of each script:
//   /// <reference path="./.orkeon/orkeon-cli.d.ts" />

declare global {
  /**
   * Register a scripted command. Called at module top level — calling it from inside a
   * handler throws (the loader freezes the collector after evaluation).
   */
  function defineCommand<TArgs = Record<string, unknown>>(
    desc: CommandDescriptor<TArgs>,
  ): void;

  /**
   * Register an asynchronous scripted command (design §4.2). `dispatch` launches and returns
   * the prompt immediately; the optional `completed` is replayed on the engine thread when the
   * agent responds. Called at module top level (same freeze rule as `defineCommand`).
   */
  function defineAsyncCommand<TArgs = Record<string, unknown>>(
    desc: AsyncCommandDescriptor<TArgs>,
  ): void;
}

/** Descriptor passed to `defineAsyncCommand`. See design §4.2. */
export interface AsyncCommandDescriptor<TArgs = Record<string, unknown>> {
  name: string;
  aliases?: readonly string[];
  description: string;
  args?: ArgsSchema<TArgs>;
  /**
   * Per-command admission quota: the max number of in-flight instances of THIS command.
   * Omitted ⇒ unbounded (∞). Must be an integer ≥ 1 when present (design §5).
   */
  maxConcurrent?: number;
  /** Launches the work and returns immediately (do not block). Typically returns `{ ticket }`. */
  dispatch: (args: ParsedArgs<TArgs>, ctx: CommandRuntimeContext) => unknown;
  /** Replayed on the engine thread when the agent responds (design §4.3). */
  completed?: (result: CommandResponse, ctx: CommandRuntimeContext) => void;
}

/**
 * The command-dispatch façade — `ctx.services.get<CommandsFacade>("commands")` (design §4, §8).
 * Addresses agents by name; the host routes over `IAgentChannel` and correlates completion.
 */
export interface CommandsFacade {
  /** Dispatch to an agent and return its response (a sync command blocks until it arrives). */
  request(agent: string, intent: string, payload: string): CommandResponse;
  /** Dispatch to an agent in the background; returns a ticket immediately. */
  post(agent: string, intent: string, payload: string): string;
  /** List in-flight / recent instances, optionally filtered. */
  list(filter?: CommandInstanceFilter | string): readonly CommandInstanceView[];
  /** Full view for a ticket, or `undefined`. */
  get(ticket: string): CommandInstanceView | undefined;
  /** Request cancellation of an in-flight ticket. */
  cancel(ticket: string): boolean;
}

/** Agent response — resolved value of `request()` / argument to `completed()`. */
export interface CommandResponse {
  readonly agent: string;
  readonly intent: string;
  readonly success: boolean;
  readonly payload: string;
  readonly error?: string;
}

export type CommandInstanceState = "running" | "done" | "failed" | "cancelled" | "rejected";

/** Read filter for `commands.list`. Any omitted field is a wildcard. */
export interface CommandInstanceFilter {
  state?: CommandInstanceState | "all";
  name?: string;
  agent?: string;
}

/** Snapshot of a dispatched command instance (design §6). */
export interface CommandInstanceView {
  readonly ticket: string;
  readonly name: string;
  readonly kind: "sync" | "async";
  readonly targetAgent: string;
  readonly intent: string;
  readonly correlationId: string;
  readonly state: CommandInstanceState;
  readonly startedAt: string;
  readonly completedAt?: string;
  readonly elapsedMs: number;
  readonly result?: CommandResponse;
  readonly error?: string;
  readonly progress?: { step?: number; percent?: number; message?: string };
  /** LLM tokens attributed to this instance so far; 0 when none were observed. */
  readonly tokens: number;
}

/** Descriptor passed to `defineCommand`. See spec §4.2. */
export interface CommandDescriptor<TArgs = Record<string, unknown>> {
  /** Primary identifier, lowercase kebab-case (`^[a-z][a-z0-9-]*$`). */
  name: string;
  /** Alternate identifiers; must not duplicate `name`. */
  aliases?: readonly string[];
  /** Single-line description (≤ 200 chars, no newline). */
  description: string;
  /** Optional typed args spec (Phase 3). When omitted, handler receives `{ raw: string[] }`. */
  args?: ArgsSchema<TArgs>;
  /** Handler invoked when the user types the command. */
  handler: (
    args: ParsedArgs<TArgs>,
    ctx: CommandRuntimeContext,
  ) => Promise<CommandActionResult | void> | CommandActionResult | void;
}

/** Result returned by a handler. Use `ctx.continue()` / `ctx.exit()` to build one. */
export interface CommandActionResult {
  exit?: boolean;
  message?: string;
}

/** Rich runtime context — see spec §5.1. */
export interface CommandRuntimeContext {
  readonly command: { readonly name: string; readonly rawInput: string };

  readonly log: {
    debug(msg: string, data?: object): void;
    info(msg: string, data?: object): void;
    warn(msg: string, data?: object): void;
    error(msg: string, data?: object): void;
  };

  write(text: string): void;
  writeLine(text: string): void;
  clear(): void;

  prompt(spec: PromptSpec): Promise<PromptResult>;
  progress(spec: { total?: number; label: string }): ProgressHandle;
  table<T extends object>(rows: readonly T[], columns?: readonly (keyof T)[]): void;

  /**
   * Cooperative cancellation token, exposed directly by Jint. Properties are PascalCase
   * (CLR convention preserved through reflection — see spec §5.2):
   *   if (ctx.signal.IsCancellationRequested) return ctx.continue("cancelled");
   *   ctx.signal.ThrowIfCancellationRequested();
   *
   * The `aborted` / `throwIfAborted` aliases on `CommandSignal` are a TypeScript-only
   * documentation convenience; at runtime, use the PascalCase members.
   */
  readonly signal: CommandSignal;

  readonly services: ServiceLocator;

  continue(message?: string): CommandActionResult;
  exit(farewell?: string): CommandActionResult;
}

/** Backing shape — what Jint actually exposes from the underlying CLR CancellationToken. */
export interface CancellationTokenLike {
  readonly IsCancellationRequested: boolean;
  ThrowIfCancellationRequested(): void;
}

/**
 * Documentation alias. The actual runtime object is a CLR `CancellationToken` —
 * write `ctx.signal.IsCancellationRequested`, not `ctx.signal.aborted`.
 */
export type CommandSignal = CancellationTokenLike;

export type PromptSpec =
  | { type: "text"; message: string; default?: string }
  | { type: "confirm"; message: string; default?: boolean }
  | { type: "select"; message: string; choices: readonly string[]; default?: string }
  | { type: "password"; message: string };

export type PromptResult = string | boolean;

export interface ProgressHandle {
  advance(label?: string): void;
  set(value: number, label?: string): void;
  done(label?: string): void;
}

/** Whitelisted service locator (Phase 2 stub; Phase 4 wires the real whitelist). */
export interface ServiceLocator {
  /**
   * Resolve a whitelisted host service. Known keys:
   * - `"fs"` → IFileSystemService, `"configuration"` → IConfiguration,
   *   `"tools"` → IBaseTool[], `"llm"` → ILlmProvider, `"logger"` → ILogger,
   *   `"commands"` → CommandsFacade (agent dispatch),
   *   `"script-host"` → {@link ScriptHostFacade} (run a crew by name — exp 07 SPEC §6).
   */
  get<T = unknown>(name: string): T;
  has(name: string): boolean;
}

/**
 * The crew-launching façade — `ctx.services.get<ScriptHostFacade>("script-host")` (exp 07 §6).
 * Runs a `crews/<name>/crew.ork.ts` (which honours `.body()` + `ctx.llm`), passing `input`
 * as the crew's `globalThis.inputs` object.
 */
export interface ScriptHostFacade {
  /** Run the crew and wait for its output (short crews; call from a sync handler). */
  runCrew(name: string, input?: Record<string, unknown>): Promise<CrewRunOutput>;
  /**
   * Post the crew on a pool thread and return a ticket immediately. The completion is drained
   * to a `defineAsyncCommand`'s `completed(result)` (the same ticket cycle as `commands.post`);
   * `result.payload` carries the crew summary. Use for long workflows.
   */
  runCrewAsync(name: string, input?: Record<string, unknown>): string;
  /** The discovered crew names. */
  listCrews(): readonly string[];
}

/** Result of a crew run launched via {@link ScriptHostFacade}. */
export interface CrewRunOutput {
  readonly ok: boolean;
  readonly summary?: string;
  readonly artifacts?: unknown;
  readonly error?: string;
}

// ── Args schema (declared now so Phase 3 lands without a public-surface change). ──

export type ArgSpec =
  | { type: "string"; required?: boolean; default?: string; choices?: readonly string[] }
  | { type: "number"; required?: boolean; default?: number; min?: number; max?: number }
  | { type: "boolean"; required?: boolean; default?: boolean }
  | { type: "string[]"; required?: boolean; default?: readonly string[] };

export type ArgsSchema<T> = { [K in keyof T]: ArgSpec };

export type ParsedArgs<TArgs> = TArgs extends void | undefined
  ? { readonly raw: readonly string[] }
  : { readonly [K in keyof TArgs]: ResolveArgType<TArgs[K]> };

export type ResolveArgType<S> =
  S extends { type: "string" } ? string
  : S extends { type: "number" } ? number
  : S extends { type: "boolean" } ? boolean
  : S extends { type: "string[]" } ? readonly string[]
  : unknown;
