﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using Talifun.Web.Crusher.Config;
using Talifun.Web.Helper;

namespace Talifun.Web.Crusher
{
    /// <summary>
    /// We only want one instance of this running. It has file watchers that look for changes to js and css rules
    /// specified and will update them as neccessary.
    /// </summary>
    public sealed class CrusherManager : IDisposable
    {
        private const int BufferSize = 32768;
		private readonly Encoding _encoding = Encoding.UTF8;
        private readonly string _hashQueryStringKeyName;
        private readonly CssGroupElementCollection _cssGroups;
        private readonly JsGroupElementCollection _jsGroups;
        private readonly IPathProvider _pathProvider;
        private readonly ICssCrusher _cssCrusher;
        private readonly IJsCrusher _jsCrusher;

        private CrusherManager()
        {
            var crusherConfiguration = CurrentCrusherConfiguration.Current;
            _hashQueryStringKeyName = crusherConfiguration.QuerystringKeyName;
            _cssGroups = crusherConfiguration.CssGroups;
            _jsGroups = crusherConfiguration.JsGroups;

            var retryableFileOpener = new RetryableFileOpener();
            var hasher = new Md5Hasher(retryableFileOpener);
			var retryableFileWriter = new RetryableFileWriter(BufferSize, _encoding, retryableFileOpener, hasher);
            _pathProvider = new PathProvider();
            var cssAssetsFileHasher = new CssAssetsFileHasher(_hashQueryStringKeyName, hasher, _pathProvider);
            var cssPathRewriter = new CssPathRewriter(cssAssetsFileHasher, _pathProvider);

            var cacheManager = new HttpCacheManager();

            var jsSpriteMetaDataFileInfo = new FileInfo("js.metadata");
            var jsMetaData = new SingleFileMetaData(jsSpriteMetaDataFileInfo, retryableFileOpener, retryableFileWriter);

            var cssSpriteMetaDataFileInfo = new FileInfo("css.metadata");
            var cssMetaData = new SingleFileMetaData(cssSpriteMetaDataFileInfo, retryableFileOpener, retryableFileWriter);

            _cssCrusher = new CssCrusher(cacheManager, _pathProvider, retryableFileOpener, retryableFileWriter, cssPathRewriter, cssMetaData, crusherConfiguration.WatchAssets);
            _jsCrusher = new JsCrusher(cacheManager, _pathProvider, retryableFileOpener, retryableFileWriter, jsMetaData);

            InitManager();
        }

        public static CrusherManager Instance
        {
            get
            {
                return Nested.Instance;
            }
        }

        private class Nested
        {
            // Explicit static constructor to tell C# compiler
            // not to mark type as beforefieldinit
            static Nested()
            {
            }

            internal static readonly CrusherManager Instance = new CrusherManager();
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

            var jsExceptions = new List<JsException>(); 
            var cssExceptions = new List<CssException>();
            var countdownEvents = new CountdownEvent(2);

            ThreadPool.QueueUserWorkItem(data =>
                {
                    var manualResetEvent = (CountdownEvent)data;
                    try
                    {
                        var groupsProcessor = new JsGroupsProcessor();
                        groupsProcessor.ProcessGroups(_pathProvider, _jsCrusher, _jsGroups);
                    }
                    catch (Exception exception)
                    {
                        jsExceptions.Add(new JsException(exception));
                    }
                    manualResetEvent.Signal();

                }, countdownEvents);

            ThreadPool.QueueUserWorkItem(data =>
                {
                    var manualResetEvent = (CountdownEvent)data;
                    try
                    {
                        var groupsProcessor = new CssGroupsProcessor();
                        groupsProcessor.ProcessGroups(_pathProvider, _cssCrusher, _cssGroups);
                    }
                    catch (Exception exception)
                    {
                        cssExceptions.Add(new CssException(exception));
                    }
                    manualResetEvent.Signal();
                }, countdownEvents);

            countdownEvents.Wait();

            var exceptions = cssExceptions.Cast<Exception>().Concat(jsExceptions.Cast<Exception>()).ToList();

            if (exceptions.Any())
            {
                throw new AggregateException(exceptions);
            }
        }

        private void DisposeManager()
        {
            foreach (CssGroupElement group in _cssGroups)
            {
                var outputUri = new Uri(_pathProvider.ToAbsolute(group.OutputFilePath), UriKind.Relative);
                _cssCrusher.RemoveGroup(outputUri);
            }

            foreach (JsGroupElement group in _jsGroups)
            {
                var outputUri = new Uri(_pathProvider.ToAbsolute(group.OutputFilePath), UriKind.Relative);
                _jsCrusher.RemoveGroup(outputUri);
            }

            if (AppDomain.CurrentDomain != null)
            {
                AppDomain.CurrentDomain.DomainUnload -= OnDomainUnload;
            }
        }
       
        #region IDisposable Members
        private int alreadyDisposed = 0;

        ~CrusherManager()
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