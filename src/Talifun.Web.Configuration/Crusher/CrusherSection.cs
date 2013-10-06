﻿using System.Configuration;
using System.Diagnostics.CodeAnalysis;

namespace Talifun.Web.Crusher.Config
{
    /// <summary>
    /// The configuration root for the crusher module
    /// </summary>
    public sealed class CrusherSection : ConfigurationSection
    {
        private static ConfigurationPropertyCollection properties = new ConfigurationPropertyCollection();
        private static readonly ConfigurationProperty xmlns = new ConfigurationProperty("xmlns", typeof(string), "", ConfigurationPropertyOptions.None);
        private static readonly ConfigurationProperty cssGroups = new ConfigurationProperty("cssGroups", typeof(CssGroupElementCollection), null, ConfigurationPropertyOptions.IsRequired);
        private static readonly ConfigurationProperty jsGroups = new ConfigurationProperty("jsGroups", typeof(JsGroupElementCollection), null, ConfigurationPropertyOptions.IsRequired);
        private static readonly ConfigurationProperty querystringKeyName = new ConfigurationProperty("querystringKeyName", typeof(string), "etag", ConfigurationPropertyOptions.None);
        private static readonly ConfigurationProperty watchAssets = new ConfigurationProperty("watchAssets", typeof(bool), false, ConfigurationPropertyOptions.None);

        /// <summary>
        /// Perform static initialisation for this configuration section. This includes explicitly adding
        /// configured properties to the Properties collection, and so cannot be performed inline.
        /// </summary>
        [SuppressMessage("Microsoft.Performance", "CA1810:InitializeReferenceTypeStaticFieldsInline")]
        static CrusherSection()
        {
            properties.Add(xmlns);
            properties.Add(cssGroups);
            properties.Add(jsGroups);
            properties.Add(querystringKeyName);
            properties.Add(watchAssets);
        }

        /// <summary>
        /// Gets a <see cref="CssGroupElementCollection" /> containing the configuration elements associated with this configuration section.
        /// </summary>
        /// <value>A <see cref="CssGroupElementCollection" /> containing the configuration elements associated with this configuration section.</value>
        [ConfigurationProperty("cssGroups", DefaultValue = null, IsRequired = true)]
        public CssGroupElementCollection CssGroups
        {
            get { return ((CssGroupElementCollection)base[cssGroups]); }
        }

        /// <summary>
        /// Gets a <see cref="JsGroupElementCollection" /> containing the configuration elements associated with this configuration section.
        /// </summary>
        /// <value>A <see cref="JsGroupElementCollection" /> containing the configuration elements associated with this configuration section.</value>
        [ConfigurationProperty("jsGroups", DefaultValue = null, IsRequired = true)]
        public JsGroupElementCollection JsGroups
        {
            get { return ((JsGroupElementCollection)base[jsGroups]); }
        }

        /// <summary>
        /// The name of the query string key that is appended for the hash of the file.
        /// </summary>
        [ConfigurationProperty("querystringKeyName", DefaultValue = "etag", IsRequired = false)]
        public string QuerystringKeyName
        {
            get { return ((string)base[querystringKeyName]); }
            set { base[querystringKeyName] = value; }
        }

        /// <summary>
        /// Should changes to assets trigger regeneration of files.
        /// </summary>
        [ConfigurationProperty("watchAssets", DefaultValue = false, IsRequired = false)]
        public bool WatchAssets
        {
            get { return ((bool)base[watchAssets]); }
            set { base[watchAssets] = value; }
        }

        /// <summary>
        /// Gets the collection of configuration properties contained by this configuration element.
        /// </summary>
        /// <returns>The <see cref="T:System.Configuration.ConfigurationPropertyCollection"></see> collection of properties for the element.</returns>
        protected override ConfigurationPropertyCollection Properties
        {
            get
            {
                return properties;
            }
        }
    }
}