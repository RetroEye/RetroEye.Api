// RetroEye app API. A small JSON reader and writer, so the library needs nothing but .NET Standard 2.0 itself.
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace RetroEye.Api
{
    /// <summary>The kinds of value JSON has.</summary>
    public enum ApiJsonKind
    {
        /// <summary>null, or a value that isn't there.</summary>
        Null,
        /// <summary>true or false.</summary>
        Boolean,
        /// <summary>A number.</summary>
        Number,
        /// <summary>A string.</summary>
        String,
        /// <summary>An array of values.</summary>
        Array,
        /// <summary>An object of named values, kept in the order they came.</summary>
        Object,
    }

    /// <summary>
    /// One JSON value: an object, array, string, number, true/false or null. Reading a name or index that isn't there
    /// gives <see cref="Missing"/>, a null value, so a client can read deep into a state without checking each step:
    /// <c>state["chrome"]["caption"]["active"]["text"].AsString("#FFFFFF")</c>.
    /// </summary>
    public sealed class ApiJson
    {
        private readonly List<KeyValuePair<string, ApiJson>>? _members;
        private readonly List<ApiJson>? _items;
        private readonly string? _text;
        private readonly double _number;
        private readonly bool _flag;

        /// <summary>The value given for a name or index that isn't there: null.</summary>
        public static readonly ApiJson Missing = new ApiJson(ApiJsonKind.Null);

        /// <summary>The kind of value this is.</summary>
        public ApiJsonKind Kind { get; }

        /// <summary>Makes a value of a kind with nothing in it yet.</summary>
        private ApiJson(ApiJsonKind kind)
        {
            Kind = kind;
            if (kind == ApiJsonKind.Object) _members = new List<KeyValuePair<string, ApiJson>>();
            if (kind == ApiJsonKind.Array) _items = new List<ApiJson>();
        }

        /// <summary>Makes a string value.</summary>
        private ApiJson(string text) : this(ApiJsonKind.String) => _text = text;

        /// <summary>Makes a number value.</summary>
        private ApiJson(double number) : this(ApiJsonKind.Number) => _number = number;

        /// <summary>Makes a true or false value.</summary>
        private ApiJson(bool flag) : this(ApiJsonKind.Boolean) => _flag = flag;

        /// <summary>Returns a new, empty object.</summary>
        public static ApiJson NewObject() => new ApiJson(ApiJsonKind.Object);

        /// <summary>Returns a new, empty array.</summary>
        public static ApiJson NewArray() => new ApiJson(ApiJsonKind.Array);

        /// <summary>Returns a string value, or null for a null string.</summary>
        public static ApiJson From(string? text) => text == null ? new ApiJson(ApiJsonKind.Null) : new ApiJson(text);

        /// <summary>Returns a number value.</summary>
        public static ApiJson From(double number) => new ApiJson(number);

        /// <summary>Returns a true or false value.</summary>
        public static ApiJson From(bool flag) => new ApiJson(flag);

        /// <summary>Returns a null value.</summary>
        public static ApiJson Null() => new ApiJson(ApiJsonKind.Null);

        /// <summary>Whether this is null or missing.</summary>
        public bool IsNull => Kind == ApiJsonKind.Null;

        /// <summary>The object's member of that name (the last one, if the name came twice), or Missing.</summary>
        public ApiJson this[string name]
        {
            get
            {
                if (_members == null)
                {
                    return Missing;
                }
                for (int i = _members.Count - 1; i >= 0; i--)
                {
                    if (_members[i].Key == name)
                    {
                        return _members[i].Value;
                    }
                }
                return Missing;
            }
        }

        /// <summary>The array's item at that place, or Missing.</summary>
        public ApiJson this[int index] => _items != null && index >= 0 && index < _items.Count ? _items[index] : Missing;

        /// <summary>The array's items (none for anything but an array).</summary>
        public IReadOnlyList<ApiJson> Items => (IReadOnlyList<ApiJson>?)_items ?? Array.Empty<ApiJson>();

        /// <summary>The object's members in their order (none for anything but an object).</summary>
        public IReadOnlyList<KeyValuePair<string, ApiJson>> Members => (IReadOnlyList<KeyValuePair<string, ApiJson>>?)_members ?? Array.Empty<KeyValuePair<string, ApiJson>>();

        /// <summary>Whether the object has a member of that name.</summary>
        public bool Has(string name) => _members != null && _members.Any(m => m.Key == name);

        /// <summary>Sets an object's member, replacing one of the same name, and returns the object, so sets can be chained.</summary>
        public ApiJson Set(string name, ApiJson value)
        {
            if (_members == null)
            {
                throw new InvalidOperationException("Only an object has members.");
            }
            int at = _members.FindIndex(m => m.Key == name);
            if (at >= 0)
            {
                _members[at] = new KeyValuePair<string, ApiJson>(name, value);
            }
            else
            {
                _members.Add(new KeyValuePair<string, ApiJson>(name, value));
            }
            return this;
        }

        /// <summary>Sets an object's member to a string (or null).</summary>
        public ApiJson Set(string name, string? text) => Set(name, From(text));

        /// <summary>Sets an object's member to a number.</summary>
        public ApiJson Set(string name, double number) => Set(name, From(number));

        /// <summary>Sets an object's member to true or false.</summary>
        public ApiJson Set(string name, bool flag) => Set(name, From(flag));

        /// <summary>Adds an item to an array and returns the array.</summary>
        public ApiJson Add(ApiJson item)
        {
            if (_items == null)
            {
                throw new InvalidOperationException("Only an array has items.");
            }
            _items.Add(item);
            return this;
        }

        /// <summary>Returns the string, or the fallback for anything but a string.</summary>
        public string AsString(string fallback = "") => Kind == ApiJsonKind.String ? _text! : fallback;

        /// <summary>Returns the string, or null for anything but a string.</summary>
        public string? AsStringOrNull() => Kind == ApiJsonKind.String ? _text : null;

        /// <summary>Returns the number, or the fallback for anything but a number.</summary>
        public double AsNumber(double fallback = 0) => Kind == ApiJsonKind.Number ? _number : fallback;

        /// <summary>Returns the number rounded to a whole number, or the fallback for anything but a number.</summary>
        public int AsInt(int fallback = 0) => Kind == ApiJsonKind.Number ? (int)Math.Round(_number) : fallback;

        /// <summary>Returns true or false, or the fallback for anything else.</summary>
        public bool AsBool(bool fallback = false) => Kind == ApiJsonKind.Boolean ? _flag : fallback;

        // ---- Writing ----

        /// <summary>Returns the value as compact JSON text, on one line.</summary>
        public override string ToString()
        {
            var text = new StringBuilder();
            Write(text, null, 0);
            return text.ToString();
        }

        /// <summary>Returns the value as JSON text indented two spaces a level, for files people may read.</summary>
        public string ToIndentedString()
        {
            var text = new StringBuilder();
            Write(text, "  ", 0);
            return text.ToString();
        }

        /// <summary>Writes the value, indented with the given step (or on one line for null).</summary>
        private void Write(StringBuilder text, string? indent, int depth)
        {
            switch (Kind)
            {
                case ApiJsonKind.Null:
                    text.Append("null");
                    break;
                case ApiJsonKind.Boolean:
                    text.Append(_flag ? "true" : "false");
                    break;
                case ApiJsonKind.Number:
                    text.Append(FormatNumber(_number));
                    break;
                case ApiJsonKind.String:
                    WriteString(text, _text!);
                    break;
                case ApiJsonKind.Array:
                    WriteList(text, indent, depth, '[', ']', _items!.Count, i => _items[i].Write(text, indent, depth + 1));
                    break;
                case ApiJsonKind.Object:
                    WriteList(text, indent, depth, '{', '}', _members!.Count, i =>
                    {
                        WriteString(text, _members[i].Key);
                        text.Append(indent == null ? ":" : ": ");
                        _members[i].Value.Write(text, indent, depth + 1);
                    });
                    break;
            }
        }

        /// <summary>Writes the brackets and commas of an array or object round its items.</summary>
        private static void WriteList(StringBuilder text, string? indent, int depth, char open, char close, int count, Action<int> item)
        {
            text.Append(open);
            for (int i = 0; i < count; i++)
            {
                if (i > 0) text.Append(',');
                if (indent != null) NewLine(text, indent, depth + 1);
                item(i);
            }
            if (indent != null && count > 0) NewLine(text, indent, depth);
            text.Append(close);
        }

        /// <summary>Starts a new line indented to the depth.</summary>
        private static void NewLine(StringBuilder text, string indent, int depth)
        {
            text.Append('\n');
            for (int i = 0; i < depth; i++) text.Append(indent);
        }

        /// <summary>Writes a number the shortest way that reads back the same, with no exponent for everyday sizes.</summary>
        private static string FormatNumber(double number)
        {
            if (double.IsNaN(number) || double.IsInfinity(number))
            {
                return "0";
            }
            if (number == Math.Floor(number) && Math.Abs(number) < 1e15)
            {
                return ((long)number).ToString(CultureInfo.InvariantCulture);
            }
            return number.ToString("R", CultureInfo.InvariantCulture);
        }

        /// <summary>Writes a string in quotes, escaping what JSON needs escaped (and every control character).</summary>
        private static void WriteString(StringBuilder text, string value)
        {
            text.Append('"');
            foreach (char c in value)
            {
                switch (c)
                {
                    case '"': text.Append("\\\""); break;
                    case '\\': text.Append("\\\\"); break;
                    case '\n': text.Append("\\n"); break;
                    case '\r': text.Append("\\r"); break;
                    case '\t': text.Append("\\t"); break;
                    default:
                        if (c < 0x20 || c == (char)0x2028 || c == (char)0x2029)
                        {
                            text.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
                        }
                        else
                        {
                            text.Append(c);
                        }
                        break;
                }
            }
            text.Append('"');
        }

        // ---- Reading ----

        /// <summary>The deepest nesting a parsed text may have: 64 levels.</summary>
        public const int MaxDepth = 64;

        /// <summary>Reads JSON text, throwing FormatException if it isn't one valid JSON value.</summary>
        public static ApiJson Parse(string text)
        {
            if (text == null)
            {
                throw new ArgumentNullException(nameof(text));
            }
            var reader = new Reader(text);
            ApiJson value = reader.Value(0);
            reader.SkipSpace();
            if (!reader.AtEnd)
            {
                throw reader.Fail("text after the value");
            }
            return value;
        }

        /// <summary>Reads JSON text, giving false (and null) rather than throwing if it isn't valid.</summary>
        public static bool TryParse(string text, out ApiJson? value)
        {
            try
            {
                value = Parse(text);
                return true;
            }
            catch (FormatException)
            {
                value = null;
                return false;
            }
        }

        /// <summary>Reads one JSON value at a time from text.</summary>
        private sealed class Reader
        {
            private readonly string _text;
            private int _at;

            /// <summary>Starts reading the text from its beginning.</summary>
            public Reader(string text) => _text = text;

            /// <summary>Whether everything has been read.</summary>
            public bool AtEnd => _at >= _text.Length;

            /// <summary>Returns the error for something wrong at the current place.</summary>
            public FormatException Fail(string what) => new FormatException($"Bad JSON at {_at}: {what}.");

            /// <summary>Steps over spaces, tabs and line breaks.</summary>
            public void SkipSpace()
            {
                while (_at < _text.Length && (_text[_at] == ' ' || _text[_at] == '\t' || _text[_at] == '\n' || _text[_at] == '\r'))
                {
                    _at++;
                }
            }

            /// <summary>Reads the value at the current place, nested to the depth given.</summary>
            public ApiJson Value(int depth)
            {
                if (depth > MaxDepth)
                {
                    throw Fail("nested too deeply");
                }
                SkipSpace();
                if (AtEnd)
                {
                    throw Fail("no value");
                }
                char c = _text[_at];
                switch (c)
                {
                    case '{': return Object(depth);
                    case '[': return Array(depth);
                    case '"': return From(String());
                    case 't': Word("true"); return From(true);
                    case 'f': Word("false"); return From(false);
                    case 'n': Word("null"); return Null();
                    default:
                        if (c == '-' || (c >= '0' && c <= '9'))
                        {
                            return From(Number());
                        }
                        throw Fail($"unexpected '{c}'");
                }
            }

            /// <summary>Reads an object.</summary>
            private ApiJson Object(int depth)
            {
                var result = NewObject();
                _at++;
                SkipSpace();
                if (!AtEnd && _text[_at] == '}')
                {
                    _at++;
                    return result;
                }
                while (true)
                {
                    SkipSpace();
                    if (AtEnd || _text[_at] != '"')
                    {
                        throw Fail("a member's name should be a string");
                    }
                    string name = String();
                    SkipSpace();
                    Expect(':');
                    ApiJson value = Value(depth + 1);
                    // The last of a name given twice wins, as most readers do
                    result._members!.Add(new KeyValuePair<string, ApiJson>(name, value));
                    SkipSpace();
                    if (AtEnd)
                    {
                        throw Fail("unfinished object");
                    }
                    if (_text[_at] == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect('}');
                    return result;
                }
            }

            /// <summary>Reads an array.</summary>
            private ApiJson Array(int depth)
            {
                var result = NewArray();
                _at++;
                SkipSpace();
                if (!AtEnd && _text[_at] == ']')
                {
                    _at++;
                    return result;
                }
                while (true)
                {
                    result._items!.Add(Value(depth + 1));
                    SkipSpace();
                    if (AtEnd)
                    {
                        throw Fail("unfinished array");
                    }
                    if (_text[_at] == ',')
                    {
                        _at++;
                        continue;
                    }
                    Expect(']');
                    return result;
                }
            }

            /// <summary>Steps over the character expected, or fails.</summary>
            private void Expect(char c)
            {
                if (AtEnd || _text[_at] != c)
                {
                    throw Fail($"'{c}' expected");
                }
                _at++;
            }

            /// <summary>Steps over a word (true, false, null), or fails.</summary>
            private void Word(string word)
            {
                if (string.CompareOrdinal(_text, _at, word, 0, word.Length) != 0)
                {
                    throw Fail($"'{word}' expected");
                }
                _at += word.Length;
            }

            /// <summary>Reads a number.</summary>
            private double Number()
            {
                int start = _at;
                if (_text[_at] == '-') _at++;
                while (!AtEnd && "0123456789.eE+-".IndexOf(_text[_at]) >= 0) _at++;
                string part = _text.Substring(start, _at - start);
                if (!double.TryParse(part, NumberStyles.Float, CultureInfo.InvariantCulture, out double number))
                {
                    throw Fail($"bad number '{part}'");
                }
                return number;
            }

            /// <summary>Reads a string in quotes, with its escapes.</summary>
            private string String()
            {
                _at++;
                var result = new StringBuilder();
                while (true)
                {
                    if (AtEnd)
                    {
                        throw Fail("unfinished string");
                    }
                    char c = _text[_at++];
                    if (c == '"')
                    {
                        return result.ToString();
                    }
                    if (c < 0x20)
                    {
                        throw Fail("control character in a string");
                    }
                    if (c != '\\')
                    {
                        result.Append(c);
                        continue;
                    }
                    if (AtEnd)
                    {
                        throw Fail("unfinished escape");
                    }
                    char e = _text[_at++];
                    switch (e)
                    {
                        case '"': result.Append('"'); break;
                        case '\\': result.Append('\\'); break;
                        case '/': result.Append('/'); break;
                        case 'b': result.Append('\b'); break;
                        case 'f': result.Append('\f'); break;
                        case 'n': result.Append('\n'); break;
                        case 'r': result.Append('\r'); break;
                        case 't': result.Append('\t'); break;
                        case 'u':
                            if (_at + 4 > _text.Length || !int.TryParse(_text.Substring(_at, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out int code))
                            {
                                throw Fail("bad \\u escape");
                            }
                            result.Append((char)code);
                            _at += 4;
                            break;
                        default:
                            throw Fail($"bad escape '\\{e}'");
                    }
                }
            }
        }
    }
}
