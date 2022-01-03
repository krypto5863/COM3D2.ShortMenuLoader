using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace ShortMenuLoader
{
	//Should be noted that FileSystemWatchers (what powers LiveDirectory) does not support SymLinks or junctions :(
	internal class LiveDirectory : IDisposable
	{
		public List<string> FilesInDirectory { get; private set; }

		private FileSystemWatcher Watcher;

		public LiveDirectory(string path)
		{
			FilesInDirectory = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories).ToList();

			Watcher = new FileSystemWatcher(path);

			Watcher.NotifyFilter = NotifyFilters.CreationTime
								 | NotifyFilters.DirectoryName
								 | NotifyFilters.FileName
								 | NotifyFilters.LastWrite
								 | NotifyFilters.Size;

			Watcher.IncludeSubdirectories = true;
			Watcher.EnableRaisingEvents = true;

			Watcher.Created += DirectoryChanged;
			Watcher.Deleted += DirectoryChanged;
			Watcher.Renamed += DirectoryChanged;
		}
		public void DirectoryChanged(object sender, EventArgs e)
		{
			if (e is RenamedEventArgs args)
			{
				FilesInDirectory.Remove(args.OldFullPath);
				FilesInDirectory.Add(args.FullPath);
			}
			else if (e is FileSystemEventArgs args1)
			{
				if (args1.ChangeType == WatcherChangeTypes.Created)
				{
					FilesInDirectory.Add(args1.FullPath);
				} 
				else if (args1.ChangeType == WatcherChangeTypes.Deleted) 
				{
					FilesInDirectory.Remove(args1.FullPath);
				} 
			}
		}

		~LiveDirectory()
		{
			Dispose();
		}
		public void Dispose()
		{
			Watcher.Dispose();
			Watcher = null;
			FilesInDirectory = null;
		}
	}
}
