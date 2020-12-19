using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace StaticSiteGenerator
{
    public class FileIgnoreList
    {
        private List<Regex> _regexList = new();

        public void LoadConfig(string path)
        {
            if (File.Exists(path))
            {
                foreach (string line in File.ReadAllLines(path))
                {
                    string trimmedLine = line.Trim();

                    if (!string.IsNullOrEmpty(trimmedLine))
                    {
                        _regexList.Add(new Regex(trimmedLine));
                    }
                }
            }
        }

        public bool ShouldCopy(string path)
        {
            foreach (var regex in _regexList)
            {
                if (regex.IsMatch(path))
                {
                    return false;
                }
            }

            return true;
        }
    }
}
