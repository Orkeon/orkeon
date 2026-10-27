// Orkeon CLI scripted-commands TypeScript declarations.
// Shipped inside the `orkeon` tool: `orkeon typings` writes this file (and orkeon.d.ts) into
// ./.orkeon/ — run it again after updating the tool.
//
// Reference at the top of each script:
//   /// <reference path="./.orkeon/orkeon-cli.d.ts" />

declare global {
  /**
   * Register a scripted command. Called at module top level — calling it from inside a
   * handler throws (the loader freezes the collector after evaluation).
   */
  function defineCommand<TArgs extends ArgsSchema | undefined = undefined>(
    desc: CommandDescriptor<TArgs>,
  ): void;

  /**
   * Register an asynchronous scripted command. `dispatch` launches and returns
   * the prompt immediately; the optional `completed` is replayed on the engine thread when the
   * agent responds. Called at module top level (same freeze rule as `defineCommand`).
   */
  function defineAsyncCommand<TArgs extends ArgsSchema | undefined = undefined>(
    desc: AsyncCommandDescriptor<TArgs>,
  ): void;
}

/** Descriptor passed to `defineAsyncCommand`. */
export interface AsyncCommandDescriptor<TArgs extends ArgsSchema | undefined = undefined> {
  name: string;
  aliases?: readonly string[];
  /**
   * Group `/help` lists the command under (`^[a-z][a-z0-9-]*$`). When omitted, the name of the folder
   * holding the `*.cmd.ts` (none for a file at the root of the commands mount).
   */
  group?: string;
  description: string;
  /** Typed args spec. When omitted, `dispatch` receives `{ raw: string[] }`. */
  args?: TArgs;
  /**
   * Per-command admission quota: the max number of in-flight instances of THIS command.
   * Omitted ⇒ unbounded (∞). Must be an integer ≥ 1 when present.
   */
  maxConcurrent?: number;
  /** Launches the work and returns immediately (do not block). Typically returns `{ ticket }`. */
  dispatch: (args: ParsedArgs<TArgs>, ctx: CommandRuntimeContext) => unknown;
  /**
   * Replayed on the engine thread, under the script's engine lock, when the agent (or the crew of a
   * `runCrewAsync`) responds. It may be `async`: when it returns a promise the drain awaits it before
   * going on (to the next completion, to the next command of the same script), so its whole body,
   * every `await` included, must settle there — a host read (`ctx.services.get(...)`), then a
   * `runCrewAsync`, is the intended use. Work left pending past the promise (a promise not awaited)
   * is not waited for, and a launch it makes is not chained to this command.
   *
   * A rejection, or a throw, does not stop the drain: the console shows
   * `Error in completed('<name>'): <first line of the reason>`, the host log keeps the whole error,
   * and the registry reports a completion failure for the instance (`ReportCompletionFailure`;
   * `orkeon-repl --replay` stops on the line that launched it). The instance keeps its own state
   * (`done` or `failed`), and a launch made before the rejection still runs and drains.
   *
   *   completed: async (result, ctx) => {
   *     // A host service of the application whose method returns a Task, which the script sees as a promise.
   *     const saved = await ctx.services.get("session").save();
   *     ctx.writeLine(`session saved to ${saved}`);
   *     ctx.services.get("script-host").runCrewAsync("next-step", { previous: result.payload });
   *   },
   */
  completed?: (result: CommandResponse, ctx: CommandRuntimeContext) => void | Promise<void>;
}

/**
 * The command-dispatch facade — `ctx.services.get<CommandsFacade>("commands")`.
 * Addresses agents by name; the host routes over `IAgentChannel` and correlates completion.
 */
export interface CommandsFacade {
  /** Dispatch to an agent and return its response (a sync command blocks until it arrives). */
  request(agent: string, intent: string, payload: string): CommandResponse;
  /**
   * Dispatch to an agent in the background; returns a ticket immediately. Its completion drains to the
   * `completed` of the command that posted it. Called from a `completed`, the new instance is one hop deeper
   * in the chain, bounded by `MaxChainDepth` like {@link ScriptHostFacade.runCrewAsync}: at the limit it throws
   * `chain depth 8 reached (MaxChainDepth 8, /<command>)`.
   */
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
  /**
   * True when any turn of this request was written to the console in the foreground
   * (`Orkeon:Cli:ConsoleStreaming:Enabled`): skip your own print of `payload`, or the answer shows twice.
   * Background and labelled output report false: streaming off, an agent that did not stream, and a
   * background instance (`post`, `runCrewAsync`), whose turns are written as whole lines prefixed with
   * its ticket (`[t2] `).
   */
  readonly streamed: boolean;
  readonly error?: string;
}

export type CommandInstanceState = "running" | "done" | "failed" | "cancelled" | "rejected";

/** Read filter for `commands.list`. Any omitted field is a wildcard. */
export interface CommandInstanceFilter {
  state?: CommandInstanceState | "all";
  name?: string;
  agent?: string;
}

/** Snapshot of a dispatched command instance. */
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

/** Descriptor passed to `defineCommand`. */
export interface CommandDescriptor<TArgs extends ArgsSchema | undefined = undefined> {
  /** Primary identifier, lowercase kebab-case (`^[a-z][a-z0-9-]*$`). */
  name: string;
  /** Alternate identifiers; must not duplicate `name`. */
  aliases?: readonly string[];
  /**
   * Group `/help` lists the command under (`^[a-z][a-z0-9-]*$`). When omitted, the name of the folder
   * holding the `*.cmd.ts` (none for a file at the root of the commands mount).
   */
  group?: string;
  /** Single-line description (≤ 200 chars, no newline). */
  description: string;
  /** Typed args spec. When omitted, the handler receives `{ raw: string[] }`. */
  args?: TArgs;
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

/** Rich runtime context. */
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
   * (CLR convention preserved through reflection):
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

/** Whitelisted service locator: `get` throws for a key that is not whitelisted. */
export interface ServiceLocator {
  /**
   * Resolve a whitelisted host service. Keys of the default whitelist:
   * - `"fs"` → IFileSystemService, `"tools"` → IBaseTool[],
   *   `"runtime"` → {@link RuntimeInfo} (the version and install channel of the runtime that runs the command);
   * - present only when the host wired them (check with `has`): `"llm"` → ILlmProvider,
   *   `"logger"` → ILogger, `"commands"` → CommandsFacade (agent dispatch),
   *   `"script-host"` → {@link ScriptHostFacade} (run a crew by name),
   *   `"permissions"` → {@link PermissionRulesFacade} (the session's tool-call rules, in `orkeon-repl`),
   *   `"approvals"` → {@link PendingApprovals} (the tool approvals waiting for the operator, read-only, in `orkeon-repl`),
   *   `"session"` → {@link SessionFacade} (save the persisted session now, here or to another file, load a saved one, in `orkeon-repl`),
   *   `"usage"` → {@link SessionUsage} (the session's LLM usage by model and by command, with its cost, read-only).
   *
   * The host configuration is deliberately not served: it carries API keys and connection
   * strings (SEC-009), so `get("configuration")` throws.
   */
  get(name: "commands"): CommandsFacade;
  get(name: "script-host"): ScriptHostFacade;
  get(name: "permissions"): PermissionRulesFacade;
  get(name: "approvals"): PendingApprovals;
  get(name: "session"): SessionFacade;
  get(name: "runtime"): RuntimeInfo;
  get(name: "usage"): SessionUsage;
  get<T = unknown>(name: string): T;
  has(name: string): boolean;
}

/**
 * The crew-launching facade — `ctx.services.get<ScriptHostFacade>("script-host")`.
 * Runs a `crews/<name>/crew.ork.ts` (which honours `.body()` + `ctx.llm`), passing `input`
 * as the crew's `globalThis.inputs` object.
 */
export interface ScriptHostFacade {
  /**
   * Run the crew and wait for its output (short crews; call from a sync handler). The call
   * blocks and returns the output itself, not a promise: `.then(...)` on it throws a
   * `TypeError`. `await` on it is harmless (it resolves to the same value). In `orkeon-repl`
   * with the permission gate on, a tool call of the crew that needs an approval is asked here,
   * while the call waits, and the time the operator takes to answer does not count in
   * `RunCrewTimeout`.
   */
  runCrew(name: string, input?: Record<string, unknown>): CrewRunOutput;
  /**
   * Post the crew on a pool thread and return a ticket immediately. The completion is drained
   * to a `defineAsyncCommand`'s `completed(result)` (the same ticket cycle as `commands.post`);
   * `result.payload` carries the crew summary. Use for long workflows. It may be called from a
   * `completed` handler: the second crew's completion drains to its own command (the same command
   * name). The chain is bounded by `MaxChainDepth` (default 8, `ScriptCommandsConfiguration.MaxChainDepth`);
   * a deeper call throws `chain depth 8 reached (MaxChainDepth 8, /<command>)`. A driver relaunching
   * itself passes `{ chain: "continue" }` (see {@link CrewLaunchOptions}) to keep the parent's depth.
   */
  runCrewAsync(name: string, input?: Record<string, unknown>, options?: CrewLaunchOptions): string;
  /** The discovered crew names. */
  listCrews(): readonly string[];
}

/** Options of {@link ScriptHostFacade.runCrewAsync}. */
export interface CrewLaunchOptions {
  /**
   * `"continue"`: called from a `completed`, the new instance takes the chain depth of the instance whose
   * `completed` runs instead of that depth plus one, so a driver that relaunches its own work after each
   * cycle (command -> crew -> the same command's `completed` -> crew) is not stopped by `MaxChainDepth`.
   * The parent is always an instance of the same command: a command runs only the `completed` of its own
   * instances, and what that `completed` launches is keyed to it. Outside a `completed` (a `dispatch`, a
   * handler) it changes nothing: the launch takes the depth it takes without it (a first hop is depth 1).
   * Any other value throws.
   */
  chain?: "continue";
}

/**
 * The session's tool-call rules — `ctx.services.get("permissions")`, in `orkeon-repl`. The permission
 * gate reads them before the mode: a deny rule refuses a call in every mode, an allow rule lets it run
 * without asking in every mode but `plan`. A rule covers every call of a tool, or the calls whose
 * argument matches a pattern whole, `*` standing for any run of characters. They last until the REPL
 * exits.
 */
export interface PermissionRulesFacade {
  /**
   * Whether the host registered a permission gate (`Orkeon:Security:PermissionGate:Enabled = true`).
   * False, the gate does not run: the session's rules and the permission modes check nothing, and every
   * tool call runs. A crew reads the same fact as `ctx.llm.permissionGate.enabled`.
   */
  readonly enabled: boolean;
  /** Lets the calls run without asking: `allow("shell_command", "command", "npm test*")`. */
  allow(tool: string, argument?: string, pattern?: string): void;
  /** Refuses the calls in every mode: `deny("shell_command", "command", "git push*")`. */
  deny(tool: string, argument?: string, pattern?: string): void;
  /** Removes every rule. */
  clear(): void;
  /** The rules in force, one line each: `allow shell_command command=npm test*`. */
  list(): readonly string[];
}

/**
 * The tool approvals waiting for the operator — `ctx.services.get("approvals")`, in `orkeon-repl`.
 * Read-only: the operator answers them. Each `list()` reads the questions waiting at that moment.
 */
export interface PendingApprovals {
  /** The approvals waiting, the one the next answer settles first; empty when none waits. */
  list(): readonly PendingApproval[];
}

/**
 * The REPL's persisted session — `ctx.services.get("session")`, in `orkeon-repl` while
 * `Orkeon:Cli:Session:Persist` is on (the REPL's default). Paths are virtual (`/workspace/...`).
 */
export interface SessionFacade {
  /**
   * Writes the session now and resolves to the virtual path written. Without `path`, to its own file —
   * the file and the shape the REPL writes at exit (`<SessionsDirectory>/<project>/last.json`). With
   * `path`, to that virtual file instead, in the same shape, so {@link SessionFacade.load} reads it back.
   * A blank `path` rejects. The session goes on saving to its own file, so archive the current one with
   * `save(path)` before a `load`: the loaded session overwrites the own file at the next save.
   */
  save(path?: string): Promise<string>;
  /**
   * Swaps the conversation buffer and the state bag (with the session's id and title) for those of a
   * file the REPL saved, and resolves to the number of messages loaded: the next turn continues the
   * loaded conversation. The running command instances (`commands.list`) are untouched, and the
   * session goes on saving to its own file (archive the current one first with `save(path)`). A
   * missing file, or one not of the shape the REPL writes, rejects and leaves the session as it was.
   * A load while another command's turn runs is not supported: the last writer wins.
   */
  load(path: string): Promise<number>;
}

/** One tool approval waiting for the operator. */
export interface PendingApproval {
  /** The ticket of the background task that asks (`t1`); null outside a task. */
  readonly taskId: string | null;
  /** The name of the command that runs that task (`dev-batches`); null outside a task. */
  readonly command: string | null;
  /** The tool the call names (`shell_command`). */
  readonly tool: string;
  /** The call's input as the status line shows it: its JSON arguments, cut at 60 characters with `…`. */
  readonly input: string;
}

/**
 * The runtime that runs the command — `ctx.services.get("runtime")`, always present. The version and
 * the channel are the ones `orkeon doctor` prints; `version + "+" + commit` is the full build version.
 */
export interface RuntimeInfo {
  /** The release, build metadata removed (`1.0.0-rc.6`); `"unknown"` when the host carries none. */
  readonly version: string;
  /** The commit sha the build was made from (the metadata after `+`); null when the version carries none. */
  readonly commit: string | null;
  /**
   * The channel the runtime was installed through: `dotnet-tool`, `deb`, `tarball`, `zip`, `msi`,
   * `homebrew`, `source`…, `"unknown"` when nothing names one (a build tree, an installation older than the marker).
   */
  readonly channel: string;
}

/**
 * The session's LLM usage — `ctx.services.get("usage")`, present when the host keeps a cost manager or
 * command instances (`orkeon-repl`). Read-only. Each `report()` reads the figures of that moment.
 */
export interface SessionUsage {
  /** The session's usage now: its totals, one row per model, one row per command instance. */
  report(): SessionUsageReport;
}

/** The token split every usage row carries. The cache counts are a part of the prompt, the reasoning a part of the completion. */
export interface UsageTokens {
  readonly promptTokens: number;
  readonly completionTokens: number;
  /** Prompt tokens served from the provider's cache; 0 when the provider reports none. */
  readonly cacheHitTokens: number;
  /** Prompt tokens written to the provider's cache; 0 when the provider reports none. */
  readonly cacheWriteTokens: number;
  /** Completion tokens spent reasoning (Z.AI, OpenRouter report them); 0 when the provider reports none. */
  readonly reasoningTokens: number;
}

/** What {@link SessionUsage.report} returns. */
export interface SessionUsageReport extends UsageTokens {
  /** `promptTokens + completionTokens`. */
  readonly totalTokens: number;
  /** The LLM calls of the session. */
  readonly calls: number;
  /** The calls nobody priced (no vendor charge, no registry price): `cost` counts them as 0, a lower bound while this is not 0. */
  readonly unpricedCalls: number;
  /** The session's cost (USD for the registry's prices); null when no price is known for any call it made. */
  readonly cost: number | null;
  /**
   * One row per model, by name. A call whose provider named no model has no row: the session's totals count it,
   * so they may exceed the sum of these rows.
   */
  readonly byModel: readonly ModelUsageRow[];
  /** One row per command instance the session keeps (a crew run with `runCrewAsync` included), the newest first. */
  readonly byCommand: readonly CommandUsageRow[];
}

/** The usage of one model. */
export interface ModelUsageRow extends UsageTokens {
  /** The model, as the provider named it in its answer. */
  readonly model: string;
  /** The LLM calls of the model. */
  readonly calls: number;
  /** The calls nobody priced: `cost` counts them as 0. */
  readonly unpricedCalls: number;
  /** The model's cost; null when the vendor billed nothing and no price is configured for it. */
  readonly cost: number | null;
}

/** The usage of one command instance. */
export interface CommandUsageRow extends UsageTokens {
  /** The instance's ticket (`t1`). */
  readonly ticket: string;
  /** The command's name. */
  readonly name: string;
  /** The instance's state. */
  readonly state: CommandInstanceState;
  /** The LLM calls of the instance. */
  readonly calls: number;
  /** The calls nobody priced: `cost` counts them as 0. */
  readonly unpricedCalls: number;
  /** The instance's cost; null while none of its calls was priced. */
  readonly cost: number | null;
}

/** Result of a crew run launched via {@link ScriptHostFacade}. */
export interface CrewRunOutput {
  readonly ok: boolean;
  readonly summary?: string;
  readonly artifacts?: unknown;
  readonly error?: string;
  /**
   * True when any turn of this run was written to the console in the foreground; background and
   * labelled output report false: streaming off, a crew task's lines (`[task] `), and `runCrewAsync`.
   */
  readonly streamed: boolean;
}

// ── Args schema: `args: { who: { type: "string" } }` gives the handler `args.who: string`. ──

export type ArgSpec =
  | { type: "string"; required?: boolean; default?: string; choices?: readonly string[] }
  | { type: "number"; required?: boolean; default?: number; min?: number; max?: number }
  | { type: "boolean"; required?: boolean; default?: boolean }
  | { type: "string[]"; required?: boolean; default?: readonly string[] };

export type ArgsSchema = Readonly<Record<string, ArgSpec>>;

export type ParsedArgs<TArgs> = TArgs extends ArgsSchema
  ? { readonly [K in keyof TArgs]: ResolveArgType<TArgs[K]> }
  : { readonly raw: readonly string[] };

export type ResolveArgType<S> =
  S extends { type: "string" } ? string
  : S extends { type: "number" } ? number
  : S extends { type: "boolean" } ? boolean
  : S extends { type: "string[]" } ? readonly string[]
  : unknown;
