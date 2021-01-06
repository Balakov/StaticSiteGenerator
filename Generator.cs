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

        public class FileProcessContext
        {
            public string InputFilePath { get; set; }
            public string RootFilePath { get; set; }
            public StringReader InputFileContents { get; set; }
            public List<ContentSection> Sections { get; set; } = new List<ContentSection>();
        }

        public class PathResolveResult
        {
            public enum StatusType
            {
                Success,
                Failure
            };

            public StatusType Status { get; set; } = StatusType.Failure;
            public string ResolvedPath { get; set; }
            public string RequestedFile { get; set; }
            public List<string> PathsSearched { get; set; } = new();
        }

        private const string c_includeDirectory = "include";
        private const string c_layoutDirectory = "layout";
        private const string c_assetsDirectory = "assets";
        private const string c_htmlDirectory = "html";
        private const string c_pagesDirectory = "pages";
        private const string c_markdownDirectory = "markdown";
        private const string c_rootFilesDirectory = "rootfiles";

        private string _inputDirectory;
        private string _outputDirectory;
        private bool _includeDebug;
        private static bool _debugLogEnabled = false;

        private readonly MarkdownProcessor _markdown = new MarkdownProcessor();

        // Command: {{ include x.html }}
        private Regex _commandRegex = new Regex(@"{{\s*(?<command>.*?)\s*}}");
        // Special date variable: $(date) = "yyyy-MM-dd"
        private Regex _dateRegex = new Regex(@"\$\(date\)\s*=\s*(""(?<dateformat>.*?)""|'(?<dateformat>.*?)')");
        // Variable usage: {{ $(var) }}
        private Regex _variableUseageRegex = new Regex(@"(?<key>\$\(.*?\))");
        // Variable assigment: {{ $(var) = "value" }}
        private Regex _variableAssignmentRegex = new Regex(@"\$\(.*?\)\s*=\s*(""(?:[^""]).*?""|'(?:[^']).*?')");
        // Ternary Expression: {{ $(var) == "x" ? "true" : "false" }}
        private Regex _variableTernary = new Regex(@"(?<key>\$\(.*?\))\s*==\s*[""'](?<checkvalue>.*)[""']\s*\?\s*[""'](?<truevalue>.*)[""']\s*:\s*[""'](?<falsevalue>.*)[""']");
        // Relative asset link: "assets/"
        private Regex _relativeAssetLink = new Regex(@"""(?<link>assets/\S+?)""|'(?<link>assets/\S+?)'");
        // Relative link: href="file.html"
        private Regex _relativePageLink = new Regex(@"href\s*=\s*(""(?<link>\S+?)""|'(?<link>\S+?)')");
        // {{ section vetbatim }}
        private Regex _verbatimStartMarker = new Regex(@"{{\s*verbatim\s*}}");
        // {{ endsection vetbatim }}
        private Regex _verbatimEndMarker = new Regex(@"{{\s*endverbatim\s*}}");

        private VariableStack _variableStack = new();
        private FileIgnoreList _fileIgnoreList = new();

        public void Generate(string inputDirectory, string outputDirectory, bool includeDebug, bool generateSitemap)
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

                    string processedHTML = ProcessRootFile(file);

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

                            if (!link.Contains("://") && !link.StartsWith('#'))
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

            if (generateSitemap)
            {
                var baseURL = _variableStack.Get("$(site.url)");

                string sitemapContents = CreateSiteMap(siteURLs, baseURL);
                WriteTextIfChanged(Path.Combine(_outputDirectory, "sitemap.xml"), sitemapContents);
                WriteTextIfChanged(Path.Combine(_outputDirectory, "robots.txt"), $"Sitemap: {baseURL}/sitemap.xml");
            }

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
                if (Directory.Exists(rootAssetsPath))
                {
                    foreach (string file in Directory.GetFiles(rootAssetsPath, "*", SearchOption.AllDirectories))
                    {
                        string destinationPath = rootDestinationPath + file.Substring(rootAssetsPath.Length);
                        SafeFileCopy.Copy(file, destinationPath, _fileIgnoreList);
                    }
                }
            }

            // Copy any files in the rootfiles directory to the output directory
            string rootFilesDirectory = Path.Combine(_inputDirectory, c_rootFilesDirectory);

            if (Directory.Exists(rootFilesDirectory))
            {
                foreach (string file in Directory.GetFiles(rootFilesDirectory))
                {
                    SafeFileCopy.Copy(file, Path.Combine(_outputDirectory, Path.GetFileName(file)), _fileIgnoreList);
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

        private string ProcessRootFile(string inputFilePath)
        {
            DebugLog($"Processing root file {inputFilePath}");

            StringReader fileContentsReader;

            if (Path.GetExtension(inputFilePath) == ".md")
            {
                string markdownContents = _markdown.Transform(SafeFileReader.ReadAllText(inputFilePath));
                fileContentsReader = new StringReader(markdownContents);
            }
            else
            {
                fileContentsReader = new StringReader(SafeFileReader.ReadAllText(inputFilePath));
            }

            FileProcessContext context = new()
            {
                RootFilePath = inputFilePath,
                InputFilePath = inputFilePath,
                InputFileContents = fileContentsReader
            };

            return ProcessFile(context);
        }

        private string ProcessLayoutFile(string layoutFilePath, FileProcessContext context)
        {
            FileProcessContext layoutContext = new()
            {
                Sections = context.Sections,
                RootFilePath = context.RootFilePath,
                InputFilePath = layoutFilePath,
                InputFileContents = new StringReader(SafeFileReader.ReadAllText(layoutFilePath))
            };

            return ProcessFile(layoutContext);
        }

        private string ProcessFile(FileProcessContext context)
        {
            DebugLog($"    Processing file {context.InputFilePath}");

            StringBuilder currentOutput = new StringBuilder();
            ContentSection currentSection = null;
            bool writeVerbatim = false; // If this is true we won't perform any processing on the lines and output the content vebatim.

            string layoutToUse = null;

            if (context.InputFilePath != null && Path.GetExtension(context.InputFilePath) == ".md")
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
            while((line = context.InputFileContents.ReadLine()) != null)
            {
                lineCount++;

                // Multi-line support
                if (line.Contains("{{") && !line.Contains("}}"))
                {
                    bool foundClosingMarker = false;
                    while (!foundClosingMarker)
                    {
                        string continuationLine = context.InputFileContents.ReadLine();

                        if (continuationLine != null)
                        {
                            line += continuationLine;
                            if (continuationLine.Contains("}}"))
                            {
                                foundClosingMarker = true;
                            }
                        }
                        else
                        {
                            break;
                        }
                    }
                }

                // Check for verbatim output start and end markers
                if (_verbatimStartMarker.IsMatch(line))
                {
                    writeVerbatim = true;
                }
                else if (_verbatimEndMarker.IsMatch(line))
                {
                    writeVerbatim = false;
                }
                else
                {
                    if (writeVerbatim)
                    {
                        if (currentSection != null)
                        {
                            currentSection.Content.AppendLine(line);
                        }
                        else
                        {
                            currentOutput.AppendLine(line);
                        }

                        continue;
                    }
                }

                // Variable setter
                if (line.StartsWith("$("))
                {
                    _variableStack.AddVariable(line);
                }
                else
                {
                    // Process any {{ }} commands

                    string processedOutput = _commandRegex.Replace(line, commandMatch =>
                    {
                        string commandString = commandMatch.Groups["command"].Value;
                        string[] parts = commandString.Split(" ", StringSplitOptions.RemoveEmptyEntries);
                        string command = parts[0];

                        if (command.StartsWith("$("))
                        {
                            return ProcessVariable(commandString);
                        }
                        else if (command == "debug-vars" || command == "dump-vars")
                        {
                            StringBuilder debugOutput = new("<h5>Variables</h5><ul>" + _variableStack.Print("<li>", "</li>") + "</ul>");

                            if (context.Sections.Count > 0)
                            {
                                debugOutput.AppendLine("<h5>Sections</h5><ul>");
                                foreach (var section in context.Sections)
                                {
                                    debugOutput.AppendLine($"<li>{section.Name}</li>");
                                }
                                debugOutput.AppendLine("</ul>");
                            }

                            return debugOutput.ToString();
                        }
                        else if (command == "layout")
                        {
                            layoutToUse = (parts.Length > 0) ? parts[1] : null;

                            // Process any variable assignments to be passed to the layout
                            var variableAssignmentMatches = _variableAssignmentRegex.Matches(commandString);
                            foreach (Match match in variableAssignmentMatches)
                            {
                                _variableStack.AddVariable(match.Value);
                            }

                            return string.Empty;
                        }
                        else if (command == "section" && parts.Length > 1)
                        {
                            if (currentSection != null)
                            {
                                return OutputHTMLError($"Nesting not allowed. Attempt to nest section \"{parts[1]}\" inside section \"{currentSection.Name}\"", context.InputFilePath, lineCount);
                            }
                            else if (parts[1].ToLower() == "content")
                            {
                                return OutputHTMLError($"Section 'content' is a reserved name and may not be used.", context.InputFilePath, lineCount);
                            }
                            else
                            {
                                currentSection = new() { Name = parts[1] };
                                context.Sections.Add(currentSection);
                            }
                        }
                        else if (command == "endsection")
                        {
                            // If the current section was a markdown section then paste it in-place and remove the section
                            if (currentSection?.Name == "markdown")
                            {
                                string markdownContent = _markdown.Transform(currentSection.Content.ToString());

                                FileProcessContext markdownContext = new()
                                {
                                    InputFileContents = new StringReader(markdownContent),
                                    RootFilePath = context.RootFilePath
                                };

                                string processedHTMLContent = ProcessFile(markdownContext);

                                // Write into the default section
                                currentOutput.AppendLine(processedHTMLContent);
                                context.Sections.Remove(currentSection);
                            }

                            // Revert back to the default section. We don't support nesting of sections
                            currentSection = null;
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

                                // Search the immediate directory and a local include directory before looking in the global one.
                                if (context.InputFilePath != null)
                                {
                                    includePaths.Add(Path.GetDirectoryName(context.InputFilePath));
                                    includePaths.Add(Path.Combine(Path.GetDirectoryName(context.RootFilePath), c_includeDirectory));
                                }

                                // Add the global include path
                                includePaths.Add(Path.Combine(_inputDirectory, c_includeDirectory));

                                var resolveResult = ResolveFilePath(includePaths, param.Replace('\\', '/'));

                                string returnValue;

                                if (resolveResult.Status == PathResolveResult.StatusType.Success)
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
                                        returnValue = _markdown.Transform(SafeFileReader.ReadAllText(resolveResult.ResolvedPath));
                                    }
                                    else if (param.ToLower().EndsWith(".html"))
                                    {
                                        FileProcessContext includeContext = new()
                                        {
                                            Sections = context.Sections,
                                            RootFilePath = context.RootFilePath,
                                            InputFilePath = resolveResult.ResolvedPath,
                                            InputFileContents = new StringReader(SafeFileReader.ReadAllText(resolveResult.ResolvedPath))
                                        };

                                        returnValue = ProcessFile(includeContext);
                                    }
                                    else
                                    {
                                        returnValue = SafeFileReader.ReadAllText(resolveResult.ResolvedPath);
                                    }
                                
                                    _variableStack.Pop();
                                }
                                else
                                {
                                    returnValue = OutputHTMLError($"Include \"{resolveResult.RequestedFile}\" not found." +
                                                                  "<div>Searched For: <ul>" + string.Join("", resolveResult.PathsSearched.Select(x => "<li>" + x + "</li>")) + "</ul></div>",
                                                                  context.InputFilePath, lineCount);
                                }

                                return returnValue;
                            }

                            return string.Empty;
                        }
                        else
                        {
                            // Unknown command - see if we have a matching section names or variables.
                            // It's possible to have multiple sections with the same name if they comes from 
                            // different include files, so concatenate all matches.
                            StringBuilder sectionContent = new StringBuilder();
                            bool foundSectionMatch = false;

                            foreach (var section in context.Sections)
                            {
                                if (string.Compare(section.Name, command, ignoreCase: true) == 0)
                                {
                                    foundSectionMatch = true;
                                    sectionContent.AppendLine(section.Content.ToString());
                                }
                            }

                            if (foundSectionMatch)
                            {
                                return sectionContent.ToString();
                            }

                            return ProcessVariable("$(" + command + ")");
                        }

                        return string.Empty;
                    });

                    if (currentSection != null)
                    {
                        currentSection.Content.AppendLine(processedOutput);
                    }
                    else
                    {
                        currentOutput.AppendLine(processedOutput);
                    }
                }
            }

            if (layoutToUse != null)
            {
                // Add a "content" section containing all of the output generated so far and send it to the layout file.
                context.Sections.Add(new ContentSection() { Name = "content", Content = currentOutput });

                return ProcessLayoutFile(Path.Combine(_inputDirectory, c_layoutDirectory, layoutToUse), context);
            }
            else
            {
                return currentOutput.ToString();
            }
        }

        private static PathResolveResult ResolveFilePath(IEnumerable<string> pathsInPriorityOrder, string filename)
        {
            PathResolveResult result = new()
            {
                RequestedFile = filename
            };

            foreach (var path in pathsInPriorityOrder)
            {
                string fullPath = Path.Combine(path, filename);

                result.PathsSearched.Add(fullPath);

                if (File.Exists(fullPath))
                {
                    result.ResolvedPath = fullPath;
                    result.Status = PathResolveResult.StatusType.Success;
                    return result;
                }
            }

            return result;
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

        private static string OutputHTMLError(string text, string file, int line)
        {
            string fileText = (file != null) ? $"\"{file}\"" :
                                               $"embedded markdown";

            return $"<div style='background-color:#b53b95; color: white; padding:10px; border-radius: 5px; margin: 3px; font-family:monospace'>{text}<div>In {fileText} on line {line}.</div>";
        }

        private static void DebugLog(string text)
        {
            if (_debugLogEnabled)
            {
                Console.WriteLine(text);
            }
        }

        // End
    }
}
