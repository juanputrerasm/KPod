using System.Globalization;
using System.Text;

namespace KPod.Core.Compat;

/// <summary>
/// A small JSON reader and writer covering exactly what preferences.json needs.
/// Hand-written so the library has no package dependencies and produces a single
/// executable on every target framework.
/// </summary>
internal static class Json
{
    /// <summary>
    /// Parses a JSON object. Returns null for anything malformed, so callers can
    /// fall back to defaults instead of handling exceptions.
    /// </summary>
    internal static Dictionary<string, object?>? ParseObject(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        try
        {
            JsonReader reader = new(text!);
            object? value = reader.ReadValue();
            reader.SkipWhitespace();
            return reader.AtEnd ? value as Dictionary<string, object?> : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    internal static int? GetInt(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out object? value) && value is double number ? (int)number : null;

    internal static bool? GetBool(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out object? value) && value is bool flag ? flag : null;

    internal static string? GetString(Dictionary<string, object?> obj, string key) =>
        obj.TryGetValue(key, out object? value) ? value as string : null;

    /// <summary>String array, or null when the key is absent or not an array.</summary>
    internal static List<string>? GetStringList(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value is not List<object?> items)
        {
            return null;
        }

        List<string> result = [];
        foreach (object? item in items)
        {
            if (item is string text)
            {
                result.Add(text);
            }
        }

        return result;
    }

    /// <summary>Array of objects, or null when the key is absent or not an array.</summary>
    internal static List<Dictionary<string, object?>>? GetObjectList(Dictionary<string, object?> obj, string key)
    {
        if (!obj.TryGetValue(key, out object? value) || value is not List<object?> items)
        {
            return null;
        }

        List<Dictionary<string, object?>> result = [];
        foreach (object? item in items)
        {
            if (item is Dictionary<string, object?> child)
            {
                result.Add(child);
            }
        }

        return result;
    }

    internal static string Escape(string? value)
    {
        StringBuilder builder = new();
        foreach (char c in value ?? string.Empty)
        {
            switch (c)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                default:
                    if (c < ' ')
                    {
                        builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    internal static string Quote(string? value) => "\"" + Escape(value) + "\"";

    internal static string StringArray(IEnumerable<string> values) =>
        "[" + string.Join(", ", values.Select(Quote)) + "]";

    /// <summary>Recursive descent over the JSON grammar.</summary>
    private sealed class JsonReader(string text)
    {
        private int _index;

        internal bool AtEnd => _index >= text.Length;

        internal void SkipWhitespace()
        {
            while (_index < text.Length && char.IsWhiteSpace(text[_index]))
            {
                _index++;
            }
        }

        internal object? ReadValue()
        {
            SkipWhitespace();
            if (AtEnd)
            {
                throw new FormatException("Unexpected end of JSON.");
            }

            return text[_index] switch
            {
                '{' => ReadObject(),
                '[' => ReadArray(),
                '"' => ReadString(),
                't' => ReadLiteral("true", true),
                'f' => ReadLiteral("false", false),
                'n' => ReadLiteral("null", null),
                _ => ReadNumber(),
            };
        }

        private Dictionary<string, object?> ReadObject()
        {
            Dictionary<string, object?> result = new(StringComparer.Ordinal);
            Expect('{');
            SkipWhitespace();

            if (Peek() == '}')
            {
                _index++;
                return result;
            }

            while (true)
            {
                SkipWhitespace();
                string key = ReadString();
                SkipWhitespace();
                Expect(':');
                result[key] = ReadValue();
                SkipWhitespace();

                char next = Peek();
                _index++;
                if (next == '}')
                {
                    return result;
                }

                if (next != ',')
                {
                    throw new FormatException("Expected ',' or '}' in JSON object.");
                }
            }
        }

        private List<object?> ReadArray()
        {
            List<object?> result = [];
            Expect('[');
            SkipWhitespace();

            if (Peek() == ']')
            {
                _index++;
                return result;
            }

            while (true)
            {
                result.Add(ReadValue());
                SkipWhitespace();

                char next = Peek();
                _index++;
                if (next == ']')
                {
                    return result;
                }

                if (next != ',')
                {
                    throw new FormatException("Expected ',' or ']' in JSON array.");
                }
            }
        }

        private string ReadString()
        {
            Expect('"');
            StringBuilder builder = new();

            while (true)
            {
                if (AtEnd)
                {
                    throw new FormatException("Unterminated JSON string.");
                }

                char c = text[_index++];
                if (c == '"')
                {
                    return builder.ToString();
                }

                if (c != '\\')
                {
                    builder.Append(c);
                    continue;
                }

                if (AtEnd)
                {
                    throw new FormatException("Unterminated JSON escape.");
                }

                char escape = text[_index++];
                switch (escape)
                {
                    case '"': builder.Append('"'); break;
                    case '\\': builder.Append('\\'); break;
                    case '/': builder.Append('/'); break;
                    case 'b': builder.Append('\b'); break;
                    case 'f': builder.Append('\f'); break;
                    case 'n': builder.Append('\n'); break;
                    case 'r': builder.Append('\r'); break;
                    case 't': builder.Append('\t'); break;
                    case 'u':
                        if (_index + 4 > text.Length
                            || !ushort.TryParse(text.Substring(_index, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ushort code))
                        {
                            throw new FormatException("Bad \\u escape in JSON string.");
                        }

                        builder.Append((char)code);
                        _index += 4;
                        break;
                    default:
                        throw new FormatException("Unknown escape in JSON string.");
                }
            }
        }

        private double ReadNumber()
        {
            int start = _index;
            while (!AtEnd && (char.IsDigit(text[_index]) || "+-.eE".IndexOf(text[_index]) >= 0))
            {
                _index++;
            }

            if (double.TryParse(text.Substring(start, _index - start), NumberStyles.Float, CultureInfo.InvariantCulture, out double value))
            {
                return value;
            }

            throw new FormatException("Bad number in JSON.");
        }

        private object? ReadLiteral(string literal, object? value)
        {
            if (_index + literal.Length > text.Length
                || string.CompareOrdinal(text, _index, literal, 0, literal.Length) != 0)
            {
                throw new FormatException("Unknown JSON literal.");
            }

            _index += literal.Length;
            return value;
        }

        private char Peek() => AtEnd ? throw new FormatException("Unexpected end of JSON.") : text[_index];

        private void Expect(char expected)
        {
            if (Peek() != expected)
            {
                throw new FormatException($"Expected '{expected}' in JSON.");
            }

            _index++;
        }
    }
}
