using System.Globalization;
using System.Text.Json.Nodes;

namespace SpanFlow.Expressions;

/// <summary>
/// Evaluates the small expression language used inside <c>{{ ... }}</c> templates.
/// Deliberately NOT a scripting engine: paths, literals, comparisons, boolean operators,
/// <c>.length</c>, and <c>exists(path)</c> — nothing user-executable.
///
/// Grammar (precedence low→high): or → and → equality (== !=) → comparison (&lt; &lt;= &gt; &gt;=)
/// → unary (!) → primary (literal | path | exists(path) | parenthesized).
/// Paths: <c>input.items[0].id</c>, <c>trigger.date</c>, <c>nodes.n1.output.total</c>.
/// A missing path yields null (never throws); malformed syntax throws <see cref="ExpressionException"/>.
/// </summary>
public static class ExpressionEvaluator
{
    /// <summary>Evaluates to a JSON value (null when the expression resolves to nothing).</summary>
    public static JsonNode? Evaluate(string expression, ExpressionContext context)
    {
        var parser = new Parser(expression, context);
        var result = parser.ParseOr();
        parser.ExpectEnd();
        return result;
    }

    /// <summary>Truthiness: false/null/0/""/[] are false; everything else is true.</summary>
    public static bool EvaluateAsBool(string expression, ExpressionContext context) =>
        IsTruthy(Evaluate(expression, context));

    internal static bool IsTruthy(JsonNode? value) => value switch
    {
        null => false,
        JsonArray array => array.Count > 0,
        JsonObject => true,
        JsonValue v when v.TryGetValue<bool>(out var b) => b,
        JsonValue v when TryGetNumber(v, out var n) => n != 0,
        JsonValue v when v.TryGetValue<string>(out var s) => s.Length > 0,
        _ => true,
    };

    public static bool TryGetNumber(JsonNode? node, out double number)
    {
        number = 0;
        if (node is not JsonValue value) return false;
        // In-memory JsonValues are backed by their exact CLR type — probe the common ones.
        if (value.TryGetValue<double>(out number)) return true;
        if (value.TryGetValue<int>(out var i)) { number = i; return true; }
        if (value.TryGetValue<long>(out var l)) { number = l; return true; }
        if (value.TryGetValue<decimal>(out var m)) { number = (double)m; return true; }
        if (value.TryGetValue<string>(out var s)
            && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out number))
        {
            return true;
        }
        return false;
    }

    private sealed class Parser(string text, ExpressionContext context)
    {
        private int _pos;

        public void ExpectEnd()
        {
            SkipWhitespace();
            if (_pos < text.Length)
            {
                throw new ExpressionException($"Unexpected '{text[_pos]}' at position {_pos} in '{text}'.");
            }
        }

        public JsonNode? ParseOr()
        {
            var left = ParseAnd();
            while (TryConsume("||"))
            {
                // No short-circuit skipping needed: evaluation has no side effects.
                var right = ParseAnd();
                left = JsonValue.Create(IsTruthy(left) || IsTruthy(right));
            }
            return left;
        }

        private JsonNode? ParseAnd()
        {
            var left = ParseEquality();
            while (TryConsume("&&"))
            {
                var right = ParseEquality();
                left = JsonValue.Create(IsTruthy(left) && IsTruthy(right));
            }
            return left;
        }

        private JsonNode? ParseEquality()
        {
            var left = ParseComparison();
            while (true)
            {
                if (TryConsume("==")) left = JsonValue.Create(AreEqual(left, ParseComparison()));
                else if (TryConsume("!=")) left = JsonValue.Create(!AreEqual(left, ParseComparison()));
                else return left;
            }
        }

        private JsonNode? ParseComparison()
        {
            var left = ParseUnary();
            while (true)
            {
                // Order matters: try two-char operators before their one-char prefixes.
                if (TryConsume(">=")) left = Compare(left, ParseUnary(), static c => c >= 0);
                else if (TryConsume("<=")) left = Compare(left, ParseUnary(), static c => c <= 0);
                else if (TryConsume(">")) left = Compare(left, ParseUnary(), static c => c > 0);
                else if (TryConsume("<")) left = Compare(left, ParseUnary(), static c => c < 0);
                else return left;
            }
        }

        private JsonNode? ParseUnary()
        {
            SkipWhitespace();
            // '!' negates unless it is the start of '!=' (which ParseEquality owns).
            if (_pos < text.Length && text[_pos] == '!' && (_pos + 1 >= text.Length || text[_pos + 1] != '='))
            {
                _pos++;
                return JsonValue.Create(!IsTruthy(ParseUnary()));
            }
            return ParsePrimary();
        }

        private JsonNode? ParsePrimary()
        {
            SkipWhitespace();
            if (_pos >= text.Length) throw new ExpressionException($"Unexpected end of expression '{text}'.");

            var c = text[_pos];
            if (c == '(')
            {
                _pos++;
                var inner = ParseOr();
                SkipWhitespace();
                if (_pos >= text.Length || text[_pos] != ')') throw new ExpressionException($"Missing ')' in '{text}'.");
                _pos++;
                return inner;
            }
            if (c is '\'' or '"') return JsonValue.Create(ParseStringLiteral(c));
            if (char.IsDigit(c) || (c == '-' && _pos + 1 < text.Length && char.IsDigit(text[_pos + 1])))
            {
                return JsonValue.Create(ParseNumberLiteral());
            }
            if (char.IsLetter(c) || c == '_') return ParseIdentifierExpression();

            throw new ExpressionException($"Unexpected '{c}' at position {_pos} in '{text}'.");
        }

        private JsonNode? ParseIdentifierExpression()
        {
            var name = ReadIdentifier();
            switch (name)
            {
                case "true": return JsonValue.Create(true);
                case "false": return JsonValue.Create(false);
                case "null": return null;
                case "exists":
                    SkipWhitespace();
                    if (!TryConsume("(")) throw new ExpressionException("exists() requires parentheses: exists(path).");
                    var value = ParseOr();
                    SkipWhitespace();
                    if (!TryConsume(")")) throw new ExpressionException($"Missing ')' after exists( in '{text}'.");
                    return JsonValue.Create(value is not null);
                default:
                    return ParsePath(name);
            }
        }

        private JsonNode? ParsePath(string root)
        {
            JsonNode? current;
            if (root == "nodes")
            {
                // nodes.<id>.output[...]
                if (!TryConsume(".")) throw new ExpressionException("'nodes' must be followed by a node id: nodes.<id>.output...");
                var nodeId = ReadIdentifier();
                context.NodeOutputs.TryGetValue(nodeId, out current);
                if (TryConsume("."))
                {
                    var member = ReadIdentifier();
                    current = member == "output" ? current : Access(current, member);
                }
            }
            else
            {
                if (!ExpressionContext.KnownRoots.Contains(root))
                {
                    throw new ExpressionException(
                        $"Unknown root '{root}' in '{text}'. Expressions start with one of: " +
                        $"{string.Join(", ", ExpressionContext.KnownRoots.Order())}.");
                }
                current = context.ResolveRoot(root);
            }

            while (true)
            {
                if (TryConsume("."))
                {
                    current = Access(current, ReadIdentifier());
                }
                else if (TryConsume("["))
                {
                    SkipWhitespace();
                    // A quoted key indexes an object member (headers['X-GitHub-Event']); a number indexes an array.
                    if (_pos < text.Length && text[_pos] is '\'' or '"')
                    {
                        var key = ParseStringLiteral(text[_pos]);
                        SkipWhitespace();
                        if (!TryConsume("]")) throw new ExpressionException($"Missing ']' in '{text}'.");
                        current = Access(current, key);
                    }
                    else
                    {
                        var index = (int)ParseNumberLiteral();
                        SkipWhitespace();
                        if (!TryConsume("]")) throw new ExpressionException($"Missing ']' in '{text}'.");
                        current = current is JsonArray array && index >= 0 && index < array.Count ? array[index] : null;
                    }
                }
                else
                {
                    return current;
                }
            }
        }

        private static JsonNode? Access(JsonNode? node, string member)
        {
            if (member == "length")
            {
                return node switch
                {
                    JsonArray array => JsonValue.Create(array.Count),
                    JsonValue v when v.TryGetValue<string>(out var s) => JsonValue.Create(s.Length),
                    _ => null,
                };
            }
            return node is JsonObject obj ? obj[member] : null;
        }

        private static bool AreEqual(JsonNode? left, JsonNode? right)
        {
            if (left is null || right is null) return left is null && right is null;
            if (TryGetNumber(left, out var ln) && TryGetNumber(right, out var rn)) return ln == rn;
            if (left is JsonValue lv && right is JsonValue rv
                && lv.TryGetValue<bool>(out var lb) && rv.TryGetValue<bool>(out var rb))
            {
                return lb == rb;
            }
            return AsString(left) == AsString(right);
        }

        private static JsonNode Compare(JsonNode? left, JsonNode? right, Func<int, bool> accept)
        {
            if (TryGetNumber(left, out var ln) && TryGetNumber(right, out var rn))
            {
                return JsonValue.Create(accept(ln.CompareTo(rn)));
            }
            if (left is JsonValue lv && right is JsonValue rv
                && lv.TryGetValue<string>(out var ls) && rv.TryGetValue<string>(out var rs))
            {
                return JsonValue.Create(accept(string.CompareOrdinal(ls, rs)));
            }
            return JsonValue.Create(false); // null / mixed types: no ordering
        }

        internal static string AsString(JsonNode? node) => node switch
        {
            null => "",
            JsonValue v when v.TryGetValue<string>(out var s) => s,
            _ => node.ToJsonString(),
        };

        private string ParseStringLiteral(char quote)
        {
            _pos++; // opening quote
            var start = _pos;
            while (_pos < text.Length && text[_pos] != quote) _pos++;
            if (_pos >= text.Length) throw new ExpressionException($"Unterminated string in '{text}'.");
            var value = text[start.._pos];
            _pos++; // closing quote
            return value;
        }

        private double ParseNumberLiteral()
        {
            var start = _pos;
            if (_pos < text.Length && text[_pos] == '-') _pos++;
            while (_pos < text.Length && (char.IsDigit(text[_pos]) || text[_pos] == '.')) _pos++;
            return _pos > start && double.TryParse(text[start.._pos], NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
                ? number
                : throw new ExpressionException($"Expected a number at position {start} in '{text}'.");
        }

        private string ReadIdentifier()
        {
            SkipWhitespace();
            var start = _pos;
            while (_pos < text.Length && (char.IsLetterOrDigit(text[_pos]) || text[_pos] is '_' or '-')) _pos++;
            if (_pos == start) throw new ExpressionException($"Expected a name at position {_pos} in '{text}'.");
            return text[start.._pos];
        }

        private bool TryConsume(string token)
        {
            SkipWhitespace();
            if (_pos + token.Length <= text.Length && text.AsSpan(_pos, token.Length).SequenceEqual(token))
            {
                _pos += token.Length;
                return true;
            }
            return false;
        }

        private void SkipWhitespace()
        {
            while (_pos < text.Length && char.IsWhiteSpace(text[_pos])) _pos++;
        }
    }
}

/// <summary>A syntax error in a {{ }} expression (bad syntax, not a missing value).</summary>
public class ExpressionException(string message) : Exception(message);
