using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;

namespace StaticSiteGenerator
{
    class Program
    {
        private const string c_outputDirectory = "_public";
        private static Watcher _watcher = new();
        private static Generator _generator = new();
        private static string _inputDirectory;
        private static string _outputDirectory;
        private static bool _shouldServe = false;
        private static bool _shouldWatch = false;

        static void Main(string[] args)
        {
            foreach (string arg in args)
            {
                if (arg == "--serve")
                {
                    _shouldServe = true;
                }
                else if (arg == "--watch")
                {
                    _shouldWatch = true;
                }
                else
                {
                    _inputDirectory = arg;
                }
            }

            if (string.IsNullOrEmpty(_inputDirectory))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    //_inputDirectory = @"/Users/mikestimpson/Google Drive File Stream/My Drive/Mike/Code/FoghornWebsite";
                    _inputDirectory = @"/Users/mikestimpson/Google Drive File Stream/My Drive/Mike/Code/FoghornWebsiteMarkdown";
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    _inputDirectory = @"D:\GoogleDrive\Mike\Code\FoghornWebsite";
                    //_inputDirectory = @"D:\GoogleDrive\Mike\Code\FoghornWebsiteMarkdown";
                }
            }

            _outputDirectory = Path.Combine(_inputDirectory, c_outputDirectory);

            GenerateSite(_shouldWatch && _shouldServe);

            if(_shouldServe)
            {
                if (_shouldWatch)
                {
                    AddWatchers();
                }

                // This is a blocking call!
                StartWebserver();
            }
        }

        static void GenerateSite(bool includeDebug)
        {
            _generator.Generate(_inputDirectory, _outputDirectory, includeDebug);
        }

        static void AddWatchers()
        {
            List<string> contentDirectories = new List<string>();

            contentDirectories.Add(_inputDirectory);

            string[] watchedDirectories =
            {
                "assets",
                "include",
                "layout",
                "html",
                "pages",
                "markdown"
            };

            foreach (var watchedDirectory in watchedDirectories)
            {
                string dir = Path.Combine(_inputDirectory, watchedDirectory);
                
                if (Directory.Exists(dir))
                {
                    contentDirectories.Add(dir);
                    contentDirectories.AddRange(Directory.GetDirectories(dir, "*", SearchOption.AllDirectories));
                }
            }

            foreach (string contentDirectory in contentDirectories)
            {
                _watcher.AddDirectory(contentDirectory);
            }

            var monitorThread = new System.Threading.Thread(MonitorWatcherThread);
            monitorThread.IsBackground = true;
            monitorThread.Start();
        }

        static void MonitorWatcherThread()
        {
            while (true)
            {
                if (_watcher.ChangesDetected)
                {
                    _watcher.ChangesDetected = false;
                    GenerateSite(_shouldWatch && _shouldServe);
                }

                System.Threading.Thread.Sleep(500);
            }
        }

        static void StartWebserver()
        {
            var builder = WebHost.CreateDefaultBuilder()
                                 .UseStartup<Startup>()
                                 .UseKestrel()
                                 .UseContentRoot(_outputDirectory)
                                 .UseWebRoot(_outputDirectory)
                                 .UseUrls("http://0.0.0.0:5001/")
                                 .ConfigureLogging(logging =>
                                 {
                                     logging.ClearProviders();
                                 })
                                 .Build();

            builder.Run();
        }
    }

    public class Startup
    {
        public void Configure(IApplicationBuilder app, IHostingEnvironment env)
        {
            // https://stackoverflow.com/questions/43090718/setting-index-html-as-default-page-in-asp-net-core
            app.UseFileServer();
        }
    }
}
