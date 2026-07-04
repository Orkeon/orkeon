// Orkeon Scripting DSL — Custom tool builder
// See chapter 03 §custom tools.

declare global {
    interface ToolSchema {
        readonly input: JsonSchema;
        readonly output: JsonSchema;
    }

    interface Tool<TIn = unknown, TOut = unknown> {
        readonly name: string;
        readonly description: string;
        execute(input: TIn, ctx: ExecutionContext): Promise<TOut>;
    }

    interface ToolBuilder<TIn = unknown, TOut = unknown> {
        name(value: string): this;
        description(value: string): this;
        withSchema(schema: ToolSchema): this;
        execute(fn: (input: TIn, ctx: ExecutionContext) => Promise<TOut> | TOut): this;
        build(): Tool<TIn, TOut>;
    }

    function toolBuilder<TIn = unknown, TOut = unknown>(): ToolBuilder<TIn, TOut>;
}

export { };
