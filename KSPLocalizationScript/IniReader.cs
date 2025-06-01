using static KspLocalizer.KSPLocalizer;

namespace KspLocalizer
{
    internal class IniReader
    {
        public static void ReadIniFile(
               string filePath,
               out HashSet<SearchPattern> includeStrings,
               out HashSet<SearchPattern> includeFiles,
               out HashSet<SearchPattern> excludeStrings,
               out HashSet<SearchPattern> excludeFiles)
        {
            includeStrings = new HashSet<SearchPattern>();
            includeFiles = new HashSet<SearchPattern>();
            excludeStrings = new HashSet<SearchPattern>();
            excludeFiles = new HashSet<SearchPattern>();

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
                }
            }
        }

    }
}