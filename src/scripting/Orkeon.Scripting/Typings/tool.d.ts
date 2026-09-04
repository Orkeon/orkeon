// Orkeon Scripting DSL — Custom tool builder
// See chapter 03 §custom tools.

declare global {
    /**
     * Two accepted shapes: a bare JSON schema (`{ type, properties, required }`)
     * or an input/output pair. Per property, `type`, `description`, `default`,
     * `enum`, `format`, `items` and `example` are all honored — the schema is
     * what the LLM sees when it decides how to call the tool.
     */
    type ToolSchemaSpec = JsonSchema | { input: JsonSchema; output?: JsonSchema };

    interface Tool<TIn = unknown, TOut = unknown> {
        readonly name: string;
        readonly description: string;
        /**
         * Direct invocation from a script body. Returns the callback's raw
         * result — a value, or a Promise to `await`. `ctx` is forwarded to the
         * callback verbatim; when omitted the callback receives `undefined`.
         */
        execute(input: TIn, ctx?: unknown): TOut | Promise<TOut>;
    }

    interface ToolBuilder<TIn = unknown, TOut = unknown> {
        name(value: string): this;
        description(value: string): this;
        withSchema(schema: ToolSchemaSpec): this;
        /**
         * The tool body. When the LLM calls the tool through the orchestration
         * pipeline, `ctx` is `undefined` — reach shared capabilities through the
         * `tools.*` / `rag.*` globals instead. A script calling
         * `myTool.execute(input, something)` forwards its own second argument.
         */
        execute(fn: (input: TIn, ctx?: unknown) => Promise<TOut> | TOut): this;
        /**
         * Declared access class for permission gates. Undeclared tools are
         * treated fail-closed (as writes) by gated autonomous calls.
         */
        access(value: "read" | "edit" | "execute"): this;
        build(): Tool<TIn, TOut>;
    }

    function toolBuilder<TIn = unknown, TOut = unknown>(): ToolBuilder<TIn, TOut>;
}

export { };
