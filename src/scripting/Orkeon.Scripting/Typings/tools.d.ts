// Orkeon Scripting DSL — Built-in tools namespace
// Surface mirrors `IBaseTool` implementations under `Orkeon.Tools.*`. The exhaustive
// signatures land alongside each builtin tool in SCR-11.

declare global {
    /**
     * The `tools` object: every registered `IBaseTool`, reachable by name.
     *
     * The named members below are the built-ins with a settled signature. Tools not
     * enumerated are still reachable at runtime — hence the index signature, which is what
     * forces this to be ONE interface rather than a namespace plus a fallback declaration.
     *
     * It has been wrong twice. First as an index signature written inside a `namespace`,
     * which is not a declaration TypeScript accepts at all — the file did not parse, so the
     * whole namespace was unavailable to every editor that loaded it. Then as a `namespace
     * tools` beside a `const tools`, which do not merge either (TS2300 duplicate identifier,
     * TS2395 merged declarations must be all exported or all local). Both survived because
     * the only test over the shipped typings ran them through esbuild — a transpiler, which
     * strips types and reports neither error. The test type-checks now.
     */
    interface OrkeonTools {
        fileRead(input: { path: string; encoding?: string }, ctx?: ExecutionContext): Promise<{ content: string }>;
        fileWrite(input: { path: string; content: string }, ctx?: ExecutionContext): Promise<{ bytes: number }>;
        directoryRead(input: { path: string; recursive?: boolean }, ctx?: ExecutionContext): Promise<{ entries: readonly string[] }>;
        webScrape(input: { url: string; selector?: string }, ctx?: ExecutionContext): Promise<{ html: string }>;
        httpApi(input: { url: string; method?: string; body?: unknown; headers?: Record<string, string> }, ctx?: ExecutionContext): Promise<{ status: number; body: unknown }>;
        searchTool(input: { query: string; topK?: number }, ctx?: ExecutionContext): Promise<{ results: readonly unknown[] }>;
        databaseQuery(input: { connection: string; sql: string; params?: readonly unknown[] }, ctx?: ExecutionContext): Promise<{ rows: readonly Record<string, unknown>[] }>;
        delegateWork(input: { agent: string; task: string }, ctx?: ExecutionContext): Promise<{ output: unknown }>;
        askQuestion(input: { question: string }, ctx?: ExecutionContext): Promise<{ answer: string }>;

        /** Any other registered tool, by name. */
        [name: string]: (input: never, ctx?: ExecutionContext) => Promise<unknown>;
    }

    const tools: OrkeonTools;
}

export { };
