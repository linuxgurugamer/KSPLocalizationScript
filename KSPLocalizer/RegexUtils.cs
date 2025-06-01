using System.Text.RegularExpressions;

namespace KspLocalizer
{
    public class RegexUtils
    {
        /// <summary>
        /// Returns true when <paramref name="pattern"/> appears to be a regex.
        /// Heuristic: looks for characters that rarely occur in plain text but
        /// are significant in .NET regex syntax – *, +, ?, {, }, |, (, ), [, ], ^, $, \.
        /// A leading ^ or trailing $ also counts, even if they are the only meta-character.
        /// </summary>
        public static bool LooksLikeRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern))
                return false;

            // 1️⃣ obvious anchors
            if (pattern.StartsWith("^") || pattern.EndsWith("$"))
                return true;

            // 2️⃣ any unescaped regex-meta character?
            foreach (char c in pattern)
            {
                switch (c)
                {
                    case '*':
                    case '+':
                    case '?':
                    case '{':
                    case '}':
                    case '|':
                    case '(':
                    case ')':
                    case '[':
                    case ']':
                    case '\\':   // backslash is the big giveaway
                        return true;
                }
            }
            return false;
        }

        /* ───────── OPTIONAL: “strong” test ─────────
         * If you need a slower but certain answer, try compiling the pattern.
         * Anything that compiles without throwing is technically a regex.
         * Use when you really must know and performance isn’t critical.
         */
        public static bool IsValidRegex(string pattern)
        {
            if (string.IsNullOrEmpty(pattern)) return false;
            try
            {
                _ = Regex.IsMatch(string.Empty, pattern);
                return true;
            }
            catch (ArgumentException)
            {
                return false; // invalid regex syntax
            }
        }

    }
}
