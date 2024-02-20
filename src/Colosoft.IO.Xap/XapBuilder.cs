using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;

namespace Colosoft.IO.Xap
{
    public static class XapBuilder
    {
        public static byte[] XapToMemory(IXapConfiguration configuration, string directoryName)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            using (var ms = new MemoryStream())
            using (var zipArchive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                XapFiles(configuration, zipArchive, directoryName);
                return ms.ToArray();
            }
        }

        public static byte[] XapToMemory(IXapConfiguration configuration, IEnumerable<XapEntry> entries)
        {
            using (var ms = new MemoryStream())
            using (var zipArchive = new ZipArchive(ms, ZipArchiveMode.Create, true))
            {
                XapFiles(configuration, zipArchive, entries);
                return ms.ToArray();
            }
        }

        public static void XapToDisk(IXapConfiguration configuration, string directoryName, string xapfile)
        {
            if (string.IsNullOrEmpty(xapfile))
            {
                throw new ArgumentException($"'{nameof(xapfile)}' cannot be null or empty.", nameof(xapfile));
            }

            using (var output = File.Create(xapfile))
            using (var archive = new ZipArchive(output, ZipArchiveMode.Create))
            {
                XapFiles(configuration, archive, directoryName);
            }
        }

        public static void XapToDisk(IXapConfiguration configuration, IEnumerable<XapEntry> entries, string xapfile)
        {
            using (var outputStream = File.Create(xapfile))
            using (var archive = new ZipArchive(outputStream, ZipArchiveMode.Create))
            {
                XapFiles(configuration, archive, entries);
            }
        }

        internal static string GetRelativePath(string fileName, string directory)
        {
            int directoryEnd = directory.Length;
            if (directoryEnd == 0)
            {
                return fileName;
            }

            while (directoryEnd < fileName.Length && fileName[directoryEnd] == Path.DirectorySeparatorChar)
            {
                directoryEnd++;
            }

            string relativePath = fileName.Substring(directoryEnd);
            return relativePath;
        }

        public static void XapFiles(IXapConfiguration configuration, ZipArchive zip, string directoryName)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (zip is null)
            {
                throw new ArgumentNullException(nameof(zip));
            }

            if (string.IsNullOrEmpty(directoryName))
            {
                throw new ArgumentException($"'{nameof(directoryName)}' cannot be null or empty.", nameof(directoryName));
            }

            ICollection<LanguageInfo> langs = FindSourceLanguages(configuration, directoryName);

            string manifestPath = Path.Combine(directoryName, "AppManifest.xaml");
            IList<Uri> assemblies;

            if (File.Exists(manifestPath))
            {
                assemblies = GetManifestAssemblies(manifestPath);
            }
            else
            {
                assemblies = GetLanguageAssemblies(configuration, langs);
                var manifestEntry = zip.CreateEntry("AppManifest.xaml");

                using (Stream appManifest = manifestEntry.Open())
                {
                    configuration.ManifestTemplate.Generate(assemblies).Save(appManifest);
                }
            }

            AddAssemblies(zip, directoryName, assemblies);

            GenerateLanguagesConfig(zip, langs);

            foreach (string path in Directory.GetFiles(directoryName, "*", SearchOption.AllDirectories))
            {
                string relativePath = GetRelativePath(path, directoryName);
                zip.CreateEntryFromFile(path, Path.Combine(string.Empty, relativePath));
            }
        }

        public static void XapFiles(IXapConfiguration configuration, ZipArchive zip, IEnumerable<XapEntry> entries)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            if (zip is null)
            {
                throw new ArgumentNullException(nameof(zip));
            }

            if (entries is null)
            {
                throw new ArgumentNullException(nameof(entries));
            }

            ICollection<LanguageInfo> langs = FindSourceLanguages(configuration, entries);
            IList<Uri> assemblies;

            assemblies = GetLanguageAssemblies(configuration, langs);
            var manifestEntry = zip.CreateEntry("AppManifest.xaml");

            using (var appManifest = manifestEntry.Open())
            {
                configuration.ManifestTemplate.Generate(assemblies).Save(appManifest);
            }

            var buffer = new byte[1024];
            var read = 0;
            foreach (var entry in entries)
            {
                var zipEntry = zip.CreateEntry(entry.Name);
                using (var outputStream = zipEntry.Open())
                {
                    while ((read = entry.Stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        outputStream.Write(buffer, 0, read);
                    }

                    outputStream.Flush();
                }

                zipEntry.LastWriteTime = entry.LastWriteTime;
            }

            GenerateLanguagesConfig(zip, langs);
        }

        private static IList<Uri> GetManifestAssemblies(string manifestPath)
        {
            var assemblies = new List<Uri>();

            var doc = new System.Xml.XmlDocument();
            doc.Load(manifestPath);
            foreach (System.Xml.XmlElement ap in doc.GetElementsByTagName("AssemblyPart"))
            {
                string src = ap.GetAttribute("Source");
                if (!string.IsNullOrEmpty(src))
                {
                    assemblies.Add(new Uri(src, UriKind.RelativeOrAbsolute));
                }
            }

            return assemblies;
        }

        internal static System.Xml.XmlDocument GenerateManifest(IXapConfiguration configuration, string dir)
        {
            return configuration.ManifestTemplate.Generate(GetLanguageAssemblies(configuration, FindSourceLanguages(configuration, dir)));
        }

        private static IList<Uri> GetLanguageAssemblies(IXapConfiguration configuration, IEnumerable<LanguageInfo> langs)
        {
            List<Uri> assemblies = new List<Uri>();

            foreach (LanguageInfo lang in langs)
            {
                foreach (string asm in lang.Assemblies)
                {
                    assemblies.Add(GetUri(configuration, asm));
                }
            }

            return assemblies;
        }

        private static Uri GetUri(IXapConfiguration configuration, string path)
        {
            if (configuration is null)
            {
                throw new ArgumentNullException(nameof(configuration));
            }

            Uri uri = new Uri(path, UriKind.RelativeOrAbsolute);
            string prefix = configuration.UrlPrefix;
            if (!string.IsNullOrEmpty(prefix) && !IsPathRooted(uri))
            {
                uri = new Uri(prefix + path, UriKind.RelativeOrAbsolute);
            }

            return uri;
        }

        private static bool IsPathRooted(Uri uri)
        {
            return uri.IsAbsoluteUri || uri.OriginalString.StartsWith("/", StringComparison.InvariantCultureIgnoreCase);
        }

        private static void AddAssemblies(ZipArchive zip, string directoryName, IList<Uri> assemblyLocations)
        {
            foreach (Uri uri in assemblyLocations)
            {
                if (IsPathRooted(uri))
                {
                    continue;
                }

                string targetPath = uri.OriginalString;
                string localPath = Path.Combine(directoryName, targetPath);

                if (!File.Exists(localPath))
                {
                    throw new InvalidOperationException($"Could not find assembly: {uri}");
                }

                zip.CreateEntryFromFile(localPath, targetPath);

                string pdbPath = Path.ChangeExtension(localPath, ".pdb");
                string pdbTarget = Path.ChangeExtension(targetPath, ".pdb");
                if (File.Exists(pdbPath))
                {
                    zip.CreateEntryFromFile(pdbPath, pdbTarget);
                }
            }
        }

        private static void GenerateLanguagesConfig(ZipArchive zip, ICollection<LanguageInfo> langs)
        {
            bool needLangConfig = false;
            foreach (LanguageInfo lang in langs)
            {
                if (!string.IsNullOrEmpty(lang.LanguageContext))
                {
                    needLangConfig = true;
                    break;
                }
            }

            if (needLangConfig)
            {
                var entry = zip.CreateEntry("languages.config");
                using (var outStream = entry.Open())
                {
                    var writer = new StreamWriter(outStream);
                    writer.WriteLine("<Languages>");

                    foreach (LanguageInfo lang in langs)
                    {
                        writer.WriteLine("  <Language languageContext=\"{0}\"", lang.LanguageContext);
                        writer.WriteLine("            assembly=\"{0}\"", lang.GetContextAssemblyName());
                        writer.WriteLine("            extensions=\"{0}\" />", lang.GetExtensionsString());
                    }

                    writer.WriteLine("</Languages>");
                    writer.Close();
                }
            }
        }

        internal static ICollection<LanguageInfo> FindSourceLanguages(IXapConfiguration configuration, string directoryName)
        {
            Dictionary<LanguageInfo, bool> result = new Dictionary<LanguageInfo, bool>();

            foreach (string file in Directory.GetFiles(directoryName, "*", SearchOption.AllDirectories))
            {
                string ext = Path.GetExtension(file);
                LanguageInfo lang;
                if (configuration.Languages.TryGetValue(ext.ToLower(), out lang))
                {
                    result[lang] = true;
                }
            }

            return result.Keys;
        }

        internal static ICollection<LanguageInfo> FindSourceLanguages(IXapConfiguration configuration, IEnumerable<XapEntry> entries)
        {
            Dictionary<LanguageInfo, bool> result = new Dictionary<LanguageInfo, bool>();

            foreach (string file in entries.Select(f => f.Name))
            {
                string ext = Path.GetExtension(file);
                LanguageInfo lang;
                if (configuration.Languages.TryGetValue(ext.ToLower(), out lang))
                {
                    result[lang] = true;
                }
            }

            return result.Keys;
        }
    }
}
