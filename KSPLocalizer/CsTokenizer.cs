using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace KspLocalizer
{
    // ──────────────────────────────────────────────────────────────────────────────
    //  Tiny tokenizer – zero regex for code parsing
    // ──────────────────────────────────────────────────────────────────────────────
    internal static class CsTokenizer
    {
        public static List<string> ParseLine(string line, ref bool inBlockComment)
        {
            var tokens = new List<string>();
            var current = new StringBuilder();

            bool inString = false;
            bool verbatim = false; // @" or $@"
            bool escapeNext = false; // for standard strings
            bool inLineComment = false;
            //bool interpolatedString = false;

            const string EXISTING = "Localizer.Format(";
            int existlen = EXISTING.Length;

            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];

                // ───── within // comment – copy remainder verbatim ─────
                if (inLineComment)
                {
                    current.Append(c);
                    continue;
                }

                // ───── within /* … */ block comment ─────
                if (inBlockComment)
                {
                    current.Append(c);
                    if (c == '*' && i + 1 < line.Length && line[i + 1] == '/')
                    {
                        current.Append('/');
                        i++;
                        inBlockComment = false;
                    }
                    continue;
                }

                // ───── within string literal ─────
                if (inString)
                {
                    current.Append(c);

                    if (verbatim)
                    {
                        if (c == '"')
                        {
                            if (i + 1 < line.Length && line[i + 1] == '"')
                            {
                                // doubled quote inside verbatim string
                                current.Append('"');
                                i++;
                            }
                            else
                            {
                                tokens.Add(current.ToString());
                                current.Clear();
                                inString = false;
                            }
                        }
                    }
                    else // standard string
                    {
                        if (escapeNext)
                        {
                            escapeNext = false;
                        }
                        else if (c == '\\')
                        {
                            escapeNext = true;
                        }
                        else if (c == '"')
                        {
                            tokens.Add(current.ToString());
                            current.Clear();
                            inString = false;
                        }
                    }
                    continue;
                }

                // ───── outside strings & comments ─────

                // single‑line comment start
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '/')
                {
                    current.Append(line.AsSpan(i));
                    i = line.Length; // consume to EOL
                    inLineComment = true;
                    break;
                }

                // block comment start
                if (c == '/' && i + 1 < line.Length && line[i + 1] == '*')
                {
                    current.Append("/*");
                    i++;
                    inBlockComment = true;
                    continue;
                }

                // Check for Localizer.Format(
                if (c == '"')
                {
                    if (i > existlen)
                    {
                        int p = i - existlen;
                        if (line.Substring(p, existlen) == EXISTING)
                        {
                            inString = true;
                            current.Append('"');
                            continue;
                        }
                    }
                }

                // string literal start – detect $/@ prefixes immediately preceding
                if (c == '"')
                {
                    int p = i - 1;
                    var prefix = new StringBuilder();
                    while (p >= 0 && (line[p] == '@' || line[p] == '$'))
                    {
                        prefix.Insert(0, line[p]);
                        p--;
                    }

                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }
                    current.Append(prefix);
                    current.Append('"');

                    inString = true;
                    verbatim = prefix.ToString().Contains('@') || prefix.ToString().Contains('$');
                    escapeNext = false;
                    continue;
                }


                // ordinary char
                current.Append(c);



            }
            if (current.Length > 0)
                tokens.Add(current.ToString());

            // if still in verbatim string, preserve newline (multi‑line verbatim)
            if (inString && verbatim)
                tokens[^1] += "\n";

            return tokens;
        }

        public static bool IsStringToken(string token)
            => token.Length > 1 && token.IndexOf('"') >= 0 &&
               (token[0] == '"' || token[0] == '@' || token[0] == '$');
    }
}
