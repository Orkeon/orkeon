using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using Orkeon.Scripting.Bindings;
using Orkeon.Scripting.Builders;
using Orkeon.Scripting.ErrorPolicy;
using Orkeon.Scripting.Runtime;

namespace Orkeon.Scripting.Tests.Typings;

/// <summary>
/// The typings and the runtime are written in two languages (GAP-12). The example typecheck
/// (<c>scripts/check-scripting-typings.sh</c>) proves that code known to run also compiles; it
/// cannot see a member the declarations promise and the runtime lacks, nor a member the runtime
/// offers that no declaration mentions. This compares the two surfaces by name: the members a
/// <c>.d.ts</c> interface declares against the public, JS-cased members of the CLR type Jint
/// projects for it.
/// </summary>
/// <remarks>
/// A name match is all it checks — not parameter shapes. It is the cheap half of the contract,
/// and the half that drifted most: <c>agent.role</c> declared and absent, <c>withResponseFormat</c>
/// present and undeclared, <c>withTaskTool</c> declared and read by nothing.
/// </remarks>
public sealed partial class TypingsRuntimeParityTests
{
    /// <summary>Each declared surface and the CLR type that implements it.</summary>
    private static readonly Dictionary<string, Type> s_pairs = new(StringComparer.Ordinal)
    {
        ["AgentBuilder"] = typeof(JsAgentBuilder),
        ["Agent"] = typeof(JsAgent),
        ["TaskBuilder"] = typeof(JsTaskBuilder),
        ["Task"] = typeof(JsTask),
        ["CrewBuilder"] = typeof(JsCrewBuilder),
        ["Crew"] = typeof(JsCrew),
        ["CrewView"] = typeof(JsCrewProxy),
        ["ExecutionContext"] = typeof(JsExecutionContext),
        ["AgentContext"] = typeof(JsAgentContext),
        ["Logger"] = typeof(JsLogger),
        ["MemoryScope"] = typeof(JsMemoryScope),
        ["LlmFacade"] = typeof(JsLlmFacade),
        ["LlmConfig"] = typeof(JsLlmConfig),
        ["namespace llm"] = typeof(JsLlmNamespace),
        ["ErrorContext"] = typeof(JsErrorContext),
        ["EventQueue"] = typeof(JsEventQueue),
        ["EventTopic"] = typeof(JsEventTopic),
        ["PublishedEvent"] = typeof(JsPublishedEvent),
    };

    public static TheoryData<string> Pairs() => new(s_pairs.Keys.Order(StringComparer.Ordinal));

    /// <summary>
    /// The explicit exceptions: a member on one side with no counterpart on the other, ON
    /// PURPOSE. Every entry says why. An entry that no longer matches anything fails the test
    /// too, so the table cannot rot into a list of things that were fixed long ago.
    /// </summary>
    private static readonly (string Declared, string Member, string Why)[] s_exceptions =
    [
        // `default` is a reserved word: a TypeScript namespace cannot declare `const default`,
        // so the declaration and the runtime both spell it `default_`. Nothing to except.
    ];

    [Theory]
    [MemberData(nameof(Pairs))]
    public void Declared_members_and_runtime_members_match(string declared)
    {
        var typings = LoadTypings();
        var declaredMembers = declared.StartsWith("namespace ", StringComparison.Ordinal)
            ? NamespaceMembers(typings, declared["namespace ".Length..])
            : InterfaceMembers(typings, declared);
        var runtimeMembers = RuntimeMembers(s_pairs[declared]);

        var excepted = s_exceptions.Where(e => e.Declared == declared).Select(e => e.Member).ToHashSet(StringComparer.Ordinal);

        var undeclared = runtimeMembers.Except(declaredMembers).Except(excepted).Order(StringComparer.Ordinal).ToList();
        var missing = declaredMembers.Except(runtimeMembers).Except(excepted).Order(StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0 && missing.Count == 0,
            $"`{declared}` and {s_pairs[declared].Name} disagree.\n" +
            $"  On the runtime, not declared: {string.Join(", ", undeclared)}\n" +
            $"  Declared, not on the runtime: {string.Join(", ", missing)}");
    }

    [Fact]
    public void Every_exception_still_names_a_real_mismatch()
    {
        var typings = LoadTypings();
        foreach (var (declared, member, why) in s_exceptions)
        {
            var declaredMembers = declared.StartsWith("namespace ", StringComparison.Ordinal)
                ? NamespaceMembers(typings, declared["namespace ".Length..])
                : InterfaceMembers(typings, declared);
            var runtimeMembers = RuntimeMembers(s_pairs[declared]);
            Assert.True(declaredMembers.Contains(member) != runtimeMembers.Contains(member),
                $"The exception `{declared}.{member}` ({why}) no longer names a mismatch; remove it.");
        }
    }

    private static readonly string[] s_probeMembers =
        ["callback", "generic", "method", "optional", "overload", "plain", "withObject"];

    [Fact]
    public void The_parser_sees_a_member_added_to_a_builder()
    {
        // The guard's own guard: a declaration parsed to nothing would agree with nothing and
        // report every runtime member, but a parser that silently skipped a member shape would
        // not. A synthetic interface carrying each shape the typings use must parse whole.
        const string source = """
            declare global {
                interface Probe<T = unknown> {
                    readonly plain: string;
                    optional?: number;
                    method(value: string): this;
                    generic<S>(factory: (() => S) | S): Probe<S>;
                    overload(a: string): this;
                    overload(a: string, b: (env: { x: number }) => void): this;
                    /** doc { with braces } */
                    withObject(opts?: { delay?: number | string; max?: number }): void;
                    callback(fn: (input: T) => Promise<T> | T): this;
                }
            }
            """;
        var members = InterfaceMembers(source, "Probe");
        Assert.Equal(s_probeMembers, members.Order(StringComparer.Ordinal));
    }

    // ---- typings side -------------------------------------------------------------------

    private static string LoadTypings()
    {
        var builder = new StringBuilder();
        foreach (var file in Directory.EnumerateFiles(TypingsDirectory(), "*.d.ts").Order(StringComparer.Ordinal))
            builder.AppendLine(File.ReadAllText(file));
        return builder.ToString();
    }

    private static HashSet<string> InterfaceMembers(string source, string name)
    {
        var text = StripComments(source);
        var match = InterfaceHeader().Matches(text).FirstOrDefault(m => m.Groups["name"].Value == name);
        Assert.True(match is not null, $"interface {name} is not declared in Typings/*.d.ts.");

        var members = TopLevelMembers(Body(text, match.Index + match.Length - 1))
            .Select(m => MemberName().Match(m))
            .Where(m => m.Success)
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);

        // `interface AgentContext<T> extends ExecutionContext` carries its base's members.
        var head = match.Groups["head"].Value;
        var extends = ExtendsClause().Match(head);
        if (extends.Success)
        {
            foreach (var baseName in GenericArguments().Replace(extends.Groups["bases"].Value, "").Split(','))
                members.UnionWith(InterfaceMembers(source, baseName.Trim()));
        }
        return members;
    }

    private static HashSet<string> NamespaceMembers(string source, string name)
    {
        var text = StripComments(source);
        var match = NamespaceHeader().Matches(text).FirstOrDefault(m => m.Groups["name"].Value == name);
        Assert.True(match is not null, $"namespace {name} is not declared in Typings/*.d.ts.");
        return NamespaceMember().Matches(Body(text, match.Index + match.Length - 1))
            .Select(m => m.Groups["name"].Value)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>The text between the brace at <paramref name="open"/> and its match.</summary>
    private static string Body(string text, int open)
    {
        var depth = 0;
        for (var i = open; i < text.Length; i++)
        {
            if (text[i] == '{') depth++;
            else if (text[i] == '}' && --depth == 0)
                return text[(open + 1)..i];
        }
        throw new InvalidOperationException("Unbalanced braces in Typings/*.d.ts.");
    }

    /// <summary>Splits an interface body on the <c>;</c> that end its top-level members.</summary>
    private static IEnumerable<string> TopLevelMembers(string body)
    {
        var depth = 0;
        var start = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var c = body[i];
            if (c is '{' or '(' or '[') depth++;
            else if (c is '}' or ')' or ']') depth--;
            else if (c == ';' && depth == 0)
            {
                yield return body[start..i];
                start = i + 1;
            }
        }
        if (start < body.Length)
            yield return body[start..];
    }

    private static string StripComments(string source)
        => LineComment().Replace(BlockComment().Replace(source, " "), " ");

    [GeneratedRegex(@"\binterface\s+(?<name>[A-Za-z_$][\w$]*)\b(?<head>[^{]*)\{")]
    private static partial Regex InterfaceHeader();

    [GeneratedRegex(@"\bnamespace\s+(?<name>[A-Za-z_$][\w$]*)\s*\{")]
    private static partial Regex NamespaceHeader();

    [GeneratedRegex(@"\b(?:const|function)\s+(?<name>[A-Za-z_$][\w$]*)")]
    private static partial Regex NamespaceMember();

    [GeneratedRegex(@"\bextends\s+(?<bases>[\w\s,<>]+)$")]
    private static partial Regex ExtendsClause();

    [GeneratedRegex("<[^>]*>")]
    private static partial Regex GenericArguments();

    [GeneratedRegex(@"/\*.*?\*/", RegexOptions.Singleline)]
    private static partial Regex BlockComment();

    [GeneratedRegex(@"//[^\n]*")]
    private static partial Regex LineComment();

    [GeneratedRegex(@"^\s*(?:readonly\s+)?(?<name>[A-Za-z_$][\w$]*)\s*\??\s*[(<:]")]
    private static partial Regex MemberName();

    // ---- runtime side -------------------------------------------------------------------

    /// <summary>
    /// What a script reaches on an instance: public instance properties and methods, the type's
    /// own and inherited, JS-cased. A PascalCase member is the CLR-facing contract (a host's
    /// <c>RunAsync</c>, <c>IDisposable.Dispose</c>) and is not part of the script surface.
    /// </summary>
    private static HashSet<string> RuntimeMembers(Type type)
    {
        const BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;
        var names = type.GetProperties(flags).Select(p => p.Name)
            .Concat(type.GetMethods(flags).Where(m => !m.IsSpecialName).Select(m => m.Name))
            .Where(n => char.IsLower(n[0]))
            .Where(n => typeof(object).GetMethod(n) is null);
        return names.ToHashSet(StringComparer.Ordinal);
    }

    private static string TypingsDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "src", "scripting", "Orkeon.Scripting", "Typings");
            if (Directory.Exists(candidate))
                return candidate;
            directory = directory.Parent;
        }
        throw new DirectoryNotFoundException("src/scripting/Orkeon.Scripting/Typings not found above the test output.");
    }
}
