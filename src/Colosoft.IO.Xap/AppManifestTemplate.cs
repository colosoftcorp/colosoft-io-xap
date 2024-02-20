using System;
using System.Collections.Generic;

namespace Colosoft.IO.Xap
{
    public class AppManifestTemplate : IAppManifestTemplate
    {
        public System.Xml.XmlDocument Generate(IEnumerable<Uri> assemblySources)
        {
            if (assemblySources is null)
            {
                throw new ArgumentNullException(nameof(assemblySources));
            }

            var doc = new System.Xml.XmlDocument();

            using (var stream = typeof(AppManifestTemplate).Assembly.GetManifestResourceStream("Colosoft.IO.Xap.AppManifest.xaml"))
            {
                doc.Load(stream);
                var target = (System.Xml.XmlElement)doc.GetElementsByTagName("Deployment.Parts")[0];

                foreach (Uri source in assemblySources)
                {
                    var ap = doc.CreateElement("AssemblyPart", target.NamespaceURI);
                    string src = source.ToString();
                    ap.SetAttribute("Source", src);
                    target.AppendChild(ap);
                }

                return doc;
            }
        }
    }
}
