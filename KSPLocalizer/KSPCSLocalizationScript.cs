// KspLocalizer.cs


using System.Text;
using System.Text.RegularExpressions; // only for key sanitising & Regex.Unescape

namespace KspLocalizer
{

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
            Console.WriteLine($" {allFiles.Count()} *.cs files; modified {modifiedFiles}; generated {keyMap.Count} unique keys.\n\n");

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

            Console.WriteLine($"Updated {path}");

            var lines = File.ReadAllLines(path).ToList();
            bool hasUsing = lines.Any(l => l.Contains("using KSP.Localization;", StringComparison.Ordinal));

            var rewritten = new List<string>();
            bool inBlockComment = false;
            int noLocDepth = 0;
            bool modified = false;
            bool attribLoc = false;
            bool recognizedAttrib = false;

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
                    if (attribLoc)
                        attribLoc = false;
                    else
                    {
                        if (noLocDepth > 0) noLocDepth--;
                    }
                    rewritten.Add(line);
                    continue;
                }

                // attribute guard
                if (trimmed.StartsWith("[", StringComparison.Ordinal))
                {
                    rewritten.Add(line);
                    recognizedAttrib = true;    // flag to mark the code as an attribute, until the next semicolon
                    continue;
                }
                // inside excluded region
                if (noLocDepth > 0)
                {
                    rewritten.Add(line);
                    continue;
                }
                if (trimmed.StartsWith("#region", StringComparison.OrdinalIgnoreCase) &&
                    trimmed.IndexOf("ATTRIBUTE_LOCALIZATION", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    attribLoc = true;
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
                            if (!attribLoc && !recognizedAttrib)
                                rebuilt.Append($"Localizer.Format(\"#{key}\")");
                            else
                                rebuilt.Append($"\"#{key}\"");
                            modified = true;
                        }
                    }
                    else
                    {
                        rebuilt.Append(tok);
                    }
                    if (rebuilt[rebuilt.Length - 1] == ';')
                        recognizedAttrib = false;
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
}

