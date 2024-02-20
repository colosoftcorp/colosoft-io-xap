using System.Collections.Generic;

namespace Colosoft.IO.Xap
{
    public interface IXapConfiguration
    {
        IAppManifestTemplate ManifestTemplate { get; }

        Dictionary<string, LanguageInfo> Languages { get; }

#pragma warning disable CA1056 // URI-like properties should not be strings
        string UrlPrefix { get; }
#pragma warning restore CA1056 // URI-like properties should not be strings
    }
}
