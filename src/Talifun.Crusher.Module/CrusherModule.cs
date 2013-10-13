﻿using Talifun.Crusher.Crusher;
using Talifun.Web.Module;

namespace Talifun.Crusher.Module
{
    /// <summary>
    /// Module that is used to crush css and js based on a configuration provided.
    /// </summary>
    public class CrusherModule : HttpModuleBase
    {
        /// <summary>
        /// We want to initialize the crusher manager.
        /// </summary>
        protected static readonly CrusherManager CrusherManager = CrusherManager.Instance;

        /// <summary>
        /// Determines whether the module will be registered for discovery
        /// in partial trust environments or not.
        /// </summary>
        protected override bool SupportDiscoverability
        {
            get { return true; }
        }
    }
}