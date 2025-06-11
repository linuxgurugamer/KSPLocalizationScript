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
                        if (literal.Length == 0)
                        {
                            rebuilt.Append("\"\"");
                        }
                        else
                        {
                            bool loop = false;
                            do
                            {
                                loop = false;
                                // Check for embedded html color
                                //Console.WriteLine("\nloop start, literal: " + literal);

                                int colorStart = literal.IndexOf("<color=", StringComparison.OrdinalIgnoreCase);
                                int colorEnd = literal.IndexOf("</color>", StringComparison.OrdinalIgnoreCase);
                                loop = (colorStart != -1 && colorEnd != -1);
                                //Console.WriteLine("colorStart: " + colorStart + ", colorend: " + colorEnd);

                                if (colorStart != -1 && colorStart < colorEnd)
                                {
                                    string literalOld = literal;
                                    bool good = true;

                                    string string1 = "", string2 = "";
                                    int i = colorStart;

                                    //    string1<color=...>
                                    //    <color=...>
                                    //    <color=...>string2


                                    string1 = literal.Substring(0, i);
                                    int i2 = literal.IndexOf(">");
                                    if (i2 < literal.Length - 2)
                                        string2 = literal.Substring(i2 + 1);
                                    string colorStr = literal.Substring(i, i2 - i + 1);
                                    //Console.WriteLine("string1: " + string1);
                                    //Console.WriteLine("colorStr: " + colorStr);
                                    //Console.WriteLine("string2: " + string2);

                                    if (string1.Length > 0)
                                    {
                                        literal = string2;

                                        if (string1.Length > 0)
                                        {
                                            loop = true;

                                            {
                                                if (verbatim || !string1.Any(char.IsLetter))
                                                {
                                                    string l = "";
                                                    foreach (var c in string1)
                                                    {
                                                        if (c != '\n')
                                                            l += c;
                                                        else
                                                            l += "\\n";
                                                    }
                                                    string1 = l;

                                                    rebuilt.Append(" \"" + string1 + "\"");
                                                }
                                                else
                                                {
                                                    string key = GetOrCreateKey(string1, prefix, keyMap, dupCounts, numerictags);
                                                    if (!attribLoc && !recognizedAttrib)
                                                        rebuilt.Append($" Localizer.Format(\"#{key}\") + ");
                                                    else
                                                        rebuilt.Append($" \"#{key}\"");
                                                    modified = true;
                                                }
                                            }
                                        }
                                    }
                                    rebuilt.Append("\"" + colorStr + "\"");
                                    literal = string2;
                                    if (literal.Length > 0)
                                        rebuilt.Append(" +");
                                    loop = true;

                                }
                                if (colorEnd != -1 && (colorEnd < colorStart || colorStart == -1))
                                {
                                    {
                                        //    string1</color>
                                        //    </color>
                                        //    string1</color>string2
                                        //    </color>string2

                                        string string1 = "", string2 = "";
                                        int i = literal.IndexOf("</color>");
                                        if (i > 0)
                                            string1 = literal.Substring(0, i);
                                        i = literal.IndexOf(">");
                                        if (i < literal.Length - 2)
                                            string2 = literal.Substring(i + 1);

                                        //Console.WriteLine("string1.2: " + string1);
                                        //Console.WriteLine("string2.2: " + string2);

                                        if (string1.Length > 0)
                                        {
                                            loop = true;

                                            //if (string2.Length > 0)
                                            {
                                                loop = true;

                                                {
                                                    if (verbatim || !string1.Any(char.IsLetter))
                                                    {
                                                        string l = "";
                                                        foreach (var c in string1)
                                                        {
                                                            if (c != '\n')
                                                                l += c;
                                                            else
                                                                l += "\\n";
                                                        }
                                                        string1 = l;

                                                        rebuilt.Append(" \"" + string1 + "\"");
                                                    }
                                                    else
                                                    {
                                                        string key = GetOrCreateKey(string1, prefix, keyMap, dupCounts, numerictags);
                                                        if (!attribLoc && !recognizedAttrib)
                                                            rebuilt.Append($" Localizer.Format(\"#{key}\")");
                                                        else
                                                            rebuilt.Append($" \"#{key}\"");
                                                        modified = true;
                                                    }
                                                }
                                            }
                                        }

                                        literal = string2;

                                        rebuilt.Append(" + \"</color>\"");
                                        if (literal.Length > 0)
                                            rebuilt.Append(" + ");

                                    }
                                }

                            } while (loop);

                            //Console.WriteLine("literal-final: " + literal);
                            
                            if (verbatim || !literal.Any(char.IsLetter) || IsNumericFormatString(literal))
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
                                if (literal.Length > 0)
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

        /// <summary>
        /// Returns true if the entire input is a valid numeric format string.
        /// </summary>
        //public static class FormatDetector
        //{
            public static bool IsNumericFormatString(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return false;

                // Standard numeric format: N, D, E, F, G, P, R, X with optional precision
                var standardPattern = @"^[NDEFGRPXndefgrpx](\d+)?$";

                // Custom numeric format: made of 0, #, ., ,, %, E0, etc.
                var customPattern = @"^[#0.,%Ee+\-]*$";

                return Regex.IsMatch(input, standardPattern) || Regex.IsMatch(input, customPattern);
            }
        //}

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

