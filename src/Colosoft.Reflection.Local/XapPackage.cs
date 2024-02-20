using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;

namespace Colosoft.Reflection.Local
{
    public static class XapPackage
    {
        private static IEnumerable<AssemblyPart> GetDeploymentParts(ZipArchive zipArchive)
        {
            var zipEntry = zipArchive.Entries.FirstOrDefault(f => f.Name == "AppManifest.xaml");

            if (zipEntry == null)
            {
                throw new InvalidOperationException("AppManifest not found.");
            }

            var list = new List<AssemblyPart>();

            System.IO.Stream resourceStream = null;

            try
            {
                using (var stream = zipEntry.Open())
                {
                    resourceStream = new System.IO.MemoryStream();

                    var buffer = new byte[1024];
                    var read = 0;

                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        resourceStream.Write(buffer, 0, read);
                    }

                    resourceStream.Seek(0, System.IO.SeekOrigin.Begin);
                }

                using (System.Xml.XmlReader reader = System.Xml.XmlReader.Create(resourceStream))
                {
                    if (!reader.ReadToFollowing("AssemblyPart"))
                    {
                        return list;
                    }

                    do
                    {
                        string attribute = reader.GetAttribute("Source");
                        if (attribute != null)
                        {
                            AssemblyPart item = new AssemblyPart();
                            item.Source = attribute;
                            list.Add(item);
                        }
                    }
                    while (reader.ReadToNextSibling("AssemblyPart"));
                }
            }
            finally
            {
                if (resourceStream != null)
                {
                    resourceStream.Dispose();
                }
            }

            return list;
        }

        public static IEnumerable<AssemblyPart> GetDeploymentParts(System.IO.Stream packageStream)
        {
            using (var zipArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                return GetDeploymentParts(zipArchive);
            }
        }

        public static IEnumerable<System.Reflection.Assembly> LoadPackagedAssemblies(
            AssemblyResolverManager resolverManager,
            string assemblyRepositoryDirectory,
            Guid packageUid,
            System.IO.Stream packageStream,
            bool canOverride,
            out AggregateException aggregateException)
        {
            if (resolverManager is null)
            {
                throw new ArgumentNullException(nameof(resolverManager));
            }

            if (packageStream is null)
            {
                throw new ArgumentNullException(nameof(packageStream));
            }

            var exceptions = new List<Exception>();

            var domainAssemblies = new Dictionary<string, System.Reflection.Assembly>(StringComparer.InvariantCultureIgnoreCase);

            foreach (var i in resolverManager.AppDomain.GetAssemblies())
            {
                var key = $"{i.GetName().Name}.dll";

                if (!domainAssemblies.ContainsKey(key))
                {
                    domainAssemblies.Add(key, i);
                }
                else
                {
                    domainAssemblies[key] = i;
                }
            }

            string packageDirectory = null;

            if (!string.IsNullOrEmpty(assemblyRepositoryDirectory))
            {
                packageDirectory = System.IO.Path.Combine(assemblyRepositoryDirectory, packageUid.ToString());

                if (!System.IO.Directory.Exists(packageDirectory))
                {
                    System.IO.Directory.CreateDirectory(packageDirectory);
                }
            }

            using (var zipArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                if (packageDirectory != null)
                {
                    foreach (var entry in zipArchive.Entries)
                    {
                        entry.ExtractToFile(System.IO.Path.Combine(packageDirectory, entry.FullName), canOverride);
                    }
                }

                var list = new List<System.Reflection.Assembly>();

                IEnumerable<AssemblyPart> deploymentParts = GetDeploymentParts(zipArchive);

                var resolver = new LoadPackageAssemblyResolver(resolverManager.AppDomain, domainAssemblies, deploymentParts, zipArchive, packageDirectory);
                resolverManager.Add(resolver);
                try
                {
                    foreach (AssemblyPart part in deploymentParts)
                    {
                        System.Reflection.Assembly assembly = null;

                        if (!domainAssemblies.TryGetValue(part.Source, out assembly))
                        {
                            if (packageDirectory == null)
                            {
                                var fileInfo = zipArchive.Entries.FirstOrDefault(f => f.Name == part.Source);

                                var raw = new byte[fileInfo.Length];

                                var entry = zipArchive.GetEntry(part.Source);
                                using (var file = entry.Open())
                                {
                                    file.Read(raw, 0, raw.Length);
                                }

                                try
                                {
                                    assembly = part.Load(resolverManager.AppDomain, raw);
                                }
                                catch (Exception ex)
                                {
                                    exceptions.Add(ex);
                                    continue;
                                }
                            }
                            else
                            {
                                try
                                {
                                    assembly = part.Load(resolverManager.AppDomain, System.IO.Path.Combine(packageDirectory, part.Source));
                                }
                                catch (Exception ex)
                                {
                                    exceptions.Add(ex);
                                    continue;
                                }

                                try
                                {
                                    assembly.GetTypes();
                                }
                                catch (System.Reflection.ReflectionTypeLoadException ex)
                                {
                                    exceptions.Add(new System.Reflection.ReflectionTypeLoadException(
                                        ex.Types,
                                        ex.LoaderExceptions,
                                        $"An error ocurred when load types from assembly '{assembly.FullName}'"));
                                    continue;
                                }
                                catch (Exception ex)
                                {
                                    exceptions.Add(new Exception($"An error ocurred when load types from assembly '{assembly.FullName}'", ex));
                                    continue;
                                }
                            }

                            if (!domainAssemblies.ContainsKey(part.Source))
                            {
                                domainAssemblies.Add(part.Source, assembly);
                            }
                        }

                        list.Add(assembly);
                    }
                }
                finally
                {
                    resolverManager.Remove(resolver);
                }

                if (exceptions.Count > 0)
                {
                    aggregateException = new AggregateException(exceptions);
                }
                else
                {
                    aggregateException = null;
                }

                return list;
            }
        }

        public static void ExtractPackageAssemblies(System.IO.Stream packageStream, string outputDirectory, bool canOverride = false)
        {
            if (packageStream is null)
            {
                throw new ArgumentNullException(nameof(packageStream));
            }

            using (var zipArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                foreach (var entry in zipArchive.Entries)
                {
                    entry.ExtractToFile(System.IO.Path.Combine(outputDirectory, entry.FullName), canOverride);
                }
            }
        }

        public static bool GetAssembly(System.IO.Stream packageStream, System.IO.Stream assemblyStream, AssemblyPart part)
        {
            if (packageStream is null)
            {
                throw new ArgumentNullException(nameof(packageStream));
            }

            if (assemblyStream is null)
            {
                throw new ArgumentNullException(nameof(assemblyStream));
            }

            if (part is null)
            {
                throw new ArgumentNullException(nameof(part));
            }

            using (var zipArchive = new ZipArchive(packageStream, ZipArchiveMode.Read, true))
            {
                foreach (var i in zipArchive.Entries)
                {
                    if (i.Name == part.Source)
                    {
                        using (var stream = i.Open())
                        {
                            var buffer = new byte[1024];
                            var read = 0;

                            while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                assemblyStream.Write(buffer, 0, read);
                            }
                        }

                        return true;
                    }
                }
            }

            return false;
        }

        private sealed class LoadPackageAssemblyResolver : IAssemblyResolver
        {
            private readonly string packageDirectory;
            private readonly AppDomain appDomain;
            private readonly ZipArchive zipArchive;
            private readonly IEnumerable<AssemblyPart> deploymentParts;
            private readonly Dictionary<string, System.Reflection.Assembly> assemblies;

            public bool IsValid
            {
                get { return true; }
            }

            public LoadPackageAssemblyResolver(
                AppDomain appDomain,
                Dictionary<string, System.Reflection.Assembly> assemblies,
                IEnumerable<AssemblyPart> deploymentParts,
                ZipArchive zipArchive,
                string packageDirectory)
            {
                this.appDomain = appDomain;
                this.assemblies = assemblies;
                this.deploymentParts = deploymentParts;
                this.zipArchive = zipArchive;
                this.packageDirectory = packageDirectory;
            }

            public bool Resolve(ResolveEventArgs args, out System.Reflection.Assembly assembly, out Exception error)
            {
                var libraryName = AssemblyNameResolver.GetAssemblyName(args.Name);

                if (!libraryName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase))
                {
                    libraryName = string.Concat(libraryName, ".dll");
                }

                if (this.assemblies.TryGetValue(libraryName, out assembly))
                {
                    error = null;
                    return true;
                }

                var part = this.deploymentParts.FirstOrDefault(f => StringComparer.InvariantCultureIgnoreCase.Compare(f.Source, libraryName) == 0);

                if (part != null)
                {
                    try
                    {
                        if (this.packageDirectory == null)
                        {
                            var fileInfo = this.zipArchive.Entries.FirstOrDefault(f => f.Name == part.Source);

                            var buffer = new byte[fileInfo.Length];

                            using (var file = fileInfo.Open())
                            {
                                file.Read(buffer, 0, buffer.Length);
                            }

                            assembly = part.Load(this.appDomain, buffer);
                        }
                        else
                        {
                            assembly = part.Load(this.appDomain, System.IO.Path.Combine(this.packageDirectory, part.Source));
                        }

                        assembly.GetTypes();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                        return false;
                    }

                    this.assemblies.Add(libraryName, assembly);
                    error = null;
                    return true;
                }

                error = null;
                return false;
            }
        }
    }
}
