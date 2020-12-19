using System;
using System.IO;

namespace StaticSiteGenerator
{
    public static class SafeFileReader
    {
        public static string[] ReadAllLines(string path)
        {
            if (File.Exists(path))
            {
                int counter = 10;
                while (counter > 0)
                {
                    try
                    {
                        return File.ReadAllLines(path);
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    counter--;
                }
            }

            return Array.Empty<string>();
        }
        
        public static string ReadAllText(string path)
        {
            if (File.Exists(path))
            {
                int counter = 10;
                while (counter > 0)
                {
                    try
                    {
                        return File.ReadAllText(path);
                    }
                    catch
                    {
                        System.Threading.Thread.Sleep(100);
                    }
                    
                    counter--;
                }
            }

            return string.Empty;
        }
    }
}
