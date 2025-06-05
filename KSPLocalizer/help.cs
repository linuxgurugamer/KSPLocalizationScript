using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Net.Mime.MediaTypeNames;

namespace KspLocalizer
{
    internal class Help
    {
        static internal void ShowHelp()
        {
            string appPath = AppDomain.CurrentDomain.BaseDirectory;

            foreach (var line in File.ReadLines($"{appPath}/README.md"))
            {
                Console.WriteLine(line);
            }
        }
    }
}
