// -----------------------------------------------------------------------------
// Console utility: scans *.cs and *.cfg files, replaces hard‑coded C# string
// literals and strings in cfg files with Kerbal Space Program localization keys
//
// Key features
// ------------
// For C-Sharp code files:
// • Backs up each file to <file>.bak and supports a --revert flag.
// • Skips lines whose first non‑whitespace character is '[' (attributes).
//   until the next semicolon
// • Skips code wrapped in  #region NO_LOCALIZATION … #endregion blocks.
// • Injects `using KSP.Localization;` when missing.
// • Generates keys as  #<prefix>_<sanitised>(≤25)  with automatic _DUP<n>.
//
// For cfg files:
// • Scans Kerbal Space Program part *.cfg files for user‑visible
// • strings—including titles, descriptions, manufacturer labels,
// • action & event names—replacing them with localization keys.
// • Duplicate strings automatically reuse the same key.
// • keeps .bak backups so you can revert with --revert.
// 
// For both:
// • Writes / updates  Localization/en-us.cfg  (KSP format) and en-us.csv.
// 

using KSPLocalizer;

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

        internal static HashSet<SearchPattern> includeStrings = new HashSet<SearchPattern>();
        internal static HashSet<SearchPattern> includeFiles = new HashSet<SearchPattern>();
        internal static HashSet<SearchPattern> excludeStrings = new HashSet<SearchPattern>();
        internal static HashSet<SearchPattern> excludeFiles = new HashSet<SearchPattern>();

        internal static int GetNextTag { get { numericTag++; return numericTag; } }
        internal static bool separatePartsCfg = false;

        internal static string prefix = "MyMod";
        internal static bool revert = false;
        internal static bool cleanbak = false;
        internal static int maxLength = DefaultMaxLength;
        internal static string outdir = "";
        internal static bool csonly = false;
        internal static bool cfgonly = false;
        internal static bool numerictags = false;

        private static void Main(string[] args)
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;

            Console.WriteLine("KSP Localizer version " + VersionInfo.FullVersion);

            string inifile = $"{appPath}/localization.ini";
            IniReader.ReadIniFile(inifile, ref includeStrings, ref includeFiles, ref excludeStrings, ref excludeFiles);

            bool help = false;

            string root = "";
            if (args.Length > 0)
                root = args[0];

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
                    IniReader.ReadIniFile(inifile, ref includeStrings, ref includeFiles, ref excludeStrings, ref excludeFiles);
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
                if (arg.Equals("--help", StringComparison.OrdinalIgnoreCase) ||
                    arg.Equals("-?", StringComparison.OrdinalIgnoreCase) )
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

            if (help || root == "--help" || root == "-?" || args.Length == 0)
            {
                Help.ShowHelp();
                return;
            }


            string locDir = Path.Combine(root, KspCSLocalizer.LocalizationFolder);
            if (outdir != "")
                locDir = outdir;
            if (locDir is null)
                Console.WriteLine("locDir is null: ");

            locDir = EnsurePathWithValidation(locDir);

            Directory.CreateDirectory(locDir);

            string cfgPath = Path.Combine(locDir, KspCSLocalizer.CfgName);
            string csvPath = Path.Combine(locDir, KspCSLocalizer.CsvName);

            if (File.Exists(cfgPath))
                Console.WriteLine("Deleting: " + cfgPath);
            if (File.Exists(csvPath))
                Console.WriteLine("Deleting: " + csvPath);
            Console.WriteLine("\n");
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
