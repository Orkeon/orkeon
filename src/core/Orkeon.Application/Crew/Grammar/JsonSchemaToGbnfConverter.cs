using System.Globalization;
using System.Text;
using System.Text.Json;

namespace Orkeon.Application.Crew.Grammar;

/// <summary>
/// Converts a subset of JSON Schema (Draft-07) into a GBNF grammar suitable for
/// the <c>grammar</c> field of the llama.cpp <c>/completion</c> endpoint (and the
/// corresponding Ollama <c>/api/generate</c> option).
/// </summary>
/// <remarks>
/// Supported keywords: <c>type</c> (object, array, string, integer, number,
/// boolean, null), <c>properties</c>, <c>required</c>, <c>items</c>, <c>enum</c>,
/// <c>const</c>. <c>$ref</c> is resolved against top-level <c>definitions</c> or
/// <c>$defs</c> sections using a limited JSON-pointer form
/// (<c>#/definitions/&lt;name&gt;</c> or <c>#/$defs/&lt;name&gt;</c>). Additional
/// keywords (oneOf, anyOf, pattern, format, minimum, maximum, minLength,
/// additionalProperties) are intentionally ignored — the grammar stays
/// permissive for those axes while still guaranteeing overall JSON validity.
/// </remarks>
public static class JsonSchemaToGbnfConverter
{
    private const string RootRuleName = "root";

    /// <summary>Converts the JSON-schema text into an equivalent GBNF grammar.</summary>
    /// <param name="jsonSchema">A JSON Schema document (as JSON text).</param>
    /// <returns>A GBNF grammar whose start symbol is <c>root</c>.</returns>
    /// <exception cref="ArgumentException">When <paramref name="jsonSchema"/> is null, empty, or not valid JSON.</exception>
    /// <exception cref="InvalidOperationException">When the schema uses constructs outside the supported subset in a way we can't safely represent.</exception>
    public static string Convert(string jsonSchema)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jsonSchema);

        using var doc = JsonDocument.Parse(jsonSchema);
        return ConvertDocument(doc);
    }

    /// <summary>Converts a parsed JSON-schema element into an equivalent GBNF grammar.</summary>
    public static string Convert(JsonElement schema)
    {
        var builder = new GrammarBuilder(schema);
        return builder.Build();
    }

    private static string ConvertDocument(JsonDocument doc)
    {
        var builder = new GrammarBuilder(doc.RootElement);
        return builder.Build();
    }

    private sealed class GrammarBuilder
    {
        private readonly JsonElement _root;
        private readonly Dictionary<string, string> _rules = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _refToRule = new(StringComparer.Ordinal);
        private int _counter;

        public GrammarBuilder(JsonElement root)
        {
            _root = root;
        }

        public string Build()
        {
            _rules[RootRuleName] = Emit(_root);

            AddIfMissing("ws", "[ \\t\\n]*");
            AddIfMissing("string", "\"\\\"\" char* \"\\\"\"");
            AddIfMissing("char", "[^\"\\\\] | \"\\\\\" ([\"\\\\/bfnrt] | \"u\" [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F] [0-9a-fA-F])");
            AddIfMissing("number", "\"-\"? (\"0\" | [1-9] [0-9]*) (\".\" [0-9]+)? ([eE] [-+]? [0-9]+)?");
            AddIfMissing("integer", "\"-\"? (\"0\" | [1-9] [0-9]*)");
            AddIfMissing("boolean", "\"true\" | \"false\"");
            AddIfMissing("null", "\"null\"");

            var sb = new StringBuilder();
            sb.Append(RootRuleName).Append(" ::= ").Append(_rules[RootRuleName]).Append('\n');
            foreach (var kv in _rules)
            {
                if (kv.Key == RootRuleName) continue;
                sb.Append(kv.Key).Append(" ::= ").Append(kv.Value).Append('\n');
            }
            return sb.ToString();
        }

        private void AddIfMissing(string name, string body)
        {
            if (!_rules.ContainsKey(name)) _rules[name] = body;
        }

        private string Emit(JsonElement schema)
        {
            if (schema.ValueKind != JsonValueKind.Object)
                throw new InvalidOperationException("Schema nodes must be JSON objects.");

            if (TryResolveRef(schema, out var refRule))
                return refRule!;

            if (schema.TryGetProperty("const", out var constEl))
                return LiteralFor(constEl);

            if (schema.TryGetProperty("enum", out var enumEl) && enumEl.ValueKind == JsonValueKind.Array)
                return EmitEnum(enumEl);

            if (!schema.TryGetProperty("type", out var typeEl))
                return "value";

            return typeEl.ValueKind switch
            {
                JsonValueKind.String => EmitByType(typeEl.GetString()!, schema),
                JsonValueKind.Array => EmitUnionTypes(typeEl, schema),
                _ => "value"
            };
        }

        private string EmitByType(string type, JsonElement schema)
        {
            return type switch
            {
                "object" => EmitObject(schema),
                "array" => EmitArray(schema),
                "string" => "string",
                "integer" => "integer",
                "number" => "number",
                "boolean" => "boolean",
                "null" => "null",
                _ => throw new InvalidOperationException($"Unsupported JSON-schema type: '{type}'.")
            };
        }

        private string EmitUnionTypes(JsonElement typeArr, JsonElement schema)
        {
            var parts = new List<string>();
            foreach (var t in typeArr.EnumerateArray())
            {
                if (t.ValueKind != JsonValueKind.String) continue;
                parts.Add(EmitByType(t.GetString()!, schema));
            }
            return parts.Count == 0 ? "value" : "(" + string.Join(" | ", parts) + ")";
        }

        private string EmitObject(JsonElement schema)
        {
            if (!schema.TryGetProperty("properties", out var props) || props.ValueKind != JsonValueKind.Object)
                return "\"{\" ws \"}\"";

            var required = CollectRequiredSet(schema);
            var ordered = CollectOrderedProperties(props);
            var requiredProps = ordered.Where(p => required.Contains(p.Name)).ToList();
            var optionalProps = ordered.Where(p => !required.Contains(p.Name)).ToList();

            var body = BuildObjectBody(requiredProps, optionalProps);
            return Register("obj", body);
        }

        private static HashSet<string> CollectRequiredSet(JsonElement schema)
        {
            var required = new HashSet<string>(StringComparer.Ordinal);
            if (schema.TryGetProperty("required", out var reqEl) && reqEl.ValueKind == JsonValueKind.Array)
            {
                foreach (var r in reqEl.EnumerateArray())
                    if (r.ValueKind == JsonValueKind.String) required.Add(r.GetString()!);
            }
            return required;
        }

        private static List<(string Name, JsonElement Schema)> CollectOrderedProperties(JsonElement props)
        {
            var ordered = new List<(string Name, JsonElement Schema)>();
            foreach (var p in props.EnumerateObject()) ordered.Add((p.Name, p.Value));
            return ordered;
        }

        private string BuildObjectBody(
            List<(string Name, JsonElement Schema)> requiredProps,
            List<(string Name, JsonElement Schema)> optionalProps)
        {
            var body = new StringBuilder("\"{\" ws");
            AppendRequiredProperties(body, requiredProps, optionalProps.Count > 0);
            AppendOptionalProperties(body, optionalProps, requiredProps.Count > 0);
            body.Append(" ws \"}\"");
            return body.ToString();
        }

        private void AppendRequiredProperties(
            StringBuilder body,
            List<(string Name, JsonElement Schema)> requiredProps,
            bool hasOptional)
        {
            for (int i = 0; i < requiredProps.Count; i++)
            {
                var (name, subSchema) = requiredProps[i];
                body.Append(' ').Append(EmitProperty(name, subSchema));
                if (i < requiredProps.Count - 1 || hasOptional)
                    body.Append(" \",\" ws");
            }
        }

        private void AppendOptionalProperties(
            StringBuilder body,
            List<(string Name, JsonElement Schema)> optionalProps,
            bool hasRequired)
        {
            for (int i = 0; i < optionalProps.Count; i++)
            {
                var (name, subSchema) = optionalProps[i];
                var sep = (hasRequired || i > 0) ? " \",\" ws" : "";
                body.Append(" (").Append(sep).Append(' ').Append(EmitProperty(name, subSchema)).Append(")?");
            }
        }

        private string EmitProperty(string name, JsonElement subSchema)
        {
            var key = "\"\\\"" + EscapeLiteral(name) + "\\\"\"";
            var val = Emit(subSchema);
            return key + " ws \":\" ws " + val;
        }

        private string EmitArray(JsonElement schema)
        {
            string itemRule = "value";
            if (schema.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Object)
                itemRule = Emit(itemsEl);

            var body = $"\"[\" ws ( {itemRule} ( ws \",\" ws {itemRule} )* )? ws \"]\"";
            return Register("arr", body);
        }

        private static string EmitEnum(JsonElement enumEl)
        {
            var literals = new List<string>();
            foreach (var v in enumEl.EnumerateArray()) literals.Add(LiteralFor(v));
            return literals.Count == 1 ? literals[0] : "(" + string.Join(" | ", literals) + ")";
        }

        private static string LiteralFor(JsonElement v)
        {
            return v.ValueKind switch
            {
                JsonValueKind.String => "\"\\\"" + EscapeLiteral(v.GetString()!) + "\\\"\"",
                JsonValueKind.Number => "\"" + v.GetRawText() + "\"",
                JsonValueKind.True => "\"true\"",
                JsonValueKind.False => "\"false\"",
                JsonValueKind.Null => "\"null\"",
                _ => throw new InvalidOperationException($"Cannot emit literal for JSON kind '{v.ValueKind}'.")
            };
        }

        private bool TryResolveRef(JsonElement schema, out string? ruleRef)
        {
            ruleRef = null;
            if (!schema.TryGetProperty("$ref", out var refEl) || refEl.ValueKind != JsonValueKind.String) return false;
            var refStr = refEl.GetString()!;
            if (_refToRule.TryGetValue(refStr, out var existing)) { ruleRef = existing; return true; }

            var target = ResolveRefPath(refStr);
            if (target is null)
                throw new InvalidOperationException($"Unresolvable $ref '{refStr}' (only #/definitions/<name> and #/$defs/<name> are supported).");

            var placeholder = AllocateName("ref");
            _refToRule[refStr] = placeholder;
            _rules[placeholder] = Emit(target.Value);
            ruleRef = placeholder;
            return true;
        }

        private JsonElement? ResolveRefPath(string refStr)
        {
            if (refStr.StartsWith("#/definitions/", StringComparison.Ordinal))
                return Lookup(_root, "definitions", refStr["#/definitions/".Length..]);
            if (refStr.StartsWith("#/$defs/", StringComparison.Ordinal))
                return Lookup(_root, "$defs", refStr["#/$defs/".Length..]);
            return null;
        }

        private static JsonElement? Lookup(JsonElement root, string section, string name)
        {
            if (!root.TryGetProperty(section, out var defs) || defs.ValueKind != JsonValueKind.Object) return null;
            if (!defs.TryGetProperty(name, out var target) || target.ValueKind != JsonValueKind.Object) return null;
            return target;
        }

        private string Register(string prefix, string body)
        {
            var name = AllocateName(prefix);
            _rules[name] = body;
            return name;
        }

        private string AllocateName(string prefix)
        {
            _counter++;
            return string.Create(CultureInfo.InvariantCulture, $"{prefix}-{_counter}");
        }

        private static string EscapeLiteral(string s)
        {
            var sb = new StringBuilder(s.Length);
            foreach (var c in s)
            {
                if (c == '"' || c == '\\') sb.Append('\\');
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
