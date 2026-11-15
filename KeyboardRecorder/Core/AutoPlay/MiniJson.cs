using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace ADOFAI_Auto.Core.AutoPlay
{
    /// <summary>
    /// .adofai 레벨 파일용 느슨한 JSON 파서. 에디터가 만든 파일에는 후행 쉼표(trailing comma)나 BOM이 있어
    /// 표준 파서가 실패하므로 직접 구현했다. 결과: Dictionary&lt;string,object&gt; / List&lt;object&gt; / string / double / bool / null
    /// </summary>
    public static class MiniJson
    {
        public static object Parse(string text)
        {
            var p = new Parser(text);
            p.SkipWhite();
            object v = p.ReadValue();
            return v;
        }

        private sealed class Parser
        {
            private readonly string s;
            private int i;

            public Parser(string text)
            {
                s = text ?? "";
                if (s.Length > 0 && s[0] == '\uFEFF') i = 1;
            }

            public void SkipWhite()
            {
                while (i < s.Length)
                {
                    char c = s[i];
                    if (char.IsWhiteSpace(c)) i++;
                    else if (c == '/' && i + 1 < s.Length && s[i + 1] == '/')
                        while (i < s.Length && s[i] != '\n') i++;
                    else break;
                }
            }

            public object ReadValue()
            {
                SkipWhite();
                if (i >= s.Length) throw new FormatException("예상치 못한 파일 끝");
                char c = s[i];
                if (c == '{') return ReadObject();
                if (c == '[') return ReadArray();
                if (c == '"') return ReadString();
                if (c == '-' || c == '+' || (c >= '0' && c <= '9')) return ReadNumber();
                if (Match("true")) return true;
                if (Match("false")) return false;
                if (Match("null")) return null;
                throw new FormatException("잘못된 값 위치: " + i);
            }

            private bool Match(string word)
            {
                if (string.CompareOrdinal(s, i, word, 0, word.Length) != 0) return false;
                i += word.Length;
                return true;
            }

            private Dictionary<string, object> ReadObject()
            {
                var map = new Dictionary<string, object>();
                i++; // {
                while (true)
                {
                    SkipWhite();
                    if (i >= s.Length) throw new FormatException("닫히지 않은 객체");
                    if (s[i] == '}') { i++; return map; }
                    if (s[i] == ',') { i++; continue; }

                    string key = ReadString();
                    SkipWhite();
                    if (i >= s.Length || s[i] != ':') throw new FormatException("':' 누락: " + i);
                    i++;
                    map[key] = ReadValue();
                }
            }

            private List<object> ReadArray()
            {
                var list = new List<object>();
                i++; // [
                while (true)
                {
                    SkipWhite();
                    if (i >= s.Length) throw new FormatException("닫히지 않은 배열");
                    if (s[i] == ']') { i++; return list; }
                    if (s[i] == ',') { i++; continue; }
                    list.Add(ReadValue());
                }
            }

            private string ReadString()
            {
                SkipWhite();
                if (i >= s.Length || s[i] != '"') throw new FormatException("문자열 시작 누락: " + i);
                i++;
                var sb = new StringBuilder();
                while (i < s.Length)
                {
                    char c = s[i++];
                    if (c == '"') return sb.ToString();
                    if (c != '\\') { sb.Append(c); continue; }

                    if (i >= s.Length) break;
                    char e = s[i++];
                    switch (e)
                    {
                        case 'n': sb.Append('\n'); break;
                        case 'r': sb.Append('\r'); break;
                        case 't': sb.Append('\t'); break;
                        case 'b': sb.Append('\b'); break;
                        case 'f': sb.Append('\f'); break;
                        case 'u':
                            if (i + 4 > s.Length) throw new FormatException("잘못된 \\u 이스케이프");
                            sb.Append((char)int.Parse(s.Substring(i, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture));
                            i += 4;
                            break;
                        default: sb.Append(e); break; // \" \\ \/ 등
                    }
                }
                throw new FormatException("닫히지 않은 문자열");
            }

            private double ReadNumber()
            {
                int start = i;
                while (i < s.Length && "+-0123456789.eE".IndexOf(s[i]) >= 0) i++;
                return double.Parse(s.Substring(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture);
            }
        }
    }
}
