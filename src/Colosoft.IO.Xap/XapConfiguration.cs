using System;
using System.Collections.Generic;

namespace Colosoft.IO.Xap
{
    public class XapConfiguration : IXapConfiguration
    {
        public IAppManifestTemplate ManifestTemplate { get; }

        public Dictionary<string, LanguageInfo> Languages { get; }

        public string UrlPrefix { get; }

#pragma warning disable CA1054 // URI-like parameters should not be strings
        public XapConfiguration(IAppManifestTemplate manifestTemplate, IEnumerable<LanguageInfo> languages, string urlPrefix)
#pragma warning restore CA1054 // URI-like parameters should not be strings
        {
            if (languages is null)
            {
                throw new ArgumentNullException(nameof(languages));
            }

            this.ManifestTemplate = manifestTemplate ?? throw new System.ArgumentNullException(nameof(manifestTemplate));

            this.Languages = new Dictionary<string, LanguageInfo>();
            foreach (var i in languages)
            {
                foreach (var ext in i.Extensions)
                {
                    this.Languages.Add(ext, i);
                }
            }

            this.UrlPrefix = urlPrefix;
        }
    }
}
