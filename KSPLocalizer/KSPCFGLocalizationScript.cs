// KSP_Part_Localizer.cs

using System.Globalization;
using System.Text;

namespace KspLocalizer
{
    internal static class KSPCFGPartLocalizer
    {
        // --------------------------------------------------------
        // Main localization workflow
        // --------------------------------------------------------
        internal static int LocalizeParts(string gameDataPath, string prefix, int maxLength,
              IDictionary<string, KSPLocalizer.Literal> keyToText, string outdir, bool numerictags, bool csonly)
        {
            var textToKey = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); // NEW: reverse map for dedup
            var dupCounters = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            var cfgFiles = Directory.EnumerateFiles(gameDataPath, "*.cfg", SearchOption.AllDirectories).ToList();
            int modifiedFiles = 0;
            int initialKeyToTextCount = 0;
            if (!csonly)
            {
                initialKeyToTextCount = keyToText.Count;

                foreach (string file in cfgFiles)
                {
                    if (!PatternSearch.ContainsAny(Path.GetFileName(file), KSPLocalizer.excludeFiles))
                    {
                        var originalLines = File.ReadAllLines(file);
                        var newLines = new List<string>(originalLines.Length);
                        bool changed = false;

                        foreach (string line in originalLines)
                        {
                            string trimmed = line.TrimStart();

                            // Skip comments and lines without '='
                            if (trimmed.StartsWith("//") || !trimmed.Contains('='))
                            {
                                newLines.Add(line);
                                continue;
                            }

                            int eq = trimmed.IndexOf('=');
                            string field = trimmed[..eq].Trim();
                            string value = trimmed[(eq + 1)..].Trim();

                            if (!IsDisplayField(field) || value.Length == 0 || value.StartsWith('#') || IsNumeric(value))
                            {
                                newLines.Add(line);
                                continue;
                            }

                            // Reuse existing key if we've already seen this exact string
                            if (!textToKey.TryGetValue(value, out string key))
                            {
                                key = MakeKey(prefix, value, maxLength, keyToText, dupCounters, numerictags);
                                textToKey[value] = key;
                                keyToText[key] = new KSPLocalizer.Literal(value, KSPLocalizer.Origin.cs); // record once per unique text
                            }

                            // rebuild the line with localized value
                            string rebuilt = line.Substring(0, line.IndexOf('=') + 1) + " #" + key;
                            newLines.Add(rebuilt);
                            changed = true;
                        }

                        if (changed)
                        {
                            SaveWithBackup(file, newLines);
                            modifiedFiles++;
                        }
                    }
                }
            }
            WriteLocalizationFiles(gameDataPath, keyToText, outdir);

            int c = keyToText.Count - initialKeyToTextCount;

            Console.WriteLine($"\nCfg Files Processed:\n");

            Console.WriteLine($" {cfgFiles.Count} *.cfg files; modified {modifiedFiles}; generated {c} unique keys.");
            Console.WriteLine($"\nTotal of {keyToText.Count} keys");
            return 0;
        }

        // --------------------------------------------------------
        // Helpers
        // --------------------------------------------------------
        private static void SaveWithBackup(string file, IEnumerable<string> newLines)
        {
            string bak = file + ".bak";
            if (!File.Exists(bak))
                File.Copy(file, bak);
            File.WriteAllLines(file, newLines);
            Console.WriteLine($"Updated {file}");

        }

        /// <summary>
        /// Determines if a cfg field should be localized.
        /// </summary>
        private static bool IsDisplayField(string field)
        {
            field = field.ToLowerInvariant();

            if (field is "title" or "description" or "manufacturer" or "label" or "guiname" or "tags" or "tooltip")
                return true;

            // action & event name heuristics
            if (field.EndsWith("actionname", StringComparison.Ordinal) ||
                field.EndsWith("eventname", StringComparison.Ordinal))
                return true;

            // variants like <something>ActionName/EventName
            if ((field.Contains("action") || field.Contains("event")) && field.EndsWith("name"))
                return true;

            if (PatternSearch.ContainsAny(field, KSPLocalizer.includeStrings))
                return true;

            if (IsScienceResult(field))
                return true;

            return false;
        }

        static internal List<string> celestialBodies = new List<string>();


        static string[] situationStates = new[]
        {
            "SrfLanded",
            "SrfSplashed",
            "InSpace",
            "FlyingLow",
            "FlyingHigh",
            "InSpaceLow",
            "InSpaceHigh"
        };

        static bool IsScienceResult(string field)
        {
            if (field is "default")
                return true;

            foreach (var c in celestialBodies)
            {
                foreach (var s in situationStates)
                {
                    string str = (c + s).ToLower();
                    if (field == str)
                        return true;
                    if (field.Contains(str)) 
                        return true;
                }
            }
            return false;
        }

        private static bool IsNumeric(string s) =>
            double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out _);

        private static string MakeKey(string prefix, string text, int maxLen,
                                      IDictionary<string, KSPLocalizer.Literal> existing,
                                      IDictionary<string, int> dupCounters,
                                      bool numerictags)
        {
            if (existing.TryGetValue(text, out KSPLocalizer.Literal curKey))
                return curKey.literal;

            if (numerictags)
            {
                int tagkey = KSPLocalizer.GetNextTag;
                string key = $"{prefix}_{tagkey}";
                existing[key] = new KSPLocalizer.Literal(text, KSPLocalizer.Origin.cfg);

                return key;
            }


            string sanitized = Sanitize(text);
            if (sanitized.Length > maxLen) sanitized = sanitized[..maxLen];
            string baseKey = $"{prefix}_{sanitized}";

            // If key unused, we’re done
            if (!existing.ContainsKey(baseKey))
                return baseKey;

            // Collision → append _DUPn
            dupCounters.TryGetValue(baseKey, out int n);
            dupCounters[baseKey] = ++n;
            return $"{baseKey}_DUP{n}";
        }

        private static string Sanitize(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (char.IsLetterOrDigit(c)) sb.Append(c);
                else if (char.IsWhiteSpace(c)) sb.Append('_');
            }
            return sb.ToString();
        }

        // --------------------------------------------------------
        // Backup restore
        // --------------------------------------------------------
        private static int RestoreBackups(string root)
        {
            int restored = 0;
            foreach (string bak in Directory.EnumerateFiles(root, "*.cfg.bak", SearchOption.AllDirectories))
            {
                string original = bak[..^4]; // remove .bak
                File.Copy(bak, original, overwrite: true);
                restored++;
            }
            Console.WriteLine($"Restored {restored} files from backups.");
            return 0;
        }

        // --------------------------------------------------------
        // Localization file writers
        // --------------------------------------------------------
        private static void WriteLocalizationFiles(string gameDataPath, IDictionary<string, KSPLocalizer.Literal> keyToText, string outdir)
        {
            string locDir = Path.Combine(gameDataPath, KspCSLocalizer.LocalizationFolder);
            if (outdir != "")
                locDir = outdir;
            Directory.CreateDirectory(locDir);

            WriteCfg(Path.Combine(locDir, "en-us.cfg"), keyToText);
            WriteCsv(Path.Combine(locDir, "en-us.csv"), keyToText);
        }

        private static void WriteCfg(string path, IDictionary<string, KSPLocalizer.Literal> keyToText)
        {
            if (KSPLocalizer.separatePartsCfg)
            {
                string p = path.Replace("en-us", "en-us-cs");
                WriteCfgFile(p, keyToText, KSPLocalizer.Origin.cs);
                p = path.Replace("en-us", "en-us-cfg");
                WriteCfgFile(path, keyToText, KSPLocalizer.Origin.cfg);
            }
            else
                WriteCfgFile(path, keyToText, KSPLocalizer.Origin.both);
        }

        private static void WriteCfgFile(string path, IDictionary<string, KSPLocalizer.Literal> keyToText, KSPLocalizer.Origin origin)
        {
            using var sw = new StreamWriter(path, true, Encoding.UTF8);
            sw.WriteLine("// Autogenerated by KSP_Localizer");
            sw.WriteLine("Localization");
            sw.WriteLine("{");
            sw.WriteLine("    en-us");
            sw.WriteLine("    {");
            foreach (var kvp in keyToText)
            {
                if (kvp.Value.origin == origin || origin == KSPLocalizer.Origin.both)
                {
                    string key = "";
                    foreach (var c in kvp.Value.literal)
                    {
                        if (c == '\n')
                        {
                            key += '\\';
                            key += 'n';
                        }
                        else
                            key += c;
                    }

                    sw.WriteLine($"        #{kvp.Key} = {key}");
                }
            }
            sw.WriteLine("    }");
            sw.WriteLine("}");
        }

        private static void WriteCsv(string path, IDictionary<string, KSPLocalizer.Literal> keyToText)
        {
            using var sw = new StreamWriter(path, false, Encoding.UTF8);
            sw.WriteLine("Key,Text");
            foreach (var kvp in keyToText)
            {
                string textEscaped = kvp.Value.literal.Replace("\"", "\"\"");

                string key = "";
                foreach (var c in textEscaped)
                {
                    if (c == '\n')
                    {
                        key += '\\';
                        key += 'n';
                    }
                    else
                        key += c;
                }

                sw.WriteLine($"\"{kvp.Key}\",\"{key}\"");
            }
        }
    }
}

