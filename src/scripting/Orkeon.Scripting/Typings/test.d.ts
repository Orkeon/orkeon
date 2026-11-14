// Orkeon Scripting DSL — the `test` namespace
// Declares the mocks and assertions a script reads under `orkeon run --test`: `test.mockLlm`,
// `test.mockTool(...)`, `test.assertLlmCalled(...)` and `test.assertToolCalled(...)`.
//
// The global exists ONLY under `orkeon run --test`. A script run without the flag has no `test`:
// referencing it fails with a ReferenceError, so a mock never hides in a production run. Under
// `--test` the host's configured LLM (or the `mock` profile of `--llm-mock`) stays behind the mock:
// a prompt an expectation of `test.mockLlm` fits is answered by it, any other prompt goes to the
// configured provider.

declare global {
    /** What a mocked tool or model answers for the calls an expectation matched. */
    interface MockLlmExpectation {
        /**
         * The answer: a string, an object carrying a `content` string, or any value (stringified as a
         * last resort).
         */
        respond(response: string | { readonly content: string } | unknown): void;
    }

    /** `test.mockLlm`: opens expectations on the model the script runs on. */
    interface MockLlmHandle {
        /**
         * Starts an expectation for prompts that contain the string (case ignored), or match the regular
         * expression, of `{ prompt: ... }`. The first expectation that fits a prompt answers it. Complete
         * it with `.respond(...)`.
         */
        when(matcher: string | { readonly prompt: string | RegExp }): MockLlmExpectation;
    }

    /** What one mocked tool returns for the calls an expectation matched. */
    interface MockToolExpectation {
        /** The value the tool returns for the matched calls. */
        respond(response: unknown): void;
    }

    /** `test.mockTool(name)`: one mocked tool, created on first use. */
    interface MockToolHandle {
        /**
         * Starts an expectation for calls whose parameters equal the object, key by key. Complete it with
         * `.respond(...)`.
         */
        when(matcher: Readonly<Record<string, unknown>>): MockToolExpectation;

        /** The value the tool returns when no expectation matches. */
        respond(response: unknown): void;
    }

    /** The options of `test.assertLlmCalled`. */
    interface AssertLlmCalledOptions {
        /** The exact number of calls; left out, at least one is required. */
        readonly times?: number;
        /** Count only the calls whose prompt contains this text (case ignored). */
        readonly with?: string;
    }

    /** The options of `test.assertToolCalled`. */
    interface AssertToolCalledOptions {
        /** The exact number of calls; left out, at least one is required. */
        readonly times?: number;
    }

    namespace test {
        /** Opens expectations on the model: `test.mockLlm.when("weather").respond("sunny")`. */
        const mockLlm: MockLlmHandle;

        /** The mocked tool named `name`, created on first use. */
        function mockTool(name: string): MockToolHandle;

        /**
         * Throws when the model was not called as `options` says: an `Error` the script catches,
         * its `clrType` `"AssertionException"`.
         */
        function assertLlmCalled(options?: AssertLlmCalledOptions): void;

        /**
         * Throws when the mocked tool `name` was not called as `options` says, or is unknown: an
         * `Error` the script catches, its `clrType` `"AssertionException"`.
         */
        function assertToolCalled(name: string, options?: AssertToolCalledOptions): void;
    }
}

export { };
