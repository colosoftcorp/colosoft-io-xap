using System;
using System.Collections.Generic;

namespace Colosoft.IO.Xap
{
    public class ConfigAppManifestTemplate : IAppManifestTemplate
    {
        private readonly string template;

        public ConfigAppManifestTemplate(string templateFileName)
        {
            this.template = templateFileName;
        }

        public System.Xml.XmlDocument Generate(IEnumerable<Uri> assemblySources)
        {
            if (assemblySources is null)
            {
                throw new ArgumentNullException(nameof(assemblySources));
            }

            var doc = new System.Xml.XmlDocument();
            doc.LoadXml(this.template);
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

#if !NETSTANDARD2_0 && !NETCOREAPP2_0
    public class AppManifestSection : System.Configuration.IConfigurationSectionHandler
    {
        public object Create(object parent, object configContext, System.Xml.XmlNode section)
        {
            if (((System.Xml.XmlElement)section).GetElementsByTagName("Deployment.Parts").Count != 1)
                throw new Exception("appManifestTemplate section requires exactly one Deployment.Parts element");
            
            return new ConfigAppManifestTemplate(section.InnerXml);
        }
    }
#endif

}
