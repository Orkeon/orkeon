// Custom tools for the review desk, written in TypeScript and handed to agents as
// instances. Same shape as examples/03-finance-trading/_tools/ — a module that builds the
// tools once and a `pickTools` that fails loudly on a typo, so a name that no longer exists
// stops the run instead of quietly leaving an agent one tool short.

// `toolBuilder<TIn, TOut>` states what the schema promises. TypeScript never reads the
// schema, so the generic is what makes `input.diff` legal instead of an error on `unknown`.
const diffStats = toolBuilder<{ diff: string }, { added: number; removed: number; net: number }>()
    .name("diff_stats")
    .description("Counts added and removed lines in a unified diff")
    .withSchema({
        type: "object",
        properties: { diff: { type: "string", description: "Unified diff text" } },
        required: ["diff"]
    })
    .execute((input) => {
        const lines = String(input.diff).split("\n");
        const added = lines.filter((l) => l.startsWith("+") && !l.startsWith("+++")).length;
        const removed = lines.filter((l) => l.startsWith("-") && !l.startsWith("---")).length;
        return { added, removed, net: added - removed };
    })
    .build();

const touchedFiles = toolBuilder<{ diff: string }, { files: string[]; count: number }>()
    .name("touched_files")
    .description("Lists the files a unified diff touches")
    .withSchema({
        type: "object",
        properties: { diff: { type: "string", description: "Unified diff text" } },
        required: ["diff"]
    })
    .execute((input) => {
        const files = String(input.diff)
            .split("\n")
            .filter((l) => l.startsWith("+++ b/"))
            .map((l) => l.slice("+++ b/".length));
        return { files, count: files.length };
    })
    .build();

const riskFlags = toolBuilder<{ diff: string }, { flags: string[]; clean: boolean }>()
    .name("risk_flags")
    .description("Flags patterns that deserve a second look in a diff")
    .withSchema({
        type: "object",
        properties: { diff: { type: "string", description: "Unified diff text" } },
        required: ["diff"]
    })
    .execute((input) => {
        const patterns: Record<string, RegExp> = {
            "swallowed exception": /catch\s*\([^)]*\)\s*\{\s*\}/,
            "hard-coded secret": /(api[_-]?key|password|token)\s*=\s*["'][^"']{8,}/i,
            "disabled test": /\.(skip|only)\(/,
        };
        const hits = Object.keys(patterns).filter((k) => patterns[k].test(String(input.diff)));
        return { flags: hits, clean: hits.length === 0 };
    })
    .build();

const all = [diffStats, touchedFiles, riskFlags];

/** Every tool of this module, in declaration order. */
export const allReviewTools = all;

/**
 * The named subset an agent should carry. Throws on an unknown name rather than handing
 * back a shorter array — a silently missing tool is a bug you find at run time, in a model's
 * confused answer.
 */
export function pickTools(...names: string[]) {
    return names.map((name) => {
        const found = all.find((t) => t.name === name);
        if (!found) {
            throw new Error(
                `unknown review tool "${name}" — available: ${all.map((t) => t.name).join(", ")}`);
        }
        return found;
    });
}
