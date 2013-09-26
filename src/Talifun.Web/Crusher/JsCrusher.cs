﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Caching;
using Talifun.FileWatcher;
using Talifun.Web.Helper;
using Talifun.Web.Helper.Pooling;

namespace Talifun.Web.Crusher
{
    /// <summary>
    /// Manages the adding and removing of js files to crush. It also does the js crushing.
    /// </summary>
    public class JsCrusher : IJsCrusher
    {
        protected readonly ICacheManager CacheManager;
        protected readonly IPathProvider PathProvider;
        protected readonly IRetryableFileOpener RetryableFileOpener;
        protected readonly IRetryableFileWriter RetryableFileWriter;
        protected readonly IMetaData FileMetaData;

        protected readonly Pool<Yahoo.Yui.Compressor.JavaScriptCompressor> YahooYuiJavaScriptCompressorPool;
        protected readonly Pool<Microsoft.Ajax.Utilities.Minifier> MicrosoftAjaxMinJavaScriptCompressorPool;

        protected readonly Pool<Coffee.CoffeeCompiler> CoffeeCompilerPool;
        protected readonly Pool<IcedCoffee.IcedCoffeeCompiler> IcedCoffeeCompilerPool;
        protected readonly Pool<LiveScript.LiveScriptCompiler> LiveScriptCompilerPool;
        protected readonly Pool<Hogan.HoganCompiler> HoganCompilerPool;
        
        protected static string JsCrusherType = typeof(JsCrusher).ToString();

        public JsCrusher(ICacheManager cacheManager, IPathProvider pathProvider, IRetryableFileOpener retryableFileOpener, IRetryableFileWriter retryableFileWriter, IMetaData fileMetaData)
        {
            CacheManager = cacheManager;
            PathProvider = pathProvider;
            RetryableFileOpener = retryableFileOpener;
            RetryableFileWriter = retryableFileWriter;
            FileMetaData = fileMetaData;
            YahooYuiJavaScriptCompressorPool = new Pool<Yahoo.Yui.Compressor.JavaScriptCompressor>(64, pool => new Yahoo.Yui.Compressor.JavaScriptCompressor(), LoadingMode.LazyExpanding, AccessMode.Circular);
            MicrosoftAjaxMinJavaScriptCompressorPool = new Pool<Microsoft.Ajax.Utilities.Minifier>(64, pool => new Microsoft.Ajax.Utilities.Minifier(), LoadingMode.LazyExpanding, AccessMode.Circular);
            CoffeeCompilerPool = new Pool<Coffee.CoffeeCompiler>(4, pool => new Coffee.CoffeeCompiler(new EmbeddedResourceLoader()), LoadingMode.LazyExpanding, AccessMode.Circular);
            IcedCoffeeCompilerPool = new Pool<IcedCoffee.IcedCoffeeCompiler>(4, pool => new IcedCoffee.IcedCoffeeCompiler(new EmbeddedResourceLoader()), LoadingMode.LazyExpanding, AccessMode.Circular);
            LiveScriptCompilerPool = new Pool<LiveScript.LiveScriptCompiler>(4, pool => new LiveScript.LiveScriptCompiler(new EmbeddedResourceLoader()), LoadingMode.LazyExpanding, AccessMode.Circular);
            HoganCompilerPool = new Pool<Hogan.HoganCompiler>(4, pool => new Hogan.HoganCompiler(new EmbeddedResourceLoader()), LoadingMode.LazyExpanding, AccessMode.Circular);
        }

    	/// <summary>
    	/// Add js files to be crushed.
    	/// </summary>
    	/// <param name="outputUri">The virtual path for the crushed js file.</param>
    	/// <param name="files">The js files to be crushed.</param>
    	/// <param name="directories">The js directories to be crushed. </param>
    	public virtual JsCrushedOutput AddGroup(Uri outputUri, IEnumerable<JsFile> files, IEnumerable<JsDirectory> directories)
        {
            var outputFileInfo = new FileInfo(new Uri(PathProvider.MapPath(outputUri)).LocalPath);
			var crushedContent = ProcessGroup(outputFileInfo, outputUri, files, directories);            
            AddGroupToCache(outputUri, crushedContent.FilesToWatch, files, crushedContent.FoldersToWatch, directories);
    	    return crushedContent;
        }

        /// <summary>
        /// Get all files and files in directories that are going to be crushed.
        /// </summary>
        /// <param name="files"></param>
        /// <param name="directories"></param>
        /// <returns></returns>
		private IEnumerable<JsFileToWatch> GetFilesToWatch(IEnumerable<JsFile> files, IEnumerable<JsDirectory> directories)
		{
            var filesToWatch = files.Select(x => new JsFileToWatch()
            {
                CompressionType = x.CompressionType,
                FilePath = x.FilePath
            });

            var filesInDirectoriesToWatch = directories
                .SelectMany(x => new DirectoryInfo(new Uri(PathProvider.MapPath(x.DirectoryPath)).LocalPath)
                    .GetFiles("*", SearchOption.AllDirectories)
                    .Where(y => (string.IsNullOrEmpty(x.IncludeFilter) || Regex.IsMatch(y.Name, x.IncludeFilter, RegexOptions.Compiled | RegexOptions.IgnoreCase))
                    && (string.IsNullOrEmpty(x.ExcludeFilter) || !Regex.IsMatch(y.Name, x.ExcludeFilter, RegexOptions.Compiled | RegexOptions.IgnoreCase)))
                    .Select(y => new JsFileToWatch()
                    {
                        CompressionType = x.CompressionType,
                        FilePath = PathProvider.ToRelative(y.FullName).ToString()
                    }));

            return filesInDirectoriesToWatch.Concat(filesToWatch).Distinct(new JsFileToWatchEqualityComparer());
		}

        /// <summary>
        /// Compress the js files and store them in the specified js file.
        /// </summary>
        /// <param name="outputFileInfo">The output path for the crushed js file.</param>
        /// <param name="jsRootUri"></param>
        /// <param name="files">The js files to be crushed.</param>
        /// <param name="directories"> </param>
        public virtual JsCrushedOutput ProcessGroup(FileInfo outputFileInfo, Uri jsRootUri, IEnumerable<JsFile> files, IEnumerable<JsDirectory> directories)
        {
    		var filesToWatch = GetFilesToWatch(files, directories);

            var metaDataFiles = filesToWatch.Select(x => new FileInfo(x.FilePath)).Distinct().OrderBy(x => x.FullName);

            var isMetaDataFresh = FileMetaData.IsMetaDataFresh(outputFileInfo, metaDataFiles);

    	    if (!isMetaDataFresh)
    	    {
                var filesToProcess = filesToWatch
                    .Select(jsFile => new JsFileProcessor(CoffeeCompilerPool, 
                        IcedCoffeeCompilerPool, 
                        LiveScriptCompilerPool, 
                        HoganCompilerPool, 
                        RetryableFileOpener, PathProvider, jsFile.FilePath, jsFile.CompressionType, jsRootUri));

                var content = GetGroupContent(filesToProcess);

    	        RetryableFileWriter.SaveContentsToFile(content, outputFileInfo);

                FileMetaData.CreateMetaData(outputFileInfo, metaDataFiles);
    	    }

            var foldersToWatch = directories
                .Select(x => Talifun.FileWatcher.EnhancedFileSystemWatcherFactory.Instance
                .CreateEnhancedFileSystemWatcher(new Uri(PathProvider.MapPath(x.DirectoryPath)).LocalPath, x.IncludeFilter, x.ExcludeFilter, x.PollTime, x.IncludeSubDirectories));

            var crushedOutput = new JsCrushedOutput
            {
                FoldersToWatch = foldersToWatch,
				FilesToWatch = filesToWatch
            };

            return crushedOutput;
        }

        private StringBuilder GetGroupContent(IEnumerable<JsFileProcessor> filesToProcess)
        {
            var uncompressedContents = new StringBuilder();
            var yahooYuiToBeCompressedContents = new StringBuilder();
            var microsoftAjaxMinToBeCompressedContents = new StringBuilder();

            foreach (var fileToProcess in filesToProcess)
            {
                switch (fileToProcess.CompressionType)
                {
                    case JsCompressionType.None:
                        uncompressedContents.AppendLine(fileToProcess.GetContents());
                        break;
                    case JsCompressionType.YahooYui:
                        yahooYuiToBeCompressedContents.AppendLine(fileToProcess.GetContents());
                        break;
                    case JsCompressionType.MicrosoftAjaxMin:
                        microsoftAjaxMinToBeCompressedContents.AppendLine(fileToProcess.GetContents());
                        break;
                }
            }

            if (yahooYuiToBeCompressedContents.Length > 0)
            {
                var yahooYuiJavaScriptCompressor = YahooYuiJavaScriptCompressorPool.Acquire();
                try
                {
                    uncompressedContents.Append(yahooYuiJavaScriptCompressor.Compress(yahooYuiToBeCompressedContents.ToString()));
                }
                finally
                {
                    YahooYuiJavaScriptCompressorPool.Release(yahooYuiJavaScriptCompressor);
                }
            }

            if (microsoftAjaxMinToBeCompressedContents.Length > 0)
            {
                var microsoftAjaxMinJavaScriptCompressor = MicrosoftAjaxMinJavaScriptCompressorPool.Acquire();
                try
                {
                    uncompressedContents.Append(microsoftAjaxMinJavaScriptCompressor.MinifyJavaScript(microsoftAjaxMinToBeCompressedContents.ToString()));
                }
                finally
                {
                    MicrosoftAjaxMinJavaScriptCompressorPool.Release(microsoftAjaxMinJavaScriptCompressor);
                }
            }
            return uncompressedContents;
        }

        /// <summary>
        /// Remove all js files from being crushed
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file</param>
        public virtual void RemoveGroup(Uri outputUri)
        {
            CacheManager.Remove<JsCacheItem>(GetKey(outputUri));
        }

        /// <summary>
        /// Add the js files to the cache so that they are monitored for any changes.
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file.</param>
        /// <param name="filesToWatch">Files that are crushed.</param>
        /// <param name="files">The js files to be crushed.</param>
        /// <param name="foldersToWatch"> </param>
        /// <param name="directories">The js directories to be crushed.</param>
        public virtual void AddGroupToCache(Uri outputUri, IEnumerable<JsFileToWatch> filesToWatch, IEnumerable<JsFile> files, IEnumerable<Talifun.FileWatcher.IEnhancedFileSystemWatcher> foldersToWatch, IEnumerable<JsDirectory> directories)
        {
            var fileNames = new List<string>
            {
                    PathProvider.MapPath(outputUri)
            };

			fileNames.AddRange(filesToWatch.Select(file => new Uri(PathProvider.MapPath(file.FilePath)).LocalPath));

            var cacheItem = new JsCacheItem()
            {
                OutputUri = outputUri,
				FilesToWatch = filesToWatch,
                Files = files,
                FoldersToWatch = foldersToWatch,
				Directories = directories
            };

            CacheManager.Insert(
                GetKey(outputUri),
                cacheItem,
                new CacheDependency(fileNames.ToArray(), System.DateTime.Now),
                Cache.NoAbsoluteExpiration,
                Cache.NoSlidingExpiration,
                CacheItemPriority.High,
                FileRemoved);

            foreach (var enhancedFileSystemWatcher in foldersToWatch)
            {
                enhancedFileSystemWatcher.FileActivityFinishedEvent += OnFileActivityFinishedEvent;
                enhancedFileSystemWatcher.UserState = outputUri;
                enhancedFileSystemWatcher.Start();
            }
        }

        /// <summary>
        /// When file events have finished it means we should should remove them from the cache
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        protected void OnFileActivityFinishedEvent(object sender, FileWatcher.FileActivityFinishedEventArgs e)
        {
            var outputUri = (Uri)e.UserState;

            if (e.FileEventItems.Any(x => x.FileEventType != FileEventType.InDirectory))
            {
                RemoveGroup(outputUri);
            }
        }

        /// <summary>
        /// When a file is removed from cache, keep it in the cache if it is unused or expired as we want to continue to monitor
        /// any changes to file. If it has been removed because the file has changed then regenerate the crushed js file and
        /// start the monitoring again.
        /// </summary>
        /// <param name="key">The key of the cache item</param>
        /// <param name="value">The value of the cache item</param>
        /// <param name="reason">The reason the file was removed from cache.</param>
        public virtual void FileRemoved(string key, object value, CacheItemRemovedReason reason)
        {
            var cacheItem = (JsCacheItem)value;
            foreach (var enhancedFileSystemWatcher in cacheItem.FoldersToWatch)
            {
                enhancedFileSystemWatcher.Stop();
                enhancedFileSystemWatcher.FileActivityFinishedEvent -= OnFileActivityFinishedEvent;
            }
            switch (reason)
            {
                case CacheItemRemovedReason.DependencyChanged:
                    AddGroup(cacheItem.OutputUri, cacheItem.Files, cacheItem.Directories);
                    break;
                case CacheItemRemovedReason.Underused:
                case CacheItemRemovedReason.Expired:
                    AddGroupToCache(cacheItem.OutputUri, cacheItem.FilesToWatch, cacheItem.Files, cacheItem.FoldersToWatch, cacheItem.Directories);
                    break;
            }
        }

        /// <summary>
        /// Get the cache key to use for caching.
        /// </summary>
        /// <param name="outputUri">The path for the crushed js file.</param>
        /// <returns>The cache key to use for caching.</returns>
        public virtual string GetKey(Uri outputUri)
        {
            var prefix = JsCrusherType + "|";
            return prefix + outputUri;
        }
    }
}