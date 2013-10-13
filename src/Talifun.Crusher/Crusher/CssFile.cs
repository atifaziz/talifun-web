﻿using Talifun.Crusher.Configuration.Css;

namespace Talifun.Crusher.Crusher
{
    public class CssFile
    {
        /// <summary>
        /// The file path where the css file will be created.
        /// </summary>
        public virtual string FilePath { get; set; }

        /// <summary>
        /// Compression type to use on css file
        /// </summary>
        public virtual CssCompressionType CompressionType { get; set; }
    }
}