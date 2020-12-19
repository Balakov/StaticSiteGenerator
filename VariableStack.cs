using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StaticSiteGenerator
{
    public class VariableStack
    {
        private static Regex _variableAssignmentRegex = new Regex(@"(?<key>\$\(.*?\))\s*=\s*(""(?<value>(?:[^""]).*?)""|'(?<value>(?:[^']).*?)')");

        private class VariableDictionary
        {
            public Dictionary<string, string> Variables { get; } = new();

            public void AddVariable(string line)
            {
                var match = _variableAssignmentRegex.Match(line);

                if (match.Success)
                {
                    var key = match.Groups["key"].Value;
                    var value = match.Groups["value"].Value;

                    if (!Variables.ContainsKey(key))
                    {
                        Variables.Add(key, value);
                    }
                    else
                    {
                        Variables[key] = value;
                    }
                }
            }
        }

        private List<VariableDictionary> _stack = new();

        public VariableStack()
        {
            Push();
        }

        public void LoadRootVariables(string path)
        {
            if (File.Exists(path))
            {
                foreach (string line in SafeFileReader.ReadAllLines(path))
                {
                    _stack.First().AddVariable(line);
                }
            }
        }

        public void Push()
        {
            _stack.Add(new VariableDictionary());
        }
        
        public void Pop()
        {
            _stack.RemoveAt(_stack.Count -1);
        }

        public void AddVariable(string variable)
        {
            _stack.Last().AddVariable(variable);
        }

        public string Get(string variable)
        {
            int stackLength = _stack.Count;

            for (var i=stackLength-1; i >= 0; i--)
            {
                if (_stack[i].Variables.TryGetValue(variable, out var value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        public string Print(string startHTML, string endHTML)
        {
            StringBuilder sb = new();
            int stackLength = _stack.Count;

            for (var i = stackLength - 1; i >= 0; i--)
            {
                foreach(var pair in _stack[i].Variables)
                {
                    sb.Append(startHTML);
                    sb.Append($"(Level {i}) - \"{pair.Key}\" = \"{pair.Value}\"");
                    sb.Append(endHTML);
                }
            }

            return sb.ToString();
        }
    }
}
