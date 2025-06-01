using System.Text.RegularExpressions;


namespace KspLocalizer
{
    internal static class KSPLocalizer
    {
        internal enum Origin { cs, cfg, both };  // used when writing out the cfg files which type to write, either the tags for only code files, parts files or both

        internal class Literal
        {
            internal string literal;
            internal Origin origin;
            internal Literal(string lit, Origin o)
            {
                literal = lit; ;
                origin = o;
            }
        }

        private const int DefaultMaxLength = 25;
        static int numericTag = 0;

        internal static HashSet<SearchPattern> includeStrings;
        internal static HashSet<SearchPattern> includeFiles;
        internal static HashSet<SearchPattern> excludeStrings;
        internal static HashSet<SearchPattern> excludeFiles ;

        internal static int GetNextTag { get { numericTag++; return numericTag; } }
        internal static bool separatePartsCfg = false;
        private static void Main(string[] args)
        {
            string prefix = "MyMod";
            bool revert = false;
            bool cleanbak = false;
            int maxLength = DefaultMaxLength;
            string outdir = "";
            bool csonly = false;
            bool cfgonly = false;
            bool numerictags = false;
            string appPath = AppDomain.CurrentDomain.BaseDirectory;


            string inifile = $"{appPath}\\localization.ini";
            bool help = false;

            string root = args[0];

            foreach (string arg in args.Skip(1))
            {
                if (arg.StartsWith("--prefix=")) // 9 chars long
                {
                    prefix = arg.Substring(9);
                }
                else
                if (arg.StartsWith("--inifile=")) // 10 chars long
                {
                    inifile = arg.Substring(17);
                }
                else
                if (arg.Equals("--numerictags", StringComparison.OrdinalIgnoreCase))
                {
                    numerictags = true;
                }
                else
                if (arg.Equals("--revert", StringComparison.OrdinalIgnoreCase))
                {
                    revert = true;
                }
                else
                if (arg.Equals("--separatepartscfg", StringComparison.OrdinalIgnoreCase))
                {
                    separatePartsCfg = true;
                }
                else
                if (arg.Equals("--cleanbak", StringComparison.OrdinalIgnoreCase))
                {
                    cleanbak = true;
                }
                else
                if (arg.StartsWith("--maxLength=", StringComparison.OrdinalIgnoreCase) &&
                    int.TryParse(arg[12..], out int ml))
                {
                    maxLength = ml;
                }
                else
                if (arg.StartsWith("--outdir=")) // 9 chars long
                {
                    outdir = arg.Substring(9);
                }
                else
                if (arg.Equals("--csonly", StringComparison.OrdinalIgnoreCase))
                {
                    csonly = true;
                }
                else
                if (arg.Equals("--cfgonly", StringComparison.OrdinalIgnoreCase))
                {
                    cfgonly = true;
                }
                else
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase))
                {
                    help = true;
                }
                else
                {
                    Console.WriteLine("Unknown arg: " + arg);
                }
            }

            if (revert)
            {
                KspCSLocalizer.RestoreBackups(root);
                return;
            }
            if (cleanbak)
            {
                KspCSLocalizer.CleanBackups(root);
                return;
            }

            if (help)
            {
                Help.ShowHelp();
                return;
            }

            string locDir = Path.Combine(root, KspCSLocalizer.LocalizationFolder);
            if (outdir != "")
                locDir = outdir;


            IniReader.ReadIniFile(inifile, out includeStrings, out includeFiles, out excludeStrings, out excludeFiles);


            locDir = EnsurePathWithValidation(locDir);

            Directory.CreateDirectory(locDir);

            string cfgPath = Path.Combine(locDir, KspCSLocalizer.CfgName);
            string csvPath = Path.Combine(locDir, KspCSLocalizer.CsvName);

            if (File.Exists(cfgPath))
                Console.WriteLine("Deleting: " + cfgPath);
            if (File.Exists(csvPath))
                Console.WriteLine("Deleting: " + csvPath);
            File.Delete(cfgPath);
            File.Delete(csvPath);


            var keyMap = new Dictionary<string, Literal>(StringComparer.Ordinal);

            var dupCounts = new Dictionary<string, int>(StringComparer.Ordinal);

            if (!cfgonly)
                KspCSLocalizer.ProcessAllFiles(root, prefix, keyMap, dupCounts, numerictags);

            KSPCFGPartLocalizer.LocalizeParts(root, prefix, maxLength, keyMap, outdir, numerictags, csonly);

        }

        public static string EnsurePathWithValidation(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                throw new ArgumentException("Path cannot be null or whitespace.", nameof(path));

            // Normalize to forward slashes
            string normalizedPath = path.Replace('\\', '/');

            // Split into parts
            string[] parts = normalizedPath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

            if (parts.Length == 0)
                throw new ArgumentException("Path does not contain any valid directory components.", nameof(path));

            // Start with root (for absolute paths on Windows or Unix)
            string currentPath = Path.IsPathRooted(path)
                ? Path.GetPathRoot(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                : "";

            for (int i = 0; i < parts.Length; i++)
            {
                currentPath = Path.Combine(currentPath, parts[i]);

                if (i < parts.Length - 1)
                {
                    if (!Directory.Exists(currentPath))
                    {
                        throw new DirectoryNotFoundException($"Intermediate directory does not exist: {currentPath}");
                    }
                }
                else
                {
                    // Last part: create if needed
                    if (!Directory.Exists(currentPath))
                    {
                        Directory.CreateDirectory(currentPath);
                    }
                }
            }

            // Return the normalized path (with forward slashes)
            return currentPath.Replace('\\', '/') + "/";
        }
    }
}
