// Orkeon Scripting DSL — Task builder
// See chapter 03 (creation-agent-crew-task.md).

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
        /**
         * First-class deliverable contract — mirror of YAML's `deliverable: { ... }`
         * block. The framework writes the produced output to `path` and (when the
         * source is `structured_output`) validates against the inline `schema`.
         */
        deliverable(spec: TaskDeliverableSpec): this;
        when(predicate: () => boolean): this;
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
