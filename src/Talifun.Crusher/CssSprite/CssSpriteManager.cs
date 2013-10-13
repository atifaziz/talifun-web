﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Web;
using Talifun.Crusher.Configuration;
using Talifun.Crusher.Configuration.Sprites;
using Talifun.Crusher.Crusher;
using Talifun.Web;
using Talifun.Web.Helper;

namespace Talifun.Crusher.CssSprite
{
    /// <summary>
    /// We only want one instance of this running. It has file watchers that look for changes to sprite component 
    /// images and will update the sprite image.
    /// </summary>
    public sealed class CssSpriteManager : IDisposable
    {
        private const int BufferSize = 32768;
		private readonly Encoding _encoding = Encoding.UTF8;
        private readonly CssSpriteGroupElementCollection _cssSpriteGroups = CurrentCrusherConfiguration.Current.CssSpriteGroups;
        private readonly ICssSpriteCreator _cssSpriteCreator;
        private readonly IPathProvider _pathProvider;
        private readonly IMetaData _cssSpriteMetaData;
    	
        private CssSpriteManager()
        {
            var retryableFileOpener = new RetryableFileOpener();
            var hasher = new Md5Hasher(retryableFileOpener);
			var retryableFileWriter = new RetryableFileWriter(BufferSize, _encoding, retryableFileOpener, hasher);

            var cacheManager = new HttpCacheManager();
			_pathProvider = new PathProvider();
            var cssSpriteMetaDataFileInfo = new FileInfo("cssSprite.metadata");
            _cssSpriteMetaData = new SingleFileMetaData(cssSpriteMetaDataFileInfo, retryableFileOpener, retryableFileWriter);
			_cssSpriteCreator = new CssSpriteCreator(cacheManager, retryableFileOpener, _pathProvider, retryableFileWriter, _cssSpriteMetaData);
            
            InitManager();
        }

        public static CssSpriteManager Instance
        {
            get
            {
                return Nested.instance;
            }
        }

        private class Nested
        {
            // Explicit static constructor to tell C# compiler
            // not to mark type as beforefieldinit
            static Nested()
            {
            }

            internal static readonly CssSpriteManager instance = new CssSpriteManager();
        }

        /// <summary>
        /// We want to release the manager when app domain is unloaded. So we removed the reference, as nothing will be referencing
        /// the manager, garbage collector will dispose it.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        /// <remarks>
        /// We are using a sneaky little trick to keep manager alive for the duration of the appdomain.
        /// We are storing a delegate with a reference to the manager in a global area (AppDomain.CurrentDomain.UnhandledException),
        /// which means the garbage collector won't be able to dispose the manager.
        /// HttpModule life is shorter then AppDomain and can be unloaded at any time.
        /// </remarks>
        private void OnDomainUnload(object sender, EventArgs e)
        {
            if (AppDomain.CurrentDomain != null)
            {
                AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
            }
        }

        private void InitManager()
        {
            AppDomain.CurrentDomain.DomainUnload += OnDomainUnload;

            foreach (CssSpriteGroupElement group in _cssSpriteGroups)
            {
                var files = new List<ImageFile>();
                var imageOutputPath = new FileInfo(new Uri(_pathProvider.MapPath(group.ImageOutputFilePath)).LocalPath);                
                var imageUrl = string.IsNullOrEmpty(group.ImageUrl)
                                   ? VirtualPathUtility.ToAbsolute(group.ImageOutputFilePath)
                                   : group.ImageUrl;
                var imageUri = new Uri(imageUrl, UriKind.RelativeOrAbsolute);
                var cssOutputPath = new FileInfo(new Uri(_pathProvider.MapPath(group.CssOutputFilePath)).LocalPath);

                foreach (ImageFileElement imageFile in group.Files)
                {
                    var file = new ImageFile()
                    {
                        FilePath = imageFile.FilePath,
                        Name = imageFile.Name
                    };
                    files.Add(file);
                }

                var directories = new List<ImageDirectory>();

                foreach (ImageDirectoryElement imageDirectory in group.Directories)
                {
                    var directory = new ImageDirectory()
                    {
                        DirectoryPath = imageDirectory.DirectoryPath,
                        ExcludeFilter = imageDirectory.ExcludeFilter,
                        IncludeFilter = imageDirectory.IncludeFilter,
                        IncludeSubDirectories = imageDirectory.IncludeSubDirectories,
                        PollTime = imageDirectory.PollTime
                    };

                    directories.Add(directory);
                }

                _cssSpriteCreator.AddFiles(imageOutputPath, imageUri, cssOutputPath, files, directories);
            }
        }

        private void DisposeManager()
        {
            foreach (CssSpriteGroupElement group in _cssSpriteGroups)
            {
                var imageUrl = string.IsNullOrEmpty(group.ImageUrl)
                                   ? VirtualPathUtility.ToAbsolute(group.ImageOutputFilePath)
                                   : group.ImageUrl;
                var imageUri = new Uri(imageUrl, UriKind.RelativeOrAbsolute);

                _cssSpriteCreator.RemoveFiles(imageUri);
            }

            if (AppDomain.CurrentDomain != null)
            {
                AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
            }
        }

        #region IDisposable Members
        private int alreadyDisposed = 0;

        ~CssSpriteManager()
        {
            // call Dispose with false.  Since we're in the
            // destructor call, the managed resources will be
            // disposed of anyways.
            Dispose(false);
        }

        void IDisposable.Dispose()
        {
            Dispose();
        }

        public void Dispose()
        {
            if (alreadyDisposed != 0) return;

            // dispose of the managed and unmanaged resources
            Dispose(true);

            // tell the GC that the Finalize process no longer needs
            // to be run for this object. 

            // it is called after Dispose(true) to ensure that GC.SuppressFinalize() 
            // only gets called if the Dispose operation completes successfully. 
            GC.SuppressFinalize(this);
        }

        private void Dispose(bool disposeManagedResources)
        {
            var disposedAlready = Interlocked.Exchange(ref alreadyDisposed, 1);
            if (disposedAlready != 0) return;

            if (!disposeManagedResources) return;

            // Dispose managed resources.
            DisposeManager();
        }

        #endregion
    }
}
