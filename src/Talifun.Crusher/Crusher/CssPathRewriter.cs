﻿using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace Talifun.Crusher.Crusher
{
    public class CssPathRewriter : ICssPathRewriter
    {
        protected readonly ICssAssetsFileHasher CssAssetsFileHasher;
        protected readonly IPathProvider PathProvider;
        public CssPathRewriter(ICssAssetsFileHasher cssAssetsFileHasher, IPathProvider pathProvider)
        {
            CssAssetsFileHasher = cssAssetsFileHasher;
            PathProvider = pathProvider;
        }

        public virtual string RewriteCssPathsToBeRelativeToPath(IEnumerable<Uri> relativePaths, Uri cssRootUri, Uri absoluteUriDirectory, string css)
        {
            if (!cssRootUri.IsAbsoluteUri)
            {
                cssRootUri = new Uri(PathProvider.MapPath(cssRootUri));
            }

            if (!absoluteUriDirectory.IsAbsoluteUri)
            {
                absoluteUriDirectory = new Uri(PathProvider.MapPath(absoluteUriDirectory));
            }

            foreach (var relativePath in relativePaths)
            {
                var absoluteUri = relativePath.IsAbsoluteUri
                                      ? relativePath
                                      : new Uri(PathProvider.MapPath(absoluteUriDirectory, relativePath));

                var resolvedOutput = cssRootUri.MakeRelativeUri(absoluteUri);

                if (relativePath.OriginalString == resolvedOutput.OriginalString) continue;

                css = css.Replace(relativePath.OriginalString, resolvedOutput.OriginalString);
            }

            return css;
        }

        private static readonly Regex FindDistinctRelativePathsRegex = new Regex(@"url\([""']{0,1}(.+?)[""']{0,1}\)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        public virtual IEnumerable<Uri> FindDistinctRelativePaths(string css)
        {
            var matches = FindDistinctRelativePathsRegex.Matches(css);
            var matchesHash = new HashSet<Uri>();
            
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Captures[0].Value;
                if (path.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) || path.StartsWith("/"))
                    continue;

                var uri = new Uri(path, UriKind.RelativeOrAbsolute);
                matchesHash.Add(uri);
            }

            return matchesHash;
        }

        public virtual string RewriteCssPathsToAppendHash(IEnumerable<Uri> localPaths, Uri cssRootUri, string css)
        {
            if (!cssRootUri.IsAbsoluteUri)
            {
                cssRootUri = new Uri(PathProvider.MapPath(cssRootUri));
            }

            foreach (var localPath in localPaths)
            {
                var localRelativePathThatExistWithFileHash = CssAssetsFileHasher.AppendFileHash(cssRootUri, localPath);

                if (localPath != localRelativePathThatExistWithFileHash)
                {
                    css = css.Replace(localPath.OriginalString, localRelativePathThatExistWithFileHash.OriginalString);
                }
            }

            return css;
        }

        public virtual IEnumerable<Uri> FindDistinctLocalPaths(string css)
        {
            var matches = FindDistinctRelativePathsRegex.Matches(css);
            var matchesHash = new HashSet<Uri>();
            foreach (Match match in matches)
            {
                var path = match.Groups[1].Captures[0].Value;
                if (path.StartsWith("http", StringComparison.InvariantCultureIgnoreCase) ||
                    path.StartsWith("data", StringComparison.InvariantCultureIgnoreCase))
                        continue;

                var uri = new Uri(path, UriKind.RelativeOrAbsolute);
                matchesHash.Add(uri);
            }

            return matchesHash;
        }
    }
}
