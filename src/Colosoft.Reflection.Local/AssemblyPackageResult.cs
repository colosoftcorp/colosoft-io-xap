using System;
using System.Collections.Generic;
using System.Linq;

namespace Colosoft.Reflection.Local
{
    internal sealed class AssemblyPackageResult : IAssemblyPackageResult
    {
        private readonly string packageFileName;
        private readonly AssemblyResolverManager assemblyResolverManager;
        private Guid packageUid;
        private Dictionary<string, System.Reflection.Assembly> assemblies;
        private AggregateException extractPackageAssembliesException;

        private AssemblyResolverManager AssemblyResolverManager
        {
            get
            {
                return this.assemblyResolverManager;
            }
        }

        public AssemblyPackageResult(AssemblyResolverManager assemblyResolverManager, Guid uid, string packageFileName)
        {
            this.assemblyResolverManager = assemblyResolverManager;
            this.packageUid = uid;
            this.packageFileName = packageFileName;
        }

        ~AssemblyPackageResult()
        {
            this.Dispose();
        }

        private void ExtractPackageAssemblies()
        {
            if (this.assemblies != null)
            {
                return;
            }

            var result = new Dictionary<string, System.Reflection.Assembly>();

            if (!string.IsNullOrEmpty(this.packageFileName) && System.IO.File.Exists(this.packageFileName))
            {
                using (var stream = System.IO.File.OpenRead(this.packageFileName))
                {
                    foreach (var assembly in XapPackage.LoadPackagedAssemblies(
                        this.AssemblyResolverManager, System.IO.Path.GetDirectoryName(this.packageFileName), this.packageUid, stream, false, out this.extractPackageAssembliesException))
                    {
                        result.Add($"{assembly.GetName().Name}.dll", assembly);
                    }
                }
            }

            this.assemblies = result;
        }

        public bool ExtractPackageFiles(string outputDirectory, bool canOverride)
        {
            if (outputDirectory is null)
            {
                throw new ArgumentNullException(nameof(outputDirectory));
            }

            if (!string.IsNullOrEmpty(this.packageFileName) && System.IO.File.Exists(this.packageFileName))
            {
                using (var stream = System.IO.File.OpenRead(this.packageFileName))
                {
                    XapPackage.ExtractPackageAssemblies(stream, outputDirectory, canOverride);
                }
            }

            return true;
        }

        public System.IO.Stream GetAssemblyStream(AssemblyPart name)
        {
            System.IO.Stream result = null;

            if (!string.IsNullOrEmpty(this.packageFileName) && System.IO.File.Exists(this.packageFileName))
            {
                var fileInfo = new System.IO.FileInfo(this.packageFileName);

                if (fileInfo.Length > 0)
                {
                    using (var packageStream = System.IO.File.OpenRead(this.packageFileName))
                    {
                        result = new System.IO.MemoryStream();

                        if (XapPackage.GetAssembly(packageStream, result, name))
                        {
                            result.Seek(0, System.IO.SeekOrigin.Begin);
                        }
                        else
                        {
                            result.Dispose();
                            result = null;
                        }
                    }
                }
            }

            return result;
        }

        public System.Reflection.Assembly LoadAssembly(AssemblyPart name)
        {
            if (name is null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            this.ExtractPackageAssemblies();

            if (this.extractPackageAssembliesException != null)
            {
                throw this.extractPackageAssembliesException;
            }

            System.Reflection.Assembly assembly = null;

            if (this.assemblies != null &&
                this.assemblies.TryGetValue(name.Source, out assembly))
            {
                return assembly;
            }

            return null;
        }

        public System.Reflection.Assembly LoadAssemblyGuarded(AssemblyPart name, out Exception exception)
        {
            try
            {
                exception = null;
                return this.LoadAssembly(name);
            }
            catch (System.IO.FileNotFoundException exception2)
            {
                exception = exception2;
            }
            catch (System.IO.FileLoadException exception3)
            {
                exception = exception3;
            }
            catch (BadImageFormatException exception4)
            {
                exception = exception4;
            }
            catch (System.Reflection.ReflectionTypeLoadException exception5)
            {
                exception = exception5;
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            return null;
        }

        public System.Reflection.Assembly GetAssembly(AssemblyPart name)
        {
            return this.LoadAssembly(name);
        }

        public AssemblyPackage CreatePackage()
        {
            List<AssemblyPart> assemblyNames = null;
            DateTime createdDate = DateTime.Now;

            if (!string.IsNullOrEmpty(this.packageFileName))
            {
                var fileInfo = new System.IO.FileInfo(this.packageFileName);

                if (fileInfo.Exists)
                {
                    using (var stream = System.IO.File.OpenRead(this.packageFileName))
                    {
                        assemblyNames = XapPackage.GetDeploymentParts(stream).ToList();
                    }

                    createdDate = fileInfo.LastWriteTime;
                }
            }

            if (assemblyNames == null)
            {
                assemblyNames = new List<AssemblyPart>();
            }

            return new AssemblyPackage(assemblyNames)
            {
                Uid = this.packageUid,
                CreateTime = createdDate,
                Result = this,
            };
        }

        public void Dispose()
        {
            GC.SuppressFinalize(this);
        }
    }
}
