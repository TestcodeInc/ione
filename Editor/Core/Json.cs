using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Ione.Core
{
    // Small JSON parser + escape helpers for cases JsonUtility can't handle
    // (polymorphic arrays, dynamic keys). Parse output: string, double,
    // bool, null, List<object>, Dictionary<string, object>.
    public static class Json
    {
        public static string Str(string s)
        {
            if (s == null) return "null";
            var sb = new StringBuilder(s.Length + 2);
            sb.Append('"');
            foreach (var c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("X4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append('"');
            return sb.ToString();
        }

        public static string Num(float f) => f.ToString("0.######", CultureInfo.InvariantCulture);
        public static string Num(double d) => d.ToString("0.######", CultureInfo.InvariantCulture);
        public static string Bool(bool b) => b ? "true" : "false";

        public static object Parse(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            var p = new Parser(json);
            p.SkipWs();
            var v = p.ReadValue();
            return v;
        }

        public static Dictionary<string, object> ParseObject(string json)
            => Parse(json) as Dictionary<string, object>;

        public static List<object> ParseArray(string json)
            => Parse(json) as List<object>;

        public static string GetString(Dictionary<string, object> d, string key, string fallback = null)
            => (d != null && d.TryGetValue(key, out var v) && v is string s) ? s : fallback;

        public static double GetNumber(Dictionary<string, object> d, string key, double fallback = 0)
            => (d != null && d.TryGetValue(key, out var v) && v is double n) ? n : fallback;

        public static bool GetBool(Dictionary<string, object> d, string key, bool fallback = false)
            => (d != null && d.TryGetValue(key, out var v) && v is bool b) ? b : fallback;

        public static List<object> GetArray(Dictionary<string, object> d, string key)
            => (d != null && d.TryGetValue(key, out var v)) ? v as List<object> : null;

        public static Dictionary<string, object> GetObject(Dictionary<string, object> d, string key)
            => (d != null && d.TryGetValue(key, out var v)) ? v as Dictionary<string, object> : null;

        // Re-serialize a parsed value back to a JSON string.
        public static string Serialize(object v)
        {
            var sb = new StringBuilder();
            SerializeInto(sb, v);
            return sb.ToString();
        }

        static void SerializeInto(StringBuilder sb, object v)
        {
            switch (v)
            {
                case null: sb.Append("null"); break;
                case string s: sb.Append(Str(s)); break;
                case bool b: sb.Append(b ? "true" : "false"); break;
                case double d: sb.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
                case float f: sb.Append(f.ToString("R", CultureInfo.InvariantCulture)); break;
                case int i: sb.Append(i.ToString(CultureInfo.InvariantCulture)); break;
                case long l: sb.Append(l.ToString(CultureInfo.InvariantCulture)); break;
                case IList<object> arr:
                    sb.Append('[');
                    for (int i = 0; i < arr.Count; i++)
                    {
                        if (i > 0) sb.Append(',');
                        SerializeInto(sb, arr[i]);
                    }
                    sb.Append(']');
                    break;
                case IDictionary<string, object> obj:
                    sb.Append('{');
                    bool first = true;
                    foreach (var kv in obj)
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        sb.Append(Str(kv.Key)).Append(':');
                        SerializeInto(sb, kv.Value);
                    }
                    sb.Append('}');
                    break;
                default:
                    sb.Append(Str(v.ToString()));
                    break;
            }
        }

        class Parser
        {
            readonly string s;
            int i;

            public Parser(string src) { s = src; i = 0; }

            public void SkipWs()
            {
                while (i < s.Length && char.IsWhiteSpace(s[i])) i++;
            }

            public object ReadValue()
            {
                SkipWs();
                if (i >= s.Length) throw new FormatException("unexpected end of json");
                var c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == 't' || c == 'f') return ReadBool();
                if (c == 'n') return ReadNull();
                if (c == '-' || char.IsDigit(c)) return ReadNumber();
                throw new FormatException($"unexpected char '{c}' at {i}");
            }

            Dictionary<string, object> ReadObject()
            {
                var o = new Dictionary<string, object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == '}') { i++; return o; }
                while (true)
                {
                    SkipWs();
                    var k = ReadString();
                    SkipWs();
                    if (i >= s.Length || s[i] != ':') throw new FormatException($"expected ':' at {i}");
                    i++;
                    var v = ReadValue();
                    o[k] = v;
                    SkipWs();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == '}') { i++; return o; }
                    throw new FormatException($"expected ',' or '}}' at {i}");
                }
            }

            List<object> ReadArray()
            {
                var a = new List<object>();
                i++;
                SkipWs();
                if (i < s.Length && s[i] == ']') { i++; return a; }
                while (true)
                {
                    a.Add(ReadValue());
                    SkipWs();
                    if (i < s.Length && s[i] == ',') { i++; continue; }
                    if (i < s.Length && s[i] == ']') { i++; return a; }
                    throw new FormatException($"expected ',' or ']' at {i}");
                }
            }

            string ReadString()
            {
                if (s[i] != '"') throw new FormatException($"expected string at {i}");
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    var c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c == '\\')
                    {
                        if (i >= s.Length) throw new FormatException("bad escape");
                        var e = s[i++];
                        switch (e)
                        {
                            case '"':  sb.Append('"');  break;
                            case '\\': sb.Append('\\'); break;
                            case '/':  sb.Append('/');  break;
                            case 'b':  sb.Append('\b'); break;
                            case 'f':  sb.Append('\f'); break;
                            case 'n':  sb.Append('\n'); break;
                            case 'r':  sb.Append('\r'); break;
                            case 't':  sb.Append('\t'); break;
                            case 'u':
                                if (i + 4 > s.Length) throw new FormatException("bad unicode escape");
                                var hex = s.Substring(i, 4);
                                i += 4;
                                sb.Append((char)Convert.ToInt32(hex, 16));
                                break;
                            default: throw new FormatException($"bad escape \\{e}");
                        }
                    }
                    else sb.Append(c);
                }
                throw new FormatException("unterminated string");
            }

            bool ReadBool()
            {
                if (i + 4 <= s.Length && s.Substring(i, 4) == "true") { i += 4; return true; }
                if (i + 5 <= s.Length && s.Substring(i, 5) == "false") { i += 5; return false; }
                throw new FormatException($"bad bool at {i}");
            }

            object ReadNull()
            {
                if (i + 4 <= s.Length && s.Substring(i, 4) == "null") { i += 4; return null; }
                throw new FormatException($"bad null at {i}");
            }

            double ReadNumber()
            {
                int start = i;
                if (s[i] == '-') i++;
                while (i < s.Length && (char.IsDigit(s[i]) || s[i] == '.' || s[i] == 'e' || s[i] == 'E' || s[i] == '+' || s[i] == '-')) i++;
                var num = s.Substring(start, i - start);
                return double.Parse(num, CultureInfo.InvariantCulture);
            }
        }
    }
}
