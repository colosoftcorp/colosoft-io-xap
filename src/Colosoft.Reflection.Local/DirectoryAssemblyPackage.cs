using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace Colosoft.Reflection.Local
{
    internal class DirectoryAssemblyPackage : IAssemblyPackage
    {
        private readonly IEnumerable<string> assemblyFiles;
        private readonly AssemblyResolverManager assemblyResolverManager;
        private readonly IEnumerable<string> assemblyPaths;
        private Dictionary<string, Assembly> assemblies;

        private Dictionary<string, Assembly> Assemblies
        {
            get
            {
                if (this.assemblies == null)
                {
                    this.assemblies = this.LoadAssemblies();
                }

                return this.assemblies;
            }
        }

        public Guid Uid { get; } = Guid.NewGuid();

        public int Count => this.assemblyFiles.Count();

        public DateTime CreateTime
        {
            get { return DateTime.MinValue; }
        }

        public AssemblyPart this[int index]
        {
            get
            {
                var fileName = this.assemblyFiles.ElementAt(index);
                return new AssemblyPart(System.IO.Path.GetFileName(fileName));
            }
        }

        public DirectoryAssemblyPackage(
            AssemblyResolverManager assemblyResolveManager,
            IEnumerable<string> assemblyFiles)
        {
            this.assemblyResolverManager = assemblyResolveManager;

            var directories = assemblyFiles
                    .Select(f => System.IO.Path.GetDirectoryName(f))
                    .Distinct()
                    .Select(f => new System.IO.DirectoryInfo(f))
                    .Where(f => f.Exists);

            var files = new List<string>();
            foreach (var dir in directories)
            {
                files.AddRange(dir.GetFiles("*.dll", System.IO.SearchOption.TopDirectoryOnly).Select(f => f.FullName));
            }

            this.assemblyPaths = assemblyFiles;
            this.assemblyFiles = files;
        }

        private string GetFileName(AssemblyPart name)
        {
            var fileName = this.assemblyFiles.First(f =>
                StringComparer.InvariantCultureIgnoreCase
                    .Equals(name.Source, System.IO.Path.GetFileName(f)));

            return fileName;
        }

        private Dictionary<string, Assembly> LoadAssemblies()
        {
            var domainAssemblies = new Dictionary<string, Assembly>();
            var exceptions = new List<Exception>();

            foreach (var i in this.assemblyResolverManager.AppDomain.GetAssemblies())
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

            var resolver = new LoadAssemblyResolver(this.assemblyResolverManager.AppDomain, domainAssemblies, this.assemblyFiles);
            this.assemblyResolverManager.Add(resolver);

            var list = new Dictionary<string, System.Reflection.Assembly>();
            try
            {
                foreach (var part in this.assemblyPaths)
                {
                    Assembly assembly = null;

                    var partFileName = System.IO.Path.GetFileName(part);

                    if (!domainAssemblies.TryGetValue(partFileName, out assembly))
                    {
                        try
                        {
                            var name = AssemblyName.GetAssemblyName(part);
                            assembly = this.assemblyResolverManager.AppDomain.Load(name);
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
                        catch (ReflectionTypeLoadException ex)
                        {
                            exceptions.Add(new ReflectionTypeLoadException(
                                ex.Types,
                                ex.LoaderExceptions,
                                $"An error ocurred when load types from assembly '{assembly.FullName}'"));
                            continue;
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(
                                new Exception(
                                    $"An error ocurred when load types from assembly '{assembly.FullName}'",
                                    ex));
                            continue;
                        }

                        if (!domainAssemblies.ContainsKey(partFileName))
                        {
                            domainAssemblies.Add(partFileName, assembly);
                        }
                    }

                    if (!list.ContainsKey(partFileName))
                    {
                        list.Add(partFileName, assembly);
                    }
                }
            }
            finally
            {
                this.assemblyResolverManager.Remove(resolver);
            }

            if (exceptions.Count > 0)
            {
                throw new AggregateException(exceptions);
            }

            return list;
        }

        public Assembly GetAssembly(AssemblyPart name)
        {
            return this.Assemblies[name.Source];
        }

        public Assembly LoadAssemblyGuarded(AssemblyPart name, out Exception exception)
        {
            try
            {
                exception = null;
                return this.Assemblies[name.Source];
            }
            catch (Exception ex)
            {
                exception = ex;
                return null;
            }
        }

        public System.IO.Stream GetAssemblyStream(AssemblyPart name)
        {
            return System.IO.File.OpenRead(this.GetFileName(name));
        }

        public bool ExtractPackageFiles(string outputDirectory, bool canOverride)
        {
            return true;
        }

        public bool Contains(AssemblyPart assemblyPart)
        {
            return this.assemblyPaths.Any(
                f =>
                    StringComparer.InvariantCultureIgnoreCase
                        .Equals(assemblyPart.Source, System.IO.Path.GetFileName(f)));
        }

        public IEnumerator<AssemblyPart> GetEnumerator()
        {
            return this.assemblyPaths.Select(f => new AssemblyPart(f)).GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            return this.assemblyPaths.Select(f => new AssemblyPart(f)).GetEnumerator();
        }

        private sealed class LoadAssemblyResolver : IAssemblyResolver
        {
#pragma warning disable S4487 // Unread "private" fields should be removed
            private readonly AppDomain appDomain;
#pragma warning restore S4487 // Unread "private" fields should be removed

            private readonly IEnumerable<string> deploymentParts;

            public bool IsValid => true;

            public Dictionary<string, Assembly> Assemblies { get; }

            public LoadAssemblyResolver(
                AppDomain appDomain,
                Dictionary<string, System.Reflection.Assembly> assemblies,
                IEnumerable<string> deploymentParts)
            {
                this.appDomain = appDomain;
                this.Assemblies = assemblies;
                this.deploymentParts = deploymentParts;
            }

            public bool Resolve(ResolveEventArgs args, out System.Reflection.Assembly assembly, out Exception error)
            {
                var libraryName = AssemblyNameResolver.GetAssemblyName(args.Name);

                if (!libraryName.EndsWith(".dll", StringComparison.InvariantCultureIgnoreCase))
                {
                    libraryName = string.Concat(libraryName, ".dll");
                }

                if (this.Assemblies.TryGetValue(libraryName, out assembly))
                {
                    error = null;
                    return true;
                }

                var part = this.deploymentParts.FirstOrDefault(f =>
                    string.Compare(System.IO.Path.GetFileName(f), libraryName, true, System.Globalization.CultureInfo.InvariantCulture) == 0);

                if (part != null)
                {
                    try
                    {
#if !NETSTANDARD2_0 && !NETCOREAPP2_0
                        var name = System.Reflection.AssemblyName.GetAssemblyName(part);
                        assembly = this.appDomain.Load(name);
#else
                        assembly = System.Runtime.Loader.AssemblyLoadContext.Default.LoadFromAssemblyPath(part);
#endif
                        assembly.GetTypes();
                    }
                    catch (Exception ex)
                    {
                        error = ex;
                        return false;
                    }

                    this.Assemblies.Add(libraryName, assembly);
                    error = null;
                    return true;
                }

                error = null;
                return false;
            }
        }
    }
}
