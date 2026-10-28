// Orkeon Scripting DSL — Task builder
// Declares Task, TaskBuilder, and the deliverable contract a task can attach to its output.

declare global {
    interface Task<TIn = unknown, TOut = unknown> {
        readonly name: string;
        readonly id: string;
        readonly description: string;
        readonly expectedOutput: string;
    }

    interface TaskBuilder<TIn = unknown, TOut = unknown> {
        name(value: string): this;
        description(value: string): this;
        /**
         * The built agent that performs this task, one of the crew's (`withAgent`): an agent the
         * crew does not hold is refused when the crew is loaded.
         */
        agent(agent: Agent<TIn, TOut>): this;
        expectedOutput(value: string): this;
        /**
         * A task this one waits for and reads, one of the crew's (`withTask`): a task the crew does
         * not hold is refused when the crew is loaded.
         */
        withContext(task: Task<unknown, unknown>): this;
        withContexts(tasks: readonly Task<unknown, unknown>[]): this;
        expect(schema: JsonSchema): this;
        /** Forces this task's output format (overrides the agent's). `"text"` keeps the provider default. */
        withResponseFormat(type: ResponseFormatType): this;
        /**
         * Constrains this task's output to a JSON Schema, on the providers whose API validates
         * one server-side. Implies `json_schema`. `strict` defaults to true.
         */
        withResponseSchema(name: string, schema: JsonSchema | string, strict?: boolean): this;
        /**
         * YAML parity `llm_override: { profile }` — runs this task alone on one of the host's
         * named LLM profiles (`Llm:Profiles:<name>`), on that profile's own model; its agent's
         * other tasks stay where they were. `"default"` brings the task back to the host's default
         * profile. A name the host does not offer fails the load, listing the known ones.
         */
        withProfile(name: string): this;
        /** YAML parity `humanInput: true` — the task pauses for the human-input provider. */
        humanInput(value?: boolean): this;
        /**
         * YAML parity `asyncExecution: true` — in a `.process("sequential")` crew the task runs
         * alongside the tasks after it, and a task that lists it in `withContext` waits for it.
         * `.process("parallel")` accepts it without an effect of its own (a wave already runs at
         * once); the four other modes refuse the crew, naming the task.
         */
        asyncExecution(value?: boolean): this;
        /**
         * YAML parity task-level `tools:` — names, `toolBuilder()` instances, or an
         * array mixing both. They ADD to the assigned agent's own tools, for this task
         * only; they never replace them. Instances are registered with the runtime
         * registry by the loader, so their names resolve like built-ins, and an unknown
         * name fails the load under `Orkeon:CrewFactory:StrictTools` like an agent's.
         */
        tools(value: string | Tool<unknown, unknown> | readonly (string | Tool<unknown, unknown>)[]): this;
        /**
         * First-class deliverable contract — mirror of YAML's `deliverable: { ... }`
         * block. The framework writes the produced output to `path` and (when the
         * source is `structured_output`) validates against the inline `schema`.
         */
        deliverable(spec: TaskDeliverableSpec): this;
        /**
         * Makes this task a decision task: it asks the LLM the listed questions and records the
         * answers, which later tasks branch on with `when`.
         */
        decision(spec: DecisionSpec): this;
        /**
         * Runs this task only when the named decision task's answers match `clauses`, or
         * (`"otherwise"`) when no other task gated on that decision matches.
         */
        when(decisionTask: string, clauses: WhenClauses | "otherwise"): this;
        build(): Task<TIn, TOut>;
    }

    /** Decision contract of a task: the questions asked, the confidence floor and the fallbacks. */
    interface DecisionSpec {
        questions: Record<string, QuestionSpec>;
        /** Answers below this confidence fall back to `fallback`. */
        minConfidence?: number;
        /** Names of the state keys the decision reads. */
        state?: string[];
        longContext?: boolean;
        /** Question name to the option taken when the answer is not confident enough. */
        fallback?: Record<string, string>;
    }

    /** One question: a choice among described options, a score per label, or a yes/no ("noul") test. */
    type QuestionSpec =
        | { choice: Record<string, string> }
        | { score: string[]; instructions?: string }
        | { noul: string };

    /**
     * Branch clauses: `question` maps to an option list or a threshold such as `">= 0.7"`;
     * `"question.option"` maps to a threshold such as `">= 0.25"`.
     */
    type WhenClauses = Record<string, string[] | string>;

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
