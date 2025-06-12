namespace KspLocalizer
{
    internal class IniReader
    {
        public static void ReadIniFile(
               string filePath,
               ref HashSet<SearchPattern> includeStrings,
               ref HashSet<SearchPattern> includeFiles,
               ref HashSet<SearchPattern> excludeStrings,
               ref HashSet<SearchPattern> excludeFiles)
        {
            string currentSection = "";

            foreach (var line in File.ReadLines(filePath))
            {
                string trimmedLine = line.Trim();

                if (string.IsNullOrEmpty(trimmedLine) || trimmedLine.StartsWith("#") || trimmedLine.StartsWith(";"))
                    continue;

                if (trimmedLine.StartsWith("[") && trimmedLine.EndsWith("]"))
                {
                    currentSection = trimmedLine[1..^1].Trim().ToLower();
                    continue;
                }

                int hashIndex = trimmedLine.IndexOf('#');
                if (hashIndex >= 0)
                    trimmedLine = trimmedLine[..hashIndex].Trim();

                if (string.IsNullOrEmpty(trimmedLine))
                    continue;
                if (currentSection == "")
                {
                    int i = trimmedLine.IndexOf("=");
                    if (i == -1)
                        i = trimmedLine.Length-1;
                    string val = trimmedLine.Substring(i + 1);

                    switch (trimmedLine.Substring(0, i).ToLower())
                    {
                        case "outdir":
                            KSPLocalizer.outdir = val;
                            break;
                        case "prefix":
                            KSPLocalizer.prefix = val;
                            break;
                        case "maxkeylength":
                            KSPLocalizer.maxLength = int.Parse(val);
                            break;
                        case "numerictags":
                            if (val == "" || val.ToLower() == "true")
                                KSPLocalizer.numerictags = true;
                            if (val.ToLower() == "false")
                                KSPLocalizer.numerictags = false;
                            break;
                        case "separatepartscfg":
                            if (val == "" || val.ToLower() == "true")
                                KSPLocalizer.separatePartsCfg = true;
                            if (val.ToLower() == "false")
                                KSPLocalizer.separatePartsCfg = false;
                            break;
                        case "csonly":
                            if (val == "" || val.ToLower() == "true")
                                KSPLocalizer.csonly = true;
                            if (val.ToLower() == "false")
                                KSPLocalizer.csonly = false;
                            break;
                        case "cfgonly":
                            if (val == "" || val.ToLower() == "true")
                                KSPLocalizer.cfgonly = true;
                            if (val.ToLower() == "false")
                                KSPLocalizer.cfgonly = false;
                            break;
                    }
                }
                else
                {


                    bool isFileEntry = trimmedLine.StartsWith("file=");
                    if (isFileEntry)
                        trimmedLine = trimmedLine.Substring(5);

                    bool isRegex = RegexUtils.LooksLikeRegex(trimmedLine);

                    var pattern = new SearchPattern(trimmedLine, isRegex);

                    switch (currentSection)
                    {
                        case "include":
                            if (isFileEntry)
                                includeFiles.Add(pattern);
                            else
                                includeStrings.Add(pattern);
                            break;
                        case "exclude":
                            if (isFileEntry)
                                excludeFiles.Add(pattern);
                            else
                                excludeStrings.Add(pattern);
                            break;
                        case "experimentplanets":
                            KSPCFGPartLocalizer.celestialBodies.Add(trimmedLine);
                            break;
                    }
                }
            }
        }
    }
}