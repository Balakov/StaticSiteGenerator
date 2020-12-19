using System;
using System.Collections.Generic;
using System.IO;

namespace StaticSiteGenerator
{
    public class Watcher : IDisposable
    {
        private List<FileSystemWatcher> _watchers = new();

        public object _changeLock = new();
        private bool _changesDetected = false;
        public bool ChangesDetected
        {
            get
            {
                lock (_changeLock)
                {
                    return _changesDetected;
                }
            }

            set 
            {
                lock (_changeLock)
                {
                    _changesDetected = value;
                }
            }
        }

        public void AddDirectory(string directory)
        {
            // Create a new FileSystemWatcher and set its properties.
            var watcher = new FileSystemWatcher();
            
            watcher.Path = directory;

            // Watch for changes in LastAccess and LastWrite times, and
            // the renaming of files or directories.
            watcher.NotifyFilter = NotifyFilters.LastAccess
                                    | NotifyFilters.LastWrite
                                    | NotifyFilters.FileName
                                    | NotifyFilters.DirectoryName;
            
            watcher.Filter = "*";

            // Add event handlers.
            watcher.Changed += OnChanged;
            watcher.Created += OnChanged;
            watcher.Deleted += OnChanged;
            watcher.Renamed += OnRenamed;

            // Begin watching.
            watcher.EnableRaisingEvents = true;

            _watchers.Add(watcher);
        }

        // Define the event handlers.
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            // Specify what is done when a file is changed, created, or deleted.
            Console.WriteLine($"{e.ChangeType} at {DateTime.Now.ToString("HH:mm:ss")}: {e.Name}");
            ChangesDetected = true;
        }

        private void OnRenamed(object source, RenamedEventArgs e)
        {
            // Specify what is done when a file is renamed.
            Console.WriteLine($"File: {e.OldFullPath} renamed to {e.FullPath}");
            ChangesDetected = true;
        }

        public void Dispose()
        {
            foreach (var watcher in _watchers)
            {
                watcher.Changed -= OnChanged;
                watcher.Created -= OnChanged;
                watcher.Deleted -= OnChanged;
                watcher.Renamed -= OnRenamed;
                watcher.Dispose();
            }
        }
    }
}