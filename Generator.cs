using MarkdownSharp;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace StaticSiteGenerator
{
    public class Generator
    {
        public class ContentSection
        {
            public string Name { get; set; }
            public StringBuilder Content { get; set; } = new();
        }

        private const string c_includeDirectory = "include";
        private const string c_layoutDirectory = "layout";
        private const string c_assetsDirectory = "assets";
        private const string c_htmlDirectory = "html";
        private const string c_pagesDirectory = "pages";
        private const string c_markdownDirectory = "markdown";

        private string _inputDirectory;
        private string _outputDirectory;
        private bool _includeDebug;

        private readonly Markdown _markdown = new Markdown();

        // Command: {{ include x.html }}
        private Regex _commandRegex = new Regex(@"{{\s*(?<command>.*?)\s*}}");
        // Special date variable: $(date) = "yyyy-MM-dd"
        private Regex _dateRegex = new Regex(@"\$\(date\)\s*=\s*(""(?<dateformat>.*?)""|'(?<dateformat>.*?)')");
        // Variable usage: {{ $(var) }}
        private Regex _variableUseageRegex = new Regex(@"(?<key>\$\(.*?\))");
        // Variable assigment: {{ $(var) = "value" }}
        private Regex _variableAssignmentRegex = new Regex(@"\$\(.*?\)\s*=\s*(""(?:[^""]).*?""|'(?:[^']).*?')");
        // Ternary Expression: {{ $(var) == "x" ? "true" : "false"
        private Regex _variableTernary = new Regex(@"(?<key>\$\(.*?\))\s*==\s*[""'](?<checkvalue>.*)[""']\s*\?\s*[""'](?<truevalue>.*)[""']\s*:\s*[""'](?<falsevalue>.*)[""']");
        // Relative asset link: "assets/"
        private Regex _relativeAssetLink = new Regex(@"""(?<link>assets/\S+?)""|'(?<link>assets/\S+?)'");
        // Relative link: href="file.html"
        private Regex _relativePageLink = new Regex(@"href\s*=\s*(""(?<link>\S+?)""|'(?<link>\S+?)')");

        private VariableStack _variableStack = new();
        private FileIgnoreList _fileIgnoreList = new();

        public void Generate(string inputDirectory, string outputDirectory, bool includeDebug)
        {
            _inputDirectory = inputDirectory;
            _outputDirectory = outputDirectory;
            _includeDebug = includeDebug;

            _variableStack.LoadRootVariables(Path.Combine(_inputDirectory, "variables.txt"));
            _fileIgnoreList.LoadConfig(Path.Combine(_inputDirectory, "ignore.txt"));

            if (!Directory.Exists(_outputDirectory))
            {
                Directory.CreateDirectory(_outputDirectory);
            }

            List<string> siteURLs = new();
            Dictionary<string, IEnumerable<string>> rootFiles = new();
            (string directory, string searchPattern)[] rootDirectories =
            {
                (c_htmlDirectory, "*.html"),
                (c_pagesDirectory, "*.html"),
                (c_markdownDirectory, "*.md")
            };

            foreach (var rootDirectory in rootDirectories)
            {
                var directoryPath = Path.Combine(_inputDirectory, rootDirectory.directory);

                if (Directory.Exists(directoryPath))
                {
                    var fileList = EnumerateRootFiles(directoryPath, rootDirectory.searchPattern);

                    rootFiles.Add(directoryPath, fileList);
                }
            }

            foreach (var pair in rootFiles)
            {
                string rootInputDirectory = pair.Key;
                IEnumerable<string> files = pair.Value;

                foreach (string file in files)
                {
                    // Create a new variable stack to make sure any variables created within the 
                    // HTML file are local to that file.
                    _variableStack.Push();

                    string relativePath = Path.GetDirectoryName(file).Substring(rootInputDirectory.Length).Trim('/', '\\');
                    // This may have been a markdown file that we converted to HTML
                    string outputFileName = Path.ChangeExtension(Path.GetFileName(file), ".html");

                    // The number of backslashes in the path indicate how many levels away
                    // this path is from the current root. We'll need this information to
                    // fix up any relative asset paths.
                    int depthFromRoot = string.IsNullOrEmpty(relativePath) ? 0 : relativePath.Split('/', '\\').Length;

                    string processedHTML = ProcessFile(file, null);

                    bool addToSitemap = !File.Exists(Path.Combine(Path.GetDirectoryName(file), "norobots.txt"));
                    if (addToSitemap)
                    {

                        if (depthFromRoot > 0)
                        {
                            siteURLs.Add(relativePath + "/" + outputFileName);
                        }
                        else
                        {
                            siteURLs.Add(outputFileName);
                        }
                    }

                    // Fix up any relative assets paths
                    if (depthFromRoot > 0)
                    {
                        processedHTML = _relativePageLink.Replace(processedHTML, linkMatch =>
                        {
                            string link = linkMatch.Groups["link"].Value;

                            if (!link.Contains("://"))
                            {
                                for (int i = 0; i < depthFromRoot; i++)
                                {
                                    link = "../" + link;
                                }

                                return "href=\"" + link.Replace('\\', '/') + '"';
                            }
                            else
                            {
                                return linkMatch.Value;
                            }
                        });

                        // This must come after the href processing otherwise we might
                        // convert href="assets/site.css" to href="../assets/site.css"
                        // and then convert it again when processing hrefs.
                        processedHTML = _relativeAssetLink.Replace(processedHTML, link =>
                        {
                            string relativeLink = link.Groups["link"].Value;

                            for (int i = 0; i < depthFromRoot; i++)
                            {
                                relativeLink = "../" + relativeLink;
                            }

                            return '"' + relativeLink + '"';
                        });
                    }

                    string outputPath = Path.Combine(_outputDirectory, relativePath, outputFileName);

                    WriteTextIfChanged(outputPath, processedHTML);

                    _variableStack.Pop();
                }
            }

            // Create a sitemap and robots.txt

            var baseURL = _variableStack.Get("$(site.url)");

            string sitemapContents = CreateSiteMap(siteURLs, baseURL);
            WriteTextIfChanged(Path.Combine(_outputDirectory, "sitemap.xml"), sitemapContents);
            WriteTextIfChanged(Path.Combine(_outputDirectory, "robots.txt"), $"Sitemap: {baseURL}/sitemap.xml");

            // Copy the assets to the output directory - TODO: Minification here?
            // Search for assets subdirectories within the pages and html directories as well.

            List<string> rootAssetsPaths = new() { Path.Combine(_inputDirectory, c_assetsDirectory) };
            string rootDestinationPath = Path.Combine(_outputDirectory, c_assetsDirectory);

            foreach (var rootDirectory in rootDirectories)
            {
                var directoryPath = Path.Combine(_inputDirectory, rootDirectory.directory);

                if (Directory.Exists(directoryPath))
                {
                    rootAssetsPaths.AddRange(Directory.GetDirectories(directoryPath, c_assetsDirectory, SearchOption.AllDirectories));
                }
            }

            foreach (string rootAssetsPath in rootAssetsPaths)
            {
                foreach (string file in Directory.GetFiles(rootAssetsPath, "*", SearchOption.AllDirectories))
                {
                    if (!_fileIgnoreList.ShouldCopy(file))
                    {
                        continue;
                    }

                    string destinationPath = rootDestinationPath + file.Substring(rootAssetsPath.Length);
                    string destinationDirectory = Path.GetDirectoryName(destinationPath);

                    if (!Directory.Exists(destinationDirectory))
                    {
                        Directory.CreateDirectory(destinationDirectory);
                    }

                    FileInfo sourceFileInfo = new FileInfo(file);
                    FileInfo destinationFileInfo = new FileInfo(destinationPath);

                    if (!destinationFileInfo.Exists ||
                        destinationFileInfo.LastWriteTimeUtc < sourceFileInfo.LastWriteTimeUtc)
                    {
                        // Sometimes the file copy may fail because the browser is accessing the file - retry a few times
                        int counter = 10;
                        while (counter > 0)
                        {
                            try
                            {
                                File.Copy(file, destinationPath.TrimEnd(), true);
                                break;
                            }
                            catch
                            {
                                counter--;
                            }
                        }

                        if (counter == 0)
                        {
                            Console.WriteLine($"File copy failed for {destinationPath} - probably in-use.");
                        }
                    }
                }
            }
        }

        private IEnumerable<string> EnumerateRootFiles(string path, string searchPattern)
        {
            List<string> fileList = new();
            
            fileList.AddRange(Directory.GetFiles(path, searchPattern, SearchOption.TopDirectoryOnly));

            foreach (var subDirectory in Directory.GetDirectories(path))
            {
                var directoryName = Path.GetFileName(subDirectory).ToLower();

                if (directoryName != c_includeDirectory.ToLower() &&
                    directoryName != c_assetsDirectory.ToLower())
                {
                    fileList.AddRange(EnumerateRootFiles(subDirectory, searchPattern));
                }
            }

            return fileList;
        }

        private string ProcessFile(string inputFilePath, List<ContentSection> contentSectionsToInsert)
        {
            if (Path.GetExtension(inputFilePath) == ".md")
            {
                string markdownContents = _markdown.Transform(SafeFileReader.ReadAllText(inputFilePath));

                return ProcessFile(inputFilePath, new StringReader(markdownContents), contentSectionsToInsert);
            }
            else
            {
                return ProcessFile(inputFilePath, new StringReader(SafeFileReader.ReadAllText(inputFilePath)), contentSectionsToInsert);
            }
        }

        private string ProcessFile(string inputFilePath, StringReader inputFileContents, List<ContentSection> contentSectionsToInsert)
        {
            List<ContentSection> sections = new();

            // Add a default content section that all of the content that isn't inside a named section will be placed into.
            ContentSection defaultContentSection = new() { Name = "content" };
            sections.Add(defaultContentSection);
            
            ContentSection currentContentSection = defaultContentSection;

            string layoutToUse = null;

            if (inputFilePath != null && Path.GetExtension(inputFilePath) == ".md")
            {
                // You can set the layout in a markdown file in the same way as an HTML file with the {{ layout }}
                // command, but to make a site from just markdown files as easy as possible we'll pick the first HTML
                // file in the layouts directory as a default.

                string layoutDirectory = Path.Combine(_inputDirectory, c_layoutDirectory);
                if (Directory.Exists(layoutDirectory))
                {
                    string defaultLayoutFile = Directory.GetFiles(layoutDirectory, "*.html").FirstOrDefault();

                    if (defaultLayoutFile != null)
                    {
                        layoutToUse = Path.GetFileName(defaultLayoutFile);
                    }
                }
            }

            string line;
            int lineCount = 0;
            while((line = inputFileContents.ReadLine()) != null)
            {
                lineCount++;

                if (line.StartsWith("$("))
                {
                    _variableStack.AddVariable(line);
                }
                else
                {
                    string processedOutput = _commandRegex.Replace(line, commandMatch =>
                    {
                        string commandString = commandMatch.Groups["command"].Value;
                        string[] parts = commandString.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        string command = parts[0];

                        if (command.StartsWith("$("))
                        {
                            return ProcessVariable(command);
                        }
                        else if (command == "debug-vars" || command == "dump-vars")
                        {
                            return "<ul>" + _variableStack.Print("<li>", "</li>") + "</ul>";
                        }
                        else if (command == "layout")
                        {
                            layoutToUse = (parts.Length > 0) ? parts[1] : null;
                            return string.Empty;
                        }
                        else if (command == "section" && parts.Length > 1)
                        {
                            ContentSection newSection = new() { Name = parts[1] };
                            sections.Add(newSection);
                            currentContentSection = newSection;
                        }
                        else if (command == "endsection")
                        {
                            // If the current section was a markdown section then paste it in-place and remove the section
                            if (currentContentSection.Name == "markdown")
                            {
                                string markdownContent = _markdown.Transform(currentContentSection.Content.ToString());
                                string processedHTMLContent = ProcessFile(null, new StringReader(markdownContent), null);
                                defaultContentSection.Content.AppendLine(processedHTMLContent);
                                sections.Remove(currentContentSection);
                            }

                            currentContentSection = defaultContentSection;
                        }
                        else if ((command == "include" && parts.Length > 1) ||
                                 ((command == "include-debug" || command == "includedebug")  && parts.Length > 1) ||
                                 ((command == "include-if"    || command == "includeif")     && parts.Length > 2))
                        {
                            int filenameParamaterIndex = 1;

                            if (command == "include-if" || command == "includeif")
                            {
                                if (string.IsNullOrEmpty(_variableStack.Get(parts[1])))
                                {
                                    return string.Empty;
                                }

                                filenameParamaterIndex = 2;
                            }
                            else if (command == "include-debug" || command == "includedebug")
                            {
                                if (!_includeDebug)
                                {
                                    return string.Empty;
                                }
                            }

                            if (filenameParamaterIndex < parts.Length)
                            {
                                // Resolve any variables in the file name
                                var param = _variableUseageRegex.Replace(parts[filenameParamaterIndex], x =>
                                {
                                    return ProcessVariable(x.Value);
                                });

                                List<string> includePaths = new();

                                // Search for a local include directory before looking in the global one.
                                if (inputFilePath != null)
                                {
                                    includePaths.Add(Path.Combine(Path.GetDirectoryName(inputFilePath), c_includeDirectory));
                                }

                                // Add the global include path
                                includePaths.Add(Path.Combine(_inputDirectory, c_includeDirectory));

                                string includePath = ResolveFilePath(includePaths, param.Replace('\\', '/'));

                                string returnValue;

                                if (includePath != null)
                                {
                                    // Create a new variable context before we add any paramters so that the new values
                                    // are only accessbile to the included file
                                    _variableStack.Push();

                                    var variableAssignmentMatches = _variableAssignmentRegex.Matches(commandString);
                                    foreach (Match match in variableAssignmentMatches)
                                    {
                                        _variableStack.AddVariable(match.Value);
                                    }

                                    if (param.ToLower().EndsWith(".md"))
                                    {
                                        returnValue = _markdown.Transform(SafeFileReader.ReadAllText(includePath));
                                    }
                                    else if (param.ToLower().EndsWith(".html"))
                                    {
                                        returnValue = ProcessFile(includePath, null);
                                    }
                                    else
                                    {
                                        returnValue = SafeFileReader.ReadAllText(includePath);
                                    }
                                }
                                else
                                {
                                    if (inputFilePath != null)
                                    {
                                        returnValue = $"<div style='background-color:#b53b95; color: white; padding:10px; border-radius: 5px; margin: 3px; font-family:monospace'>Include \"{includePath}\" not found when processing \"{inputFilePath}\" on line {lineCount}</div>";
                                    }
                                    else
                                    {
                                        returnValue = $"<div style='background-color:#b53b95; color: white; padding:10px; border-radius: 5px; margin: 3px; font-family:monospace'>Include \"{includePath}\" not found when processing embedded markdown on line {lineCount}</div>";
                                    }
                                }

                                _variableStack.Pop();

                                return returnValue;
                            }

                            return string.Empty;
                        }
                        else
                        {
                            // Unknown command - see if we have a matching section names or variables. Only check the sections if we're processing a layout file
                            // as they're not allowed in included files.
                            if (contentSectionsToInsert != null)
                            {
                                foreach (var section in contentSectionsToInsert)
                                {
                                    if (string.Compare(section.Name, command, ignoreCase: true) == 0)
                                    {
                                        return section.Content.ToString();
                                    }
                                }
                            }
                            else
                            {
                                return ProcessVariable("$(" + command + ")");
                            }
                        }

                        return string.Empty;
                    });

                    currentContentSection.Content.AppendLine(processedOutput);
                }
            }

            if (layoutToUse != null)
            {
                return ProcessFile(Path.Combine(_inputDirectory, c_layoutDirectory, layoutToUse), sections);
            }
            else
            {
                return defaultContentSection.Content.ToString();
            }
        }

        private static string ResolveFilePath(IEnumerable<string> pathsInPriorityOrder, string filename)
        {
            foreach (var path in pathsInPriorityOrder)
            {
                string fullPath = Path.Combine(path, filename);
                if (File.Exists(fullPath))
                {
                    return fullPath;
                }
            }

            return null;
        }

        private string ProcessVariable(string variable)
        {
            var dateMatch = _dateRegex.Match(variable);

            if (dateMatch.Success)
            {
                return DateTime.Now.ToString(dateMatch.Groups["dateformat"].Value);
            }
            else
            {
                var ternaryMarch = _variableTernary.Match(variable);
                if (ternaryMarch.Success)
                {
                    string checkVariable = ternaryMarch.Groups["key"].Value;
                    string checkValue = ternaryMarch.Groups["checkvalue"].Value;
                    string trueValue = ternaryMarch.Groups["truevalue"].Value;
                    string falseValue = ternaryMarch.Groups["falsevalue"].Value;

                    return _variableStack.Get(checkVariable) == checkValue ? trueValue : falseValue;
                }
                else
                {
                    return _variableStack.Get(variable);
                }
            }
        }

        private static string CreateSiteMap(List<string> urls, string baseURL)
        {
            StringBuilder sitemap = new ();

            sitemap.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            sitemap.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

            foreach (var url in urls)
            {
                sitemap.AppendLine($"<url><loc>{baseURL}/{url}</loc></url>");
            }

            sitemap.AppendLine("</urlset>");

            return sitemap.ToString();
        }

        private static void WriteTextIfChanged(string path, string text)
        {
            // Only write the file if it's changed to prevent Google Drive constantly
            // uploading all of the files when editing the site.

            bool allowWrite = true;

            if (File.Exists(path))
            {
                string contents = File.ReadAllText(path);
                if (contents == text)
                {
                    allowWrite = false;
                }
            }

            if (allowWrite)
            {
                string outputFileDirectory = Path.GetDirectoryName(path);
                if (!Directory.Exists(outputFileDirectory))
                {
                    Directory.CreateDirectory(outputFileDirectory);
                }

                File.WriteAllText(path, text);
            }
        }

        // End
    }
}
