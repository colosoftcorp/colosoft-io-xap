using System;
using System.Collections.Generic;
using System.Linq;

namespace Colosoft.Reflection.Local
{
    public class AssemblyInfoRepository : IAssemblyInfoRepository
    {
        private readonly AssemblyAnalyzer assemblyAnalyzer;
        private readonly string assembliesDirectory;
        private readonly IAssemblyInfoRepositoryObserver observer;
        private readonly object objLock = new object();
        private Dictionary<string, AssemblyInfoEntry> assemblies;
        private DateTime manifestFileLastWriteTime = DateTime.MinValue;
        private bool isLoaded;

        private static AssemblyInfo ConvertAssemblyInfo(AsmData data, string fileName)
        {
            var info = new AssemblyInfo
            {
                Name = System.IO.Path.GetFileNameWithoutExtension(fileName),
                LastWriteTime = System.IO.File.GetLastWriteTime(fileName),
                References = data.References.Select(f => f.Name).ToArray(),
            };

            return info;
        }

        public event EventHandler Loaded;

        public bool IsLoaded
        {
            get { return this.isLoaded; }
        }

        protected string ManifestFileName
        {
            get { return System.IO.Path.Combine(this.assembliesDirectory, "AssembliesManifest.xml"); }
        }

        public int Count
        {
            get
            {
                this.CheckInitialize();
                return this.assemblies.Count;
            }
        }

        public bool IsChanged
        {
            get
            {
                return this.IsManifestFileChanged;
            }
        }

        public bool IsManifestFileChanged
        {
            get
            {
                var fileInfo = new System.IO.FileInfo(this.ManifestFileName);
                if (!fileInfo.Exists)
                {
                    return true;
                }

                return fileInfo.LastWriteTime != this.manifestFileLastWriteTime;
            }
        }

        public AssemblyInfoRepository(string assembliesDirectory)
            : this(assembliesDirectory, null)
        {
        }

        public AssemblyInfoRepository(string assembliesDirectory, IAssemblyInfoRepositoryObserver observer)
        {
            this.assembliesDirectory = assembliesDirectory ?? throw new ArgumentNullException(nameof(assembliesDirectory));
            this.assemblyAnalyzer = new AssemblyAnalyzer();
            this.observer = observer;
        }

        private bool ManifestExists()
        {
            return System.IO.File.Exists(this.ManifestFileName);
        }

        private AssemblyInfoEntry[] GetManifest()
        {
            var fileInfo = new System.IO.FileInfo(this.ManifestFileName);

            if (!fileInfo.Exists)
            {
                return Array.Empty<AssemblyInfoEntry>();
            }

#pragma warning disable CA1031 // Do not catch general exception types
            try
            {
                using (var stream = fileInfo.OpenRead())
                using (var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings
                {
                    DtdProcessing = System.Xml.DtdProcessing.Ignore,
                    CloseInput = false,
                }))
                {
                    var serializer = new System.Xml.Serialization.XmlSerializer(typeof(AssemblyInfoEntry[]));
                    var result = (AssemblyInfoEntry[])serializer.Deserialize(reader);

                    this.manifestFileLastWriteTime = fileInfo.LastWriteTime;
                    return result;
                }
            }
            catch
            {
                return Array.Empty<AssemblyInfoEntry>();
            }
#pragma warning restore CA1031 // Do not catch general exception types
        }

        private void SaveManifest()
        {
            var manifestFileName = this.ManifestFileName;

            using (var stream = System.IO.File.Create(manifestFileName))
            {
                var serializer = new System.Xml.Serialization.XmlSerializer(typeof(AssemblyInfoEntry[]));
                serializer.Serialize(stream, this.assemblies.Values.ToArray());
            }
        }

        private void CheckInitialize()
        {
            if (this.assemblies == null || this.IsManifestFileChanged || !this.ManifestExists())
            {
                this.Refresh(false);
            }
        }

        protected virtual void OnLoaded()
        {
            this.isLoaded = true;

            if (this.observer != null)
            {
                this.observer.OnLoaded();
            }

            this.Loaded?.Invoke(this, EventArgs.Empty);
        }

        protected virtual void OnLoadingAssemblyFiles()
        {
            if (this.observer != null)
            {
                this.observer.OnLoadingAssemblyFiles();
            }
        }

        protected virtual void OnAnalysisAssemblyProgressChanged(IMessageFormattable message, int percentage)
        {
            if (this.observer != null)
            {
                this.observer.OnAnalysisAssemblyProgressChanged(message, percentage);
            }
        }

        public void Refresh(bool executeAnalyzer)
        {
            var isInitializing = false;

            lock (this.objLock)
            {
                string[] originalFiles = null;
                List<string> files = null;

                this.OnLoadingAssemblyFiles();

#pragma warning disable CA1031 // Do not catch general exception types
                try
                {
                    originalFiles = System.IO.Directory.GetFiles(this.assembliesDirectory, "*.dll")
                                    .Concat(System.IO.Directory.GetFiles(this.assembliesDirectory, "*.exe"))
                                            .Select(f => System.IO.Path.GetFileName(f))
                                            .ToArray();
                    Array.Sort<string>(originalFiles, StringComparer.InvariantCulture);
                    files = new List<string>(originalFiles);
                }
                catch
                {
                    files = new List<string>();
                }
#pragma warning restore CA1031 // Do not catch general exception types

                bool manifestExists = this.ManifestExists();
                isInitializing = this.assemblies == null || this.IsManifestFileChanged;

                if (isInitializing)
                {
                    this.assemblies = new Dictionary<string, AssemblyInfoEntry>();

                    if (!manifestExists && !executeAnalyzer)
                    {
                        // Carrega os arquivo originais do diretório com os assemblies
                        foreach (var file in originalFiles)
                        {
                            var entry = new AssemblyInfoEntry
                            {
                                FileName = file,
                                Info = new AssemblyInfo
                                {
                                    Name = System.IO.Path.GetFileNameWithoutExtension(file),
                                    References = Array.Empty<string>(),
                                    LastWriteTime = System.IO.File.GetLastWriteTime(System.IO.Path.Combine(this.assembliesDirectory, file)),
                                },
                            };

                            this.assemblies.Add(entry.Info.Name, entry);
                        }
                    }
                    else
                    {
                        foreach (var i in this.GetManifest())
                        {
                            this.assemblies.Add(i.Info.Name, i);
                        }
                    }
                }

                var removeNames = new Queue<string>();

                foreach (var i in this.assemblies)
                {
                    var index = files.BinarySearch(i.Value.FileName, StringComparer.InvariantCulture);
                    if (index < 0)
                    {
                        removeNames.Enqueue(i.Key);
                    }
                    else
                    {
                        files.RemoveAt(index);
                    }
                }

                while (removeNames.Count > 0)
                {
                    this.assemblies.Remove(removeNames.Dequeue());
                }

                var newInfos = new List<AssemblyInfoEntry>();

                if (manifestExists || executeAnalyzer)
                {
                    foreach (var i in this.assemblies)
                    {
#pragma warning disable CA1031 // Do not catch general exception types
                        try
                        {
                            var lastWriteTime = System.IO.File.GetLastWriteTime(System.IO.Path.Combine(this.assembliesDirectory, i.Value.FileName));

                            if (i.Value.Info.LastWriteTime != lastWriteTime)
                            {
                                removeNames.Enqueue(i.Key);

                                if (executeAnalyzer)
                                {
                                    files.Add(i.Value.FileName);
                                }
                                else
                                {
                                    i.Value.Info.LastWriteTime = lastWriteTime;
                                    newInfos.Add(i.Value);
                                }
                            }
                        }
                        catch
                        {
                            if (executeAnalyzer)
                            {
                                files.Add(i.Value.FileName);
                            }

                            removeNames.Enqueue(i.Key);
                        }
#pragma warning restore CA1031 // Do not catch general exception types
                    }

                    while (removeNames.Count > 0)
                    {
                        this.assemblies.Remove(removeNames.Dequeue());
                    }
                }

                string tempDirectory = null;

                if (files.Count > 0)
                {
                    try
                    {
                        tempDirectory = System.IO.Path.Combine(
                            System.IO.Path.GetTempPath(),
                            System.IO.Path.GetFileNameWithoutExtension(System.IO.Path.GetRandomFileName()));

                        System.IO.Directory.CreateDirectory(tempDirectory);

                        var copyFiles = new string[files.Count];

                        for (var i = 0; i < files.Count; i++)
                        {
                            var destFileName = System.IO.Path.Combine(tempDirectory, files[i]);
                            System.IO.File.Copy(System.IO.Path.Combine(this.assembliesDirectory, files[i]), destFileName);
                            copyFiles[i] = destFileName;
                        }

                        for (var i = 0; i < files.Count; i++)
                        {
                            this.OnAnalysisAssemblyProgressChanged(("Analyzing " + files[i] + " ...").GetFormatter(), 100 * i / files.Count);

#pragma warning disable CA1031 // Do not catch general exception types
                            try
                            {
                                var data = this.assemblyAnalyzer.AnalyzeRootAssembly(copyFiles[i], false);
                                newInfos.Add(new AssemblyInfoEntry
                                {
                                    FileName = files[i],
                                    Info = ConvertAssemblyInfo(data, System.IO.Path.Combine(this.assembliesDirectory, files[i])),
                                });
                            }
                            catch
                            {
                                // ignore
                            }
#pragma warning restore CA1031 // Do not catch general exception types
                        }
                    }
                    finally
                    {
                        if (tempDirectory != null)
                        {
                            var files2 = System.IO.Directory.GetFiles(tempDirectory);

                            foreach (var i in files2)
                            {
                                try
                                {
                                    System.IO.File.Delete(i);
                                }
                                catch
                                {
                                    // ignore
                                }
                            }

                            try
                            {
                                System.IO.Directory.Delete(tempDirectory, true);
                            }
                            catch
                            {
                                // ignore
                            }
                        }
                    }
                }

                foreach (var i in newInfos)
                {
                    this.assemblies.Add(i.Info.Name, i);
                }

                if (newInfos.Count > 0)
                {
                    this.SaveManifest();
                }
            }

            if (isInitializing)
            {
                this.OnLoaded();
            }
        }

        public bool TryGet(string assemblyName, out AssemblyInfo assemblyInfo, out Exception exception)
        {
            try
            {
                this.CheckInitialize();

                AssemblyInfoEntry entry = null;
                if (this.assemblies.TryGetValue(assemblyName, out entry))
                {
                    assemblyInfo = entry.Info;
                    exception = null;
                    return true;
                }

                assemblyInfo = null;
                exception = null;
                return false;
            }
            catch (Exception ex)
            {
                assemblyInfo = null;
                exception = ex;
                return false;
            }
        }

        public bool Contains(string assemblyName)
        {
            this.CheckInitialize();

            return this.assemblies.ContainsKey(assemblyName);
        }

        public IEnumerator<AssemblyInfo> GetEnumerator()
        {
            this.CheckInitialize();
            return this.assemblies.Values.Select(f => f.Info).ToList().GetEnumerator();
        }

        System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
        {
            this.CheckInitialize();
            return this.assemblies.Values.Select(f => f.Info).ToList().GetEnumerator();
        }
    }
}
