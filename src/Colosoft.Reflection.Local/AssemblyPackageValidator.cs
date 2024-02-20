using System;
using System.Collections.Generic;

namespace Colosoft.Reflection.Local
{
    public class AssemblyPackageValidator : IAssemblyPackageValidator
    {
        private readonly string[] assemblyFilesDirectories;

        public AssemblyPackageValidator(string[] assemblyFilesDirectories)
        {
            if (assemblyFilesDirectories == null)
            {
                throw new ArgumentNullException(nameof(assemblyFilesDirectories));
            }

            foreach (var i in assemblyFilesDirectories)
            {
                if (string.IsNullOrEmpty(i) || !System.IO.Directory.Exists(i))
                {
                    throw new InvalidOperationException($"Assembly files directory {i} not exists");
                }
            }

            this.assemblyFilesDirectories = assemblyFilesDirectories;
        }

        private bool Validate(IAssemblyPackage assemblyPackage, IDictionary<string, FileInfo2> files)
        {
            if (assemblyPackage == null)
            {
                return false;
            }

            FileInfo2 info = null;
            foreach (var part in assemblyPackage)
            {
                if (!files.TryGetValue(part.Source, out info) ||
                    info.LastWriteTime > assemblyPackage.CreateTime)
                {
                    return false;
                }
            }

            return true;
        }

        private IDictionary<string, FileInfo2> GetDirectoryFiles()
        {
            var dic = new Dictionary<string, FileInfo2>(Text.StringEqualityComparer.GetComparer(StringComparison.InvariantCultureIgnoreCase));

            foreach (var directory in this.assemblyFilesDirectories)
            {
                foreach (var i in System.IO.Directory.GetFiles(directory))
                {
                    var fileName = System.IO.Path.GetFileName(i);

                    if (!dic.ContainsKey(fileName))
                    {
                        dic.Add(fileName, new FileInfo2(i));
                    }
                }
            }

            return dic;
        }

        public bool[] Validate(IAssemblyPackage[] assemblyPackages)
        {
            if (assemblyPackages is null)
            {
                throw new ArgumentNullException(nameof(assemblyPackages));
            }

            if (assemblyPackages.Length == 0)
            {
                return Array.Empty<bool>();
            }

            var files = this.GetDirectoryFiles();

            var result = new bool[assemblyPackages.Length];

            for (int i = 0; i < result.Length; i++)
            {
                result[i] = this.Validate(assemblyPackages[i], files);
            }

            return result;
        }

        private sealed class FileInfo2
        {
            private readonly string path;
            private DateTime? lastWriteTime;

            public string Path
            {
                get { return this.path; }
            }

            public DateTime? LastWriteTime
            {
                get
                {
                    if (this.lastWriteTime == null)
                    {
                        this.lastWriteTime = System.IO.File.GetLastWriteTime(this.path);
                    }

                    return this.lastWriteTime;
                }
            }

            public FileInfo2(string path)
            {
                this.path = path;
            }
        }
    }
}
