// Orkeon Scripting DSL — Task builder
// Declares Task, TaskBuilder, and the deliverable contract a task can attach to its output.

declare global {
    interface Task<TIn = unknown, TOut = unknown> {
        readonly name: string;
        readonly description: string;
    }

    interface TaskBuilder<TIn = unknown, TOut = unknown> {
        name(value: string): this;
        description(value: string): this;
        agent(agent: string | Agent<TIn, TOut>): this;
        expectedOutput(value: string): this;
        withContext(task: Task<unknown, unknown>): this;
        withContexts(tasks: readonly Task<unknown, unknown>[]): this;
        expect(schema: JsonSchema): this;
        withTaskTool(tool: Tool<unknown, unknown>): this;
        /** YAML parity `humanInput: true` — the task pauses for the human-input provider. */
        humanInput(value?: boolean): this;
        /** YAML parity `asyncExecution: true` — the task may run concurrently with its siblings. */
        asyncExecution(value?: boolean): this;
        /**
         * YAML parity task-level `tools:` — names, `toolBuilder()` instances, or an
         * array mixing both. Instances are registered with the runtime registry by
         * the loader, so their names resolve like built-ins.
         */
        tools(value: string | Tool<unknown, unknown> | readonly (string | Tool<unknown, unknown>)[]): this;
        /**
         * First-class deliverable contract — mirror of YAML's `deliverable: { ... }`
         * block. The framework writes the produced output to `path` and (when the
         * source is `structured_output`) validates against the inline `schema`.
         */
        deliverable(spec: TaskDeliverableSpec): this;
        build(): Task<TIn, TOut>;
    }

    /**
     * Deliverable contract for a task. `schema` (inline object) is JSON-serialized
     * into `schemaInline` by the adapter — both fields are accepted, inline `schema`
     * wins when both are provided.
     */
    interface TaskDeliverableSpec {
        path: string;
        source: "final_message" | "structured_output" | "tool_call" | "none";
        format?: "markdown" | "json" | "text";
        sanitize?: boolean;
        schemaPath?: string;
        schema?: object;
        schemaInline?: string;
    }

    function taskBuilder<TIn = unknown, TOut = unknown>(): TaskBuilder<TIn, TOut>;
}

export { };
