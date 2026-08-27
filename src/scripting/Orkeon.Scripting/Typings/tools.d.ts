// Orkeon Scripting DSL — Built-in tools namespace
// Surface mirrors `IBaseTool` implementations under `Orkeon.Tools.*`. The exhaustive
// signatures land alongside each builtin tool in SCR-11.

declare global {
    namespace tools {
        function fileRead(input: { path: string; encoding?: string }, ctx?: ExecutionContext): Promise<{ content: string }>;
        function fileWrite(input: { path: string; content: string }, ctx?: ExecutionContext): Promise<{ bytes: number }>;
        function directoryRead(input: { path: string; recursive?: boolean }, ctx?: ExecutionContext): Promise<{ entries: readonly string[] }>;
        function webScrape(input: { url: string; selector?: string }, ctx?: ExecutionContext): Promise<{ html: string }>;
        function httpApi(input: { url: string; method?: string; body?: unknown; headers?: Record<string, string> }, ctx?: ExecutionContext): Promise<{ status: number; body: unknown }>;
        function searchTool(input: { query: string; topK?: number }, ctx?: ExecutionContext): Promise<{ results: readonly unknown[] }>;
        function databaseQuery(input: { connection: string; sql: string; params?: readonly unknown[] }, ctx?: ExecutionContext): Promise<{ rows: readonly Record<string, unknown>[] }>;
        function delegateWork(input: { agent: string; task: string }, ctx?: ExecutionContext): Promise<{ output: unknown }>;
        function askQuestion(input: { question: string }, ctx?: ExecutionContext): Promise<{ answer: string }>;
    }

    /**
     * Tools not enumerated above are still reachable at runtime — the `tools` object carries
     * every registered `IBaseTool` — but they are untyped here.
     *
     * There used to be an index signature in the namespace above, written as
     * `const [name: string]: …`. A TypeScript namespace cannot carry an index signature and
     * that is not a declaration at all: the file did not parse, so the whole `tools` namespace
     * this package advertises was unavailable to every editor that loaded it. Nothing noticed,
     * because the only test over the shipped typings asserted that certain substrings were
     * present in them.
     */
    const tools: Record<string, (input: unknown, ctx?: ExecutionContext) => Promise<unknown>>;
}

export { };
