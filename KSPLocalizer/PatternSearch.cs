using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

 namespace KspLocalizer
{
    /// <summary>Represents a search token.  Set <see cref="IsRegex"/> to true for regex patterns.</summary>
    public readonly struct SearchPattern
    {
        public string Pattern { get; }
        public bool IsRegex { get; }

        public SearchPattern(string pattern, bool isRegex = false)
        {
            Pattern = pattern ?? throw new ArgumentNullException(nameof(pattern));
            IsRegex = isRegex;
        }
    }

    public static class PatternSearch
    {
        /// <summary>
        /// Returns true if <paramref name="input"/> contains ANY of the provided patterns.
        /// </summary>
        /// <param name="input">The text you want to scan.</param>
        /// <param name="patterns">A mix of literal strings and regex patterns.</param>
        /// <param name="ignoreCase">Case-insensitive when true (default).</param>
        public static bool ContainsAny(
            string input,
            IEnumerable<SearchPattern> patterns,
            bool ignoreCase = true)
        {
            if (input is null) throw new ArgumentNullException(nameof(input));
            if (patterns is null) throw new ArgumentNullException(nameof(patterns));

            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase
                                        : StringComparison.Ordinal;
            var rxOptions = ignoreCase ? RegexOptions.IgnoreCase
                                        : RegexOptions.None;
            
            foreach (var p in patterns)
            {
                if (p.IsRegex)
                {
                    if (Regex.IsMatch(input, p.Pattern, rxOptions))
                        return true;
                }
                else
                {
                    if (input.IndexOf(p.Pattern, comparison) >= 0)
                        return true;
                }
            }
            return false;
        }
    }
}
