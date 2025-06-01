// KspLocalizer.cs
// -----------------------------------------------------------------------------
// Console utility: scans *.cs files, replaces hard‑coded C# string literals
// with Kerbal Space Program localisation keys – **without using regex for code
// parsing**. A hand‑rolled finite‑state tokenizer (adapted from the earlier
// CsStringSplitter) guarantees comment‑safe, attribute‑safe detection of
// literals (regular, verbatim, interpolated, multi‑line, etc.).
//
// Key features
// ------------
// • Backs up each file to <file>.bak and supports a --revert flag.
// • Skips lines whose first non‑whitespace character is '[' (attributes).
// • Skips code wrapped in  #region NO_LOCALIZATION … #endregion blocks.
// • Injects `using KSP.Localization;` when missing.
// • Generates keys as  #<prefix>_<sanitised>(≤25)  with automatic _DUP<n>.
// • Writes / updates  Localization/en-us.cfg  (KSP format) and en-us.csv.
//
// Build & run
//   dotnet run -- <rootFolder> <prefix> [--revert]
// -----------------------------------------------------------------------------

using System.Text;
using System.Text.RegularExpressions; // only for key sanitising & Regex.Unescape

namespace KspLocalizer;

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

// ──────────────────────────────────────────────────────────────────────────────
internal static class KspCSLocalizer
{
    private const string BackupExt = ".bak";
    internal const string LocalizationFolder = "Localization";
    internal const string CsvName = "en-us.csv";
    internal const string CfgName = "en-us.cfg";
    private const int MaxKeyTailLen = 25;

    private static readonly Regex Sanitiser = new("[^A-Za-z0-9]+", RegexOptions.Compiled);

    // ────────────────────────────────────────────────────────────────
    internal static void RestoreBackups(string root)
    {
        foreach (var bak in Directory.EnumerateFiles(root, "*" + BackupExt, SearchOption.AllDirectories))
        {
            string original = bak[..^BackupExt.Length];
            File.Copy(bak, original, overwrite: true);
            File.Delete(bak);
            Console.WriteLine($"Restored {original}");
        }
    }
    internal static void CleanBackups(string root)
    {
        foreach (var bak in Directory.EnumerateFiles(root, "*" + BackupExt, SearchOption.AllDirectories))
        {
            File.Delete(bak);
            Console.WriteLine($"Deleted {bak}");
        }
    }

    internal static void ProcessAllFiles(string path, string prefix,
                                    IDictionary<string, KSPLocalizer.Literal> keyMap,
                                    IDictionary<string, int> dupCounts,
                                    bool numerictags)
    {
        var allFiles = Directory.EnumerateFiles(path, "*.cs", SearchOption.AllDirectories);
        int fileCnt = allFiles.Count();
        int modifiedFiles = 0;

        foreach (var cs in allFiles)
        {
            if (!PatternSearch.ContainsAny(Path.GetFileName(cs), KSPLocalizer.excludeFiles))
                modifiedFiles += (KspCSLocalizer.ProcessFile(cs, prefix, keyMap, dupCounts, numerictags) ? 1 : 0);
        }
        Console.WriteLine($"\n\nCode Files Processed:\n");
        Console.WriteLine($" {allFiles.Count()} *.cs files; modified {modifiedFiles}; generated {keyMap.Count} unique keys.");

        if (KSPLocalizer.includeFiles.Count > 0)
        {
            Console.WriteLine($"\n  Include Patterns:");
            foreach (var ifiles in KSPLocalizer.includeFiles)
            {
                allFiles = Directory.EnumerateFiles(path, ifiles.Pattern, SearchOption.AllDirectories);
                fileCnt += allFiles.Count();
                int m = 0;
                int d = keyMap.Count;
                {
                    foreach (var cs in allFiles)
                    {
                        m += (KspCSLocalizer.ProcessFile(cs, prefix, keyMap, dupCounts, numerictags) ? 1 : 0);
                    }
                    modifiedFiles += m;

                }
                Console.WriteLine($"    Pattern: {ifiles.Pattern}, {allFiles.Count()}  files; modified {m}; generated {keyMap.Count - d} unique keys.");

            }
            Console.WriteLine($" Total of {allFiles.Count()}  code files; modified {modifiedFiles}; generated {keyMap.Count} unique keys.\n");
        }

    }

    // ────────────────────────────────────────────────────────────────
    internal static bool ProcessFile(string path,
                                    string prefix,
                                    IDictionary<string, KSPLocalizer.Literal> keyMap,
                                    IDictionary<string, int> dupCounts, bool numerictags)
    {
        string bakPath = path + BackupExt;
        if (!File.Exists(bakPath))
            File.Copy(path, bakPath);

        var lines = File.ReadAllLines(path).ToList();
        bool hasUsing = lines.Any(l => l.Contains("using KSP.Localization;", StringComparison.Ordinal));

        var rewritten = new List<string>();
        bool inBlockComment = false;
        int noLocDepth = 0;
        bool modified = false;


        foreach (var line in lines)
        {
            string trimmed = line.TrimStart();

            // #region guards
            if (trimmed.StartsWith("#region", StringComparison.OrdinalIgnoreCase) &&
                trimmed.IndexOf("NO_LOCALIZATION", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                noLocDepth++;
                rewritten.Add(line);
                continue;
            }
            if (trimmed.StartsWith("#endregion", StringComparison.OrdinalIgnoreCase))
            {
                if (noLocDepth > 0) noLocDepth--;
                rewritten.Add(line);
                continue;
            }

            // attribute guard
            if (trimmed.StartsWith("[", StringComparison.Ordinal))
            {
                rewritten.Add(line);
                continue;
            }

            // inside excluded region
            if (noLocDepth > 0)
            {
                rewritten.Add(line);
                continue;
            }
            // switch-case guard
            if (trimmed.StartsWith("case ", StringComparison.Ordinal))
            {
                rewritten.Add(line);
                continue;
            }
            if (PatternSearch.ContainsAny(line, KSPLocalizer.excludeStrings))
            {
                rewritten.Add(line);
                continue;
            }
            var tokens = CsTokenizer.ParseLine(line, ref inBlockComment);
            var rebuilt = new StringBuilder();

            foreach (var tok in tokens)
            {
                if (CsTokenizer.IsStringToken(tok))
                {
                    string literal = ExtractLiteral(tok, out bool verbatim);
                    if (verbatim || !literal.Any(char.IsLetter))
                    {
                        string l = "";
                        foreach (var c in literal)
                        {
                            if (c != '\n')
                                l += c;
                            else
                                l += "\\n";
                        }
                        literal = l;

                        rebuilt.Append("\"" + literal + "\"");
                    }
                    else
                    {
                        string key = GetOrCreateKey(literal, prefix, keyMap, dupCounts, numerictags);
                        rebuilt.Append($"Localizer.Format(\"#{key}\")");
                        modified = true;
                    }
                }
                else
                {
                    rebuilt.Append(tok);
                }
            }

            rewritten.Add(rebuilt.ToString());
        }

        // inject using directive if absent
        if (!hasUsing)
        {
            int insertAt = lines.FindIndex(l => l.TrimStart().StartsWith("using", StringComparison.Ordinal));
            if (insertAt < 0) insertAt = 0;
            rewritten.Insert(insertAt, "using KSP.Localization;");
        }
        if (modified)
            File.WriteAllLines(path, rewritten);
        return modified;
    }

    // ────────────────────────────────────────────────────────────────
    private static string ExtractLiteral(string token, out bool verbatim)
    {
        /*bool */
        verbatim = token.StartsWith("@\"", StringComparison.Ordinal) ||
              token.StartsWith("$\"", StringComparison.Ordinal) ||
              token.StartsWith("$@\"", StringComparison.Ordinal) ||
              token.StartsWith("@$\"", StringComparison.Ordinal);

        int firstQuote = token.IndexOf('"');
        int lastQuote = token.LastIndexOf('"');
        if (firstQuote < 0 || lastQuote <= firstQuote) return token; // fail‑safe

        string content = token.Substring(firstQuote + 1, lastQuote - firstQuote - 1);

        if (verbatim)
        {
            return content.Replace("\"\"", "\""); // doubled → single
        }
        else
        {
            return Regex.Unescape(content); // basic unescape for standard strings
        }
    }

    // ────────────────────────────────────────────────────────────────
    private static string GetOrCreateKey(string literal, string prefix,
                                         IDictionary<string, KSPLocalizer.Literal> keyMap,
                                         IDictionary<string, int> dupCounts,
                                         bool numerictags)
    {
        foreach (var v in keyMap)
        {
            if (v.Value.literal == literal)
            {
                return v.Key;
            }
        }

        if (numerictags)
        {
            int tagkey = KSPLocalizer.GetNextTag;
            string key = $"{prefix}_{tagkey}";
            keyMap[key] = new KSPLocalizer.Literal(literal, KSPLocalizer.Origin.cs);
            return key;
        }

        // build tail
        string tail = Sanitiser.Replace(literal, "_").Trim('_');
        if (tail.Length > MaxKeyTailLen) tail = tail[..MaxKeyTailLen];
        if (tail.Length == 0) tail = "TXT";

        string baseKey = $"{prefix}_{tail}";
        string candidate = baseKey;

        if (dupCounts.TryGetValue(baseKey, out int count))
        {
            count++;
            dupCounts[baseKey] = count;
            candidate = $"{baseKey}_DUP{count}"; // will adjust below
        }
        else
        {
            dupCounts[baseKey] = 0;
        }

        // ensure uniqueness across all keys
        bool loop = true;
        while (loop)
        {
            loop = false;
            foreach (var kv in keyMap)
            {

                if (kv.Value.literal == literal)
                {
                    dupCounts[baseKey]++;
                    candidate = $"{baseKey}_DUP{dupCounts[baseKey]}";
                    loop = true;
                    break;
                }
            }
        }
#if false
while (keyMap.Values.Contains(candidate))
        {
            dupCounts[baseKey]++;
            candidate = $"{baseKey}_DUP{dupCounts[baseKey]}";
        }
#endif

        keyMap[candidate] = new KSPLocalizer.Literal(literal, KSPLocalizer.Origin.cs);
        return candidate;
    }

    // ────────────────────────────────────────────────────────────────

    private static string EscapeCsv(string s)
    {
        if (s.Contains(',') || s.Contains('"') || s.Contains('\n'))
            return $"\"{s.Replace("\"", "\"\"")}\"";
        return s;
    }
}

