using System.Text;

namespace Colosoft.IO.Xap
{
    public class LanguageInfo
    {
#pragma warning disable CA1819 // Properties should not return arrays
        public string[] Extensions { get; private set; }

        public string[] Assemblies { get; private set; }
#pragma warning restore CA1819 // Properties should not return arrays

        public string LanguageContext { get; private set; }

        public LanguageInfo(string[] extensions, string[] assemblies, string languageContext)
        {
            this.Extensions = extensions;
            this.Assemblies = assemblies;
            this.LanguageContext = languageContext;
        }

        public string GetContextAssemblyName()
        {
            return System.Reflection.AssemblyName.GetAssemblyName(this.Assemblies[0]).FullName;
        }

        public string GetExtensionsString()
        {
            StringBuilder str = new StringBuilder();
            foreach (string ext in this.Extensions)
            {
                if (str.Length > 0)
                {
                    str.Append(',');
                }

                str.Append(ext + ",." + ext);
            }

            return str.ToString();
        }
    }

#if !NETSTANDARD2_0 && !NETCOREAPP2_0
    public class LanguageSection : System.Configuration.IConfigurationSectionHandler
    {
        public object Create(object parent, object configContext, System.Xml.XmlNode section)
        {
            Dictionary<string, LanguageInfo> languages = new Dictionary<string, LanguageInfo>();
            char[] splitChars = new char[] { ' ', '\t', ',', ';', '\r', '\n' };

            foreach (System.Xml.XmlElement elem in ((System.Xml.XmlElement)section).GetElementsByTagName("Language"))
            {
                LanguageInfo info = new LanguageInfo(
                    elem.GetAttribute("extensions").Split(splitChars, StringSplitOptions.RemoveEmptyEntries),
                    elem.GetAttribute("assemblies").Split(splitChars, StringSplitOptions.RemoveEmptyEntries),
                    elem.GetAttribute("languageContext")
                );

                foreach (string ext in info.Extensions)
                {
                    languages["." + ext.ToLower()] = info;
                }
            }

            return languages;
        }
    }
#endif

}
