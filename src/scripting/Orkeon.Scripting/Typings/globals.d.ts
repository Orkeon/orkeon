// Orkeon Scripting DSL - The globals the runner reads and writes
// Declares `crew`, `inputs` and `result`: the three names a script exchanges with its host.
//
// Added on 2026-09-07. These were undeclared, which is why every declarative crew in the
// repository ends with the cast `(globalThis as any).crew = crew` -- the central mechanism
// of the declarative shape was invisible to the type system. With this file the cast is
// unnecessary: `globalThis.crew = crew` type-checks, and assigning something that is not a
// Crew is now an error at authoring time instead of a message from the runner.

declare global {
    /**
     * The DECLARATIVE handoff. Assign a built crew here INSTEAD of calling `crew.run()`,
     * and the runner drives it: tasks, `process` and the manager are honoured, and
     * `orkeon run --validate` can check the definition without executing anything.
     *
     * The two shapes are exclusive, and the runner picks by looking for this assignment in
     * the source. A script that assigns it never runs agent `.body()` functions; a script
     * that awaits `crew.run()` instead never honours `withTask`. Do not do both.
     */
    var crew: Crew | undefined;

    /**
     * What `--inputs` / `--inputs-file` parsed, planted before the script is evaluated.
     * `undefined` when the run passed neither. Only the procedural shape sees it: the
     * declarative handoff goes through the shared runner, which does not plant inputs.
     */
    var inputs: Record<string, unknown> | undefined;

    /**
     * What the runner reports as the run's result. Optional: the value of the last
     * expression is used when a script never assigns it. Whatever goes here is serialised
     * to JSON, so assign a plain projection -- a `CrewResult` holds host objects and does
     * not survive the trip.
     */
    var result: unknown;
}

export { };
