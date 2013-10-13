﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Talifun.Crusher.Configuration.Css;
using Talifun.Crusher.Crusher.CssModule;
using Talifun.Web;

namespace Talifun.Crusher.Crusher
{
    public class CssFileProcessor
    {
        private readonly ReaderWriterLockSlim _contentsLock = new ReaderWriterLockSlim();
        private readonly ReaderWriterLockSlim _assetsLock = new ReaderWriterLockSlim();
        private readonly IRetryableFileOpener _retryableFileOpener;
        private readonly IPathProvider _pathProvider;
        private readonly ICssPathRewriter _cssPathRewriter;
        private readonly FileInfo _fileInfo;
        private readonly Uri _cssRootUri;
        private readonly List<ICssModule> _modules;

        public CssFileProcessor(IRetryableFileOpener retryableFileOpener, IPathProvider pathProvider, ICssPathRewriter cssPathRewriter, string filePath, CssCompressionType compressionType, Uri cssRootUri, bool appendHashToAssets)
        {
            _retryableFileOpener = retryableFileOpener;
            _pathProvider = pathProvider;
            _cssPathRewriter = cssPathRewriter;
            CompressionType = compressionType;

            _fileInfo = new FileInfo(new Uri(_pathProvider.MapPath(filePath)).LocalPath);
            _cssRootUri = cssRootUri;
            var absoluteUriDirectory = _pathProvider.GetAbsoluteUriDirectory(filePath);

            _modules = new List<ICssModule>()
            {
                new DotLessModule(),
                new RelativePathModule(absoluteUriDirectory, cssPathRewriter),
                new CssAssetsHashModule(appendHashToAssets, cssPathRewriter, pathProvider)
            };
        }

        public CssCompressionType CompressionType { get; protected set; }

        private string _contents;
        /// <summary>
        /// Get the processed css of a file.
        /// 
        /// Files with extension .less or .less.css will be generated with dotless.
        /// If appentHashToAssets = true, hashes of local existing files will be appending to processed css.
        /// </summary>
        /// <returns>The processed contents of a file.</returns>
        public string GetContents()
        {
            _contentsLock.EnterUpgradeableReadLock();

            try
            {
                if (_contents == null)
                {
                    _contentsLock.EnterWriteLock();
                    try
                    {
                        if (_contents == null)
                        {
                            _contents = _retryableFileOpener.ReadAllText(_fileInfo);

                            foreach (var module in _modules)
                            {
                                _contents = module.Process(_cssRootUri, _fileInfo, _contents);
                            }
                        }
                    }
                    finally
                    {
                        _contentsLock.ExitWriteLock();
                    }
                }

                return _contents;
            }
            finally
            {
                _contentsLock.ExitUpgradeableReadLock();
            }
        }

        private IEnumerable<CssAsset> _localCssAssetFilesThatExist;

        /// <summary>
        /// Gets local css assets that exist.
        /// </summary>
        /// <returns>A list of FileInfo of css assets that exist.</returns>
        public IEnumerable<CssAsset> GetLocalCssAssetFilesThatExist()
        {
            _assetsLock.EnterUpgradeableReadLock();

            try
            {
                if (_localCssAssetFilesThatExist == null)
                {
                    _assetsLock.EnterWriteLock();
                    try
                    {
                        if (_localCssAssetFilesThatExist == null)
                        {
                            var contents = GetContents();
                            var cssAbsoluteUriDirectory = _pathProvider.GetAbsoluteUriDirectory(_cssRootUri);
                            _localCssAssetFilesThatExist = GetCssAssets(cssAbsoluteUriDirectory, contents);
                        }
                    }
                    finally
                    {
                        _assetsLock.ExitWriteLock();
                    }
                }

                return _localCssAssetFilesThatExist;
            }
            finally
            {
                _assetsLock.ExitUpgradeableReadLock();
            }
        }

        private IEnumerable<CssAsset> GetCssAssets(Uri cssAbsoluteUriDirectory, string fileContents)
        {
            var distinctLocalPaths = _cssPathRewriter.FindDistinctLocalPaths(fileContents);

            return distinctLocalPaths
                .Select(distinctLocalPath => new CssAsset
                {
                    File = new FileInfo(new Uri(_pathProvider.MapPath(cssAbsoluteUriDirectory, distinctLocalPath)).LocalPath),
                    Url = distinctLocalPath
                })
                .Where(cssAssetFileInfo => cssAssetFileInfo.File.Exists);
        }

    }
}
