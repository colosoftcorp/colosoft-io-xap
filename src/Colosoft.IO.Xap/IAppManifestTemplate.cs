using System;

namespace Colosoft.IO.Xap
{
    public interface IAppManifestTemplate
    {
        System.Xml.XmlDocument Generate(System.Collections.Generic.IEnumerable<Uri> assemblySources);
    }
}
