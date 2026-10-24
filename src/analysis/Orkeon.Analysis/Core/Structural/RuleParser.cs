using System.Collections;
using System.Globalization;
using System.Text.RegularExpressions;

namespace Orkeon.Analysis.Core.Structural;

/// <summary>Turns the dictionary form of a rule (inline JSON or YAML) into a <see cref="RuleNode"/> tree, once per call.</summary>
internal static class RuleParser
{
    public const string SupportedKeys = "kind, pattern, regex, field, inside, has, not, all, any, stopBy";

    private const string StopByKey = "stopBy";

    public static RuleNode Parse(object? rule) => ParseObject(AsObject(rule, "rule"), relational: false);

    private static RuleNode ParseObject(IDictionary obj, bool relational)
    {
        var rules = new List<RuleNode>();
        foreach (DictionaryEntry entry in obj)
        {
            var key = Convert.ToString(entry.Key, CultureInfo.InvariantCulture) ?? string.Empty;
            switch (key)
            {
                case "kind": rules.Add(new KindRule(AsString(entry.Value, key))); break;
                case "pattern": rules.Add(new PatternRule(AsString(entry.Value, key))); break;
                case "regex": rules.Add(new RegexRule(new Regex(AsString(entry.Value, key), RegexOptions.None, TimeSpan.FromSeconds(1)))); break;
                case "field": rules.Add(new FieldRule(AsString(entry.Value, key))); break;
                case "inside": rules.Add(ParseRelational(Relation.Inside, entry.Value)); break;
                case "has": rules.Add(ParseRelational(Relation.Has, entry.Value)); break;
                case "not": rules.Add(new NotRule(Parse(entry.Value))); break;
                case "all": rules.Add(new AllRule(AsList(entry.Value, key).Select(Parse).ToList())); break;
                case "any": rules.Add(new AnyRule(AsList(entry.Value, key).Select(Parse).ToList())); break;
                case StopByKey when relational: break;
                case StopByKey: throw new ArgumentException("'stopBy' is only valid inside 'inside' or 'has'.", nameof(obj));
                default: throw new UnsupportedRuleKeyException(key);
            }
        }

        if (rules.Count == 0) throw new ArgumentException("A rule needs at least one key.", nameof(obj));
        return rules.Count == 1 ? rules[0] : new AllRule(rules);
    }

    private static RelationalRule ParseRelational(Relation relation, object? value)
    {
        var obj = AsObject(value, relation == Relation.Has ? "has" : "inside");
        var stop = obj.Contains(StopByKey) ? ParseStop(obj[StopByKey]) : new StopBy(false, null);
        return new RelationalRule(relation, ParseObject(obj, relational: true), stop);
    }

    private static StopBy ParseStop(object? value) => value switch
    {
        "neighbor" => new StopBy(false, null),
        "end" => new StopBy(true, null),
        IDictionary => new StopBy(false, Parse(value)),
        _ => throw new ArgumentException("'stopBy' takes 'neighbor', 'end' or a rule object."),
    };

    private static IDictionary AsObject(object? value, string key) =>
        value as IDictionary ?? throw new ArgumentException($"'{key}' takes a rule object.");

    private static string AsString(object? value, string key) =>
        value as string ?? throw new ArgumentException($"'{key}' takes a string.");

    private static IEnumerable<object?> AsList(object? value, string key) =>
        value is IEnumerable<object?> list ? list : throw new ArgumentException($"'{key}' takes a list of rules.");
}
