using System;
using System.Collections.Generic;
using System.Linq;

namespace Colosoft.Reflection.Local
{
    public class AssemblyRepository : IAssemblyRepository
    {
        private readonly List<AssemblyPackageCacheEntry> packages = new List<AssemblyPackageCacheEntry>();
        private readonly object objLock = new object();
        private readonly object downloaderLock = new object();
        private readonly IAssemblyPackageValidator validator;
        private readonly IAssemblyInfoRepository assemblyInfoRepository;
        private readonly string repositoryDirectory;
        private readonly string[] assemblyFilesDirectories;

        private readonly IEnumerable<IAssemblyRepositoryMaintenance> maintenanceInstances = Array.Empty<IAssemblyRepositoryMaintenance>();
        private AssemblyResolverManager assemblyResolverManager;

        private IAssemblyPackageDownloader downloader;
        private bool isStarted;
        private bool isStarting;
        private System.Threading.Thread startThread;

        public event AssemblyRepositoryStartedHandler Started;

        public bool UseDirectoryAssemblyPackages { get; set; }

        public bool IsStarted => this.isStarted;

        public AssemblyResolverManager AssemblyResolverManager
        {
            get { return this.assemblyResolverManager; }
        }

        [System.Security.Permissions.SecurityPermission(System.Security.Permissions.SecurityAction.LinkDemand, ControlAppDomain = true)]
        public AssemblyRepository()
            : this(
                  null,
                  Array.Empty<string>(),
#pragma warning disable CA2000 // Dispose objects before losing scope
                  new AssemblyResolverManager(AppDomain.CurrentDomain),
#pragma warning restore CA2000 // Dispose objects before losing scope
                  null,
                  null)
        {
        }

        public AssemblyRepository(
            string repositoryDirectory,
            string[] assemblyFilesDirectories,
            AssemblyResolverManager assemblyResolverManager,
            IAssemblyPackageDownloader downloader,
            IAssemblyPackageValidator validator)
            : this(repositoryDirectory, assemblyFilesDirectories, assemblyResolverManager, downloader, validator, null, Enumerable.Empty<IAssemblyRepositoryMaintenance>())
        {
        }

        public AssemblyRepository(
            string repositoryDirectory,
            string[] assemblyFilesDirectories,
            AssemblyResolverManager assemblyResolverManager,
            IAssemblyPackageDownloader downloader,
            IAssemblyPackageValidator validator,
            IAssemblyInfoRepository assemblyInfoRepository,
            IEnumerable<IAssemblyRepositoryMaintenance> maintenanceInstances)
        {
            if (string.IsNullOrEmpty(repositoryDirectory))
            {
                throw new ArgumentException($"'{nameof(repositoryDirectory)}' cannot be null or empty.", nameof(repositoryDirectory));
            }

            if (!System.IO.Directory.Exists(repositoryDirectory))
            {
                System.IO.Directory.CreateDirectory(repositoryDirectory);
            }

            this.assemblyFilesDirectories = assemblyFilesDirectories ?? Array.Empty<string>();
            this.repositoryDirectory = repositoryDirectory;
            this.assemblyResolverManager = assemblyResolverManager;
            this.downloader = downloader;
            this.validator = validator;
            this.assemblyInfoRepository = assemblyInfoRepository;
            this.maintenanceInstances = maintenanceInstances;

            if (!System.IO.Directory.Exists(this.GetRepositoryFolder()))
            {
                System.IO.Directory.CreateDirectory(this.GetRepositoryFolder());
            }
        }

        private void DoStart()
        {
            try
            {
                var exceptions = new List<Exception>();

                try
                {
                    var repositoryFolder = this.GetRepositoryFolder();

                    var repositoryDirectories = System.IO.Directory.GetDirectories(repositoryFolder);
                    var files = System.IO.Directory.GetFiles(repositoryFolder, "*.xap");

                    var packageDirectories = files.Select(f =>
                        System.IO.Path.Combine(repositoryFolder, System.IO.Path.GetFileNameWithoutExtension(f))).ToArray();

                    foreach (var invalidDirectory in repositoryDirectories.Except(packageDirectories, StringComparer.InvariantCultureIgnoreCase))
                    {
                        try
                        {
                            System.IO.Directory.Delete(invalidDirectory, true);
                        }
                        catch
                        {
                            if (System.IO.Directory.Exists(invalidDirectory))
                            {
                                foreach (var file in System.IO.Directory.GetFiles(invalidDirectory))
                                {
                                    try
                                    {
                                        System.IO.File.Delete(file);
                                    }
                                    catch
                                    {
                                        // ignore
                                    }
                                }
                            }
                        }
                    }

                    var loadedPackages = new List<AssemblyPackageCacheEntry>(files.Length);

                    for (var i = 0; i < files.Length; i++)
                    {
                        var file = files[i];

                        try
                        {
                            var uid = Guid.Parse(System.IO.Path.GetFileNameWithoutExtension(file));
#pragma warning disable CA2000 // Dispose objects before losing scope
                            var pkg = new AssemblyPackageResult(this.assemblyResolverManager, uid, file).CreatePackage();
#pragma warning restore CA2000 // Dispose objects before losing scope
                            loadedPackages.Add(new AssemblyPackageCacheEntry(pkg, null, file));
                        }
                        catch (Exception ex)
                        {
                            exceptions.Add(ex);
                        }
                    }

                    if (this.validator != null)
                    {
                        var validateResult = this.validator.Validate(loadedPackages.Select(f => f.Package).ToArray());

                        lock (this.objLock)
                        {
                            for (var i = 0; i < loadedPackages.Count; i++)
                            {
                                if (validateResult[i])
                                {
                                    this.packages.Add(loadedPackages[i]);
                                }
                                else
                                {
                                    try
                                    {
                                        loadedPackages[i].Dispose();

                                        var packageDirectory =
                                            System.IO.Path.Combine(
                                                System.IO.Path.GetDirectoryName(files[i]),
                                                System.IO.Path.GetFileNameWithoutExtension(files[i]));

                                        if (System.IO.Directory.Exists(packageDirectory))
                                        {
                                            try
                                            {
                                                System.IO.Directory.Delete(packageDirectory, true);
                                            }
                                            catch
                                            {
                                                // ignore
                                            }
                                        }

                                        System.IO.File.Delete(files[i]);
                                    }
                                    catch
                                    {
                                        // ignore
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        lock (this.objLock)
                        {
                            foreach (var i in loadedPackages)
                            {
                                this.packages.Add(i);
                            }
                        }
                    }
                }
                catch (System.Threading.ThreadAbortException)
                {
                    return;
                }
                catch (Exception ex)
                {
                    exceptions.Add(ex);
                }
                finally
                {
                    this.OnStartedInternal(new AssemblyRepositoryStartedArgs(exceptions.ToArray()));
                }
            }
            catch (System.Threading.ThreadAbortException)
            {
                // ignore
            }
        }

        private string GetAssemblyPackageLocalFileName(Guid uid)
        {
            return System.IO.Path.Combine(this.GetRepositoryFolder(), $"{uid}.xap");
        }

        private List<Tuple<AssemblyPart, AssemblyPackageCacheEntry>> GetAssemblyPackagesFromCache(IEnumerable<AssemblyPart> assemblyParts)
        {
            while (this.isStarting)
            {
                System.Threading.Thread.Sleep(500);
            }

            lock (this.objLock)
            {
                var packagesToValidate = new List<AssemblyPackageCacheEntry>();
                var result = new List<Tuple<AssemblyPart, AssemblyPackageCacheEntry>>();

                foreach (var assemblyPart in assemblyParts)
                {
                    var found = false;

                    for (var i = 0; i < this.packages.Count; i++)
                    {
                        var package = this.packages[i];

                        using (var enumerator = package.GetEnumerator())
                        {
                            while (!found && enumerator.MoveNext())
                            {
                                found = AssemblyPartEqualityComparer.Instance.Equals(enumerator.Current, assemblyPart);
                                if (found)
                                {
                                    break;
                                }
                            }
                        }

                        if (found)
                        {
                            packagesToValidate.Add(package);
                            result.Add(new Tuple<AssemblyPart, AssemblyPackageCacheEntry>(assemblyPart, package));
                            break;
                        }
                    }

                    if (!found)
                    {
                        result.Add(new Tuple<AssemblyPart, AssemblyPackageCacheEntry>(assemblyPart, null));
                    }
                }

                for (var i = 0; i < packagesToValidate.Count; i++)
                {
                    var package = packagesToValidate[i];

                    if (package.Package != null)
                    {
                        bool isValid = false;

                        try
                        {
                            isValid = package.IsValid(this);
                        }
                        catch (Exception ex)
                        {
                            throw new InvalidCastException(
                                ResourceMessageFormatter.Create(() => Properties.Resources.AssemblyRepository_ValidateAssemblyPackageCacheEntryError).Format(),
                                ex);
                        }

                        if (!isValid)
                        {
                            for (var j = 0; j < result.Count; j++)
                            {
                                if (result[j].Item2 == package)
                                {
                                    result[j] = new Tuple<AssemblyPart, AssemblyPackageCacheEntry>(result[j].Item1, null);
                                }
                            }

                            packagesToValidate.RemoveAt(i--);
                            this.packages.Remove(package);

                            package.Destroy();
                        }
                    }
                }

                if (this.validator != null)
                {
                    bool[] validateResult = null;

                    try
                    {
                        // Valida os pacotes
                        validateResult = this.validator.Validate(packagesToValidate.Select(f => f.Package).ToArray());
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            ResourceMessageFormatter.Create(() => Properties.Resources.AssemblyRepository_ValidateAssemblyPackageError).Format(),
                            ex);
                    }

                    for (var i = 0; i < packagesToValidate.Count; i++)
                    {
                        if (!validateResult[i])
                        {
                            var package = packagesToValidate[i];

                            for (var j = 0; j < result.Count; j++)
                            {
                                if (result[j].Item2 == package)
                                {
                                    result[j] = new Tuple<AssemblyPart, AssemblyPackageCacheEntry>(result[j].Item1, null);
                                }
                            }

                            this.packages.Remove(package);

                            package.Destroy();
                        }
                    }
                }

                return result;
            }
        }

        private void Downloader_DownloadCompleted(object sender, Net.DownloadCompletedEventArgs e)
        {
            var e2 = (AssemblyPackageDownloadCompletedEventArgs)e;
            var state = (DownloaderState)e.UserState;

            if (state == null)
            {
                return;
            }

            if (e2 == null)
            {
                if (state != null)
                {
                    state.Release();
                }

                return;
            }

            var loadedPackages = new List<AssemblyPackageCacheEntry>();
            Exception exception = null;

            try
            {
                var buffer = new byte[1024];
                var read = 0;

                if (e.Error == null && e2.PackagesResult != null)
                {
                    try
                    {
                        foreach (var i in e2.PackagesResult)
                        {
                            var packageFile = new System.IO.FileInfo(this.GetAssemblyPackageLocalFileName(i.Uid));

                            if (packageFile.Exists)
                            {
                                packageFile.Delete();
                            }

                            var inStream = i.Stream;

                            using (var outStream = packageFile.Create())
                            {
                                while ((read = inStream.Read(buffer, 0, buffer.Length)) > 0)
                                {
                                    outStream.Write(buffer, 0, read);
                                }
                            }

                            packageFile.LastAccessTime = i.LastWriteTime;

#pragma warning disable CA2000 // Dispose objects before losing scope
                            var pkg = new AssemblyPackageResult(this.assemblyResolverManager, i.Uid, packageFile.FullName)
                                        .CreatePackage();
#pragma warning restore CA2000 // Dispose objects before losing scope

                            if (pkg.Count > 0)
                            {
                                loadedPackages.Add(new AssemblyPackageCacheEntry(pkg, null, packageFile.FullName));
                            }
                            else
                            {
                                pkg.Dispose();
                                packageFile.Delete();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        exception = ex;
                    }
                }
                else if (e.Error != null)
                {
                    exception = e.Error;
                }

                if (this.packages != null)
                {
                    lock (this.objLock)
                    {
                        this.packages.AddRange(loadedPackages);
                    }
                }
            }
            catch (Exception ex)
            {
                exception = ex;
            }
            finally
            {
                state.DownloadEntries = loadedPackages;
                state.Exception = exception;
                state.Release();
            }
        }

        private AssemblyPackageContainer GetAssemblyPackageFromLocal(IEnumerable<AssemblyPart> assemblyParts)
        {
            var assemblyNames = new List<string>();
            List<string> files = new List<string>();

            foreach (var dir in this.assemblyFilesDirectories)
            {
                try
                {
                    foreach (var file in
                        System.IO.Directory.GetFiles(dir)
                                           .Select(f => System.IO.Path.GetFileName(f))
                                           .OrderBy(f => f))
                    {
                        var index = files.BinarySearch(file, StringComparer.OrdinalIgnoreCase);

                        if (index < 0)
                        {
                            files.Insert(~index, file);
                        }
                    }
                }
                catch (Exception ex)
                {
                    throw new InvalidOperationException(
                        ResourceMessageFormatter.Create(() => Properties.Resources.AssemblyRepository_GetFilesFromRepositoryDirectoryError, dir).Format(),
                        ex);
                }
            }

            var parts2 = assemblyParts.ToArray();

            IComparer<string> comparer = StringComparer.Create(System.Globalization.CultureInfo.InvariantCulture, true);
            foreach (var part in parts2)
            {
                var index = files.BinarySearch(part.Source, comparer);

                if (index >= 0)
                {
                    assemblyNames.Add(files[index]);
                }
            }

            var analyzer = new AssemblyAnalyzer();

            var assemblyPaths = new List<string>();

            var names = new List<string>();

            if (this.assemblyInfoRepository != null)
            {
                AssemblyInfo info = null;
                Exception exception = null;

                var pending = new Queue<string>(assemblyNames);

                while (pending.Count > 0)
                {
                    var assemblyName = pending.Dequeue();

                    if (names.FindIndex(f => StringComparer.InvariantCultureIgnoreCase.Equals(f, assemblyName)) < 0)
                    {
                        if (!this.assemblyInfoRepository.TryGet(System.IO.Path.GetFileNameWithoutExtension(assemblyName), out info, out exception))
                        {
                            continue;
                        }

                        foreach (var i in info.References)
                        {
                            pending.Enqueue(i + ".dll");
                        }

                        names.Add(assemblyName);
                        assemblyPaths.AddRange(
                            this.assemblyFilesDirectories
                                .Where(dir => System.IO.File.Exists(System.IO.Path.Combine(dir, assemblyName)))
                                .Select(dir => System.IO.Path.Combine(dir, assemblyName)));
                    }
                }
            }
            else
            {
                foreach (var assemblyName in assemblyNames)
                {
                    string assemblyPath = null;

                    foreach (var dir in this.assemblyFilesDirectories)
                    {
                        assemblyPath = System.IO.Path.Combine(dir, assemblyName);
                        if (System.IO.File.Exists(assemblyPath))
                        {
                            break;
                        }
                    }

                    AsmData data = null;

                    try
                    {
                        data = analyzer.AnalyzeRootAssembly(assemblyPath);
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            ResourceMessageFormatter.Create(() => Properties.Resources.AssemblyRepository_AnalyzeAssemblyError, assemblyName).Format(),
                            ex);
                    }

                    var queue = new Queue<AsmData>();
                    queue.Enqueue(data);

                    while (queue.Count > 0)
                    {
                        data = queue.Dequeue();

                        if (string.IsNullOrEmpty(data.Path))
                        {
                            continue;
                        }

                        var fileName = System.IO.Path.GetFileName(data.Path);

                        var index = names.FindIndex(f => StringComparer.InvariantCultureIgnoreCase.Equals(f, fileName));

                        if (index < 0)
                        {
                            names.Add(fileName);
                            assemblyPaths.Add(data.Path);
                        }

                        foreach (var i in data.References)
                        {
                            if (!string.IsNullOrEmpty(i.Path) && names.FindIndex(f => f == System.IO.Path.GetFileName(i.Path)) < 0)
                            {
                                queue.Enqueue(i);
                            }
                        }
                    }
                }
            }

            names.Reverse();

            var languages = new IO.Xap.LanguageInfo[]
            {
                new IO.Xap.LanguageInfo(new string[] { ".dll" }, names.ToArray(), string.Empty),
            };

            var configuration = new IO.Xap.XapConfiguration(new IO.Xap.AppManifestTemplate(), languages, null);

            if (assemblyPaths.Count > 0 && this.UseDirectoryAssemblyPackages)
            {
                return new AssemblyPackageContainer(new DirectoryAssemblyPackage(this.assemblyResolverManager, assemblyPaths));
            }
            else if (assemblyPaths.Count > 0)
            {
                assemblyPaths.Reverse();
                var entries = assemblyPaths.Select(f =>
                {
                    var fileInfo = new System.IO.FileInfo(f);
                    return new IO.Xap.XapEntry(fileInfo.Name, new Lazy<System.IO.Stream>(() => fileInfo.OpenRead()), fileInfo.LastWriteTime);
                }).ToArray();

                var pkgUid = Guid.NewGuid();

                try
                {
                    var fileName = this.GetAssemblyPackageLocalFileName(pkgUid);

                    if (System.IO.File.Exists(fileName))
                    {
                        System.IO.File.Delete(fileName);
                    }

                    IO.Xap.XapBuilder.XapToDisk(configuration, entries, fileName);

                    var pkg = new AssemblyPackageResult(this.assemblyResolverManager, pkgUid, fileName).CreatePackage();

                    lock (this.objLock)
                    {
                        this.packages.Add(new AssemblyPackageCacheEntry(pkg, null, fileName));
                    }

                    return new AssemblyPackageContainer(pkg);
                }
                finally
                {
                    foreach (var i in entries)
                    {
                        i.Dispose();
                    }
                }
            }
            else
            {
                lock (this.objLock)
                {
                    this.packages.Add(new AssemblyPackageCacheEntry(null, parts2, null));
                }

                return new AssemblyPackageContainer(Enumerable.Empty<IAssemblyPackage>());
            }
        }

        private void OnStartedInternal(AssemblyRepositoryStartedArgs e)
        {
            this.isStarting = false;
            this.isStarted = true;
            this.Started?.Invoke(this, e);

            this.OnStarted(e);
        }

        protected virtual void OnStarted(AssemblyRepositoryStartedArgs e)
        {
        }

        public string GetRepositoryFolder()
        {
            return this.repositoryDirectory;
        }

        public void Start()
        {
            if (!this.isStarted && this.startThread == null)
            {
                this.isStarting = true;
                this.startThread = new System.Threading.Thread(this.DoStart);
                this.startThread.Start();
            }
        }

        [System.Security.SecuritySafeCritical]
        public void Add(Guid uid, System.IO.Stream inputStream)
        {
            if (inputStream is null)
            {
                throw new ArgumentNullException(nameof(inputStream));
            }

            var packageFileName = System.IO.Path.Combine(this.GetRepositoryFolder(), $"{uid}.xap");

            if (System.IO.File.Exists(packageFileName))
            {
                System.IO.File.Delete(packageFileName);
            }

            var buffer = new byte[1024];
            var read = 0;

            using (var outputStream = System.IO.File.OpenWrite(packageFileName))
            {
                while ((read = inputStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    outputStream.Write(buffer, 0, read);
                }
            }
        }

        public System.IO.Stream GetAssemblyPackageStream(IAssemblyPackage package)
        {
            if (package is null)
            {
                throw new ArgumentNullException(nameof(package));
            }

            var fileName = this.GetAssemblyPackageLocalFileName(package.Uid);

            if (System.IO.File.Exists(fileName))
            {
                return System.IO.File.OpenRead(fileName);
            }

            return null;
        }

        private void DoGetAssemblyPackages(object callState)
        {
            var arguments = (object[])callState;
            var asyncResult = (Threading.AsyncResult<AssemblyPackageContainer>)arguments[0];
            var assemblyParts = (IEnumerable<AssemblyPart>)arguments[1];

            AssemblyPackageContainer packageContainer = null;

            try
            {
                assemblyParts = assemblyParts.Distinct(AssemblyPartEqualityComparer.Instance);

                var result = new List<IAssemblyPackage>();

                var assemblyPartsPackage = this.GetAssemblyPackagesFromCache(assemblyParts);

                var assemblyParts2 = new List<AssemblyPart>();
                foreach (var package in assemblyPartsPackage.GroupBy(f => f.Item2))
                {
                    if (package.Key != null)
                    {
                        result.Add(package.Key.Package);
                    }
                    else
                    {
                        assemblyParts2.AddRange(package.Select(f => f.Item1));
                    }
                }

                if (assemblyParts2.Count == 0)
                {
                    packageContainer = new AssemblyPackageContainer(result);
                }
                else
                {
                    if (this.assemblyFilesDirectories.Length > 0)
                    {
                        var container = this.GetAssemblyPackageFromLocal(assemblyParts2);

                        if (container != null)
                        {
                            result.AddRange(container);
                        }
                    }
                    else if (this.downloader != null)
                    {
                        DownloaderState state = null;

                        lock (this.downloaderLock)
                        {
                            var start = DateTime.Now.AddSeconds(60);

                            while (this.downloader.IsBusy && start > DateTime.Now)
                            {
                                System.Threading.Thread.Sleep(200);
                            }

                            if (!this.downloader.IsBusy)
                            {
#pragma warning disable CA2000 // Dispose objects before losing scope
                                this.downloader.Add(new AssemblyPackage(assemblyParts2));
#pragma warning restore CA2000 // Dispose objects before losing scope

                                state = new DownloaderState(this, result, asyncResult);

                                this.downloader.DownloadCompleted += this.Downloader_DownloadCompleted;
                                this.downloader.RunAsync(state);
                            }
                        }

                        return;
                    }

                    packageContainer = new AssemblyPackageContainer(result.Distinct());
                }
            }
            catch (Exception ex3)
            {
                asyncResult.HandleException(ex3, false);
                return;
            }

            asyncResult.Complete(packageContainer, false);
        }

        public IAsyncResult BeginGetAssemblyPackages(
            IEnumerable<AssemblyPart> assemblyParts,
            AsyncCallback callback,
            object state)
        {
            var asyncResult = new Threading.AsyncResult<AssemblyPackageContainer>(callback, state);

            var arguments = new object[] { asyncResult, assemblyParts };

            if (!System.Threading.ThreadPool.QueueUserWorkItem(this.DoGetAssemblyPackages, arguments))
            {
                this.DoGetAssemblyPackages(arguments);
            }

            return asyncResult;
        }

        public AssemblyPackageContainer EndGetAssemblyPackages(IAsyncResult ar)
        {
            var asyncResult = (Threading.AsyncResult<AssemblyPackageContainer>)ar;

            if (asyncResult?.Exception != null)
            {
                throw asyncResult.Exception;
            }

            return asyncResult?.Result;
        }

        public AssemblyPackageContainer GetAssemblyPackages(IEnumerable<AssemblyPart> assemblyParts)
        {
            using (var allDone = new System.Threading.ManualResetEvent(false))
            {
                var asyncResult = new Threading.AsyncResult<AssemblyPackageContainer>(
                    (ar) =>
                    {
                        try
                        {
                            allDone.Set();
                        }
                        catch (System.IO.IOException)
                        {
                            try
                            {
                                allDone.Dispose();
                            }
                            catch
                            {
                                // ignore
                            }
                        }
                    },
                    null);

                var arguments = new object[] { asyncResult, assemblyParts };

                this.DoGetAssemblyPackages(arguments);

                try
                {
                    allDone.WaitOne();
                }
                catch (ObjectDisposedException)
                {
                    // ignore
                }

                if (asyncResult.Exception != null)
                {
                    throw asyncResult.Exception;
                }

                return asyncResult.Result;
            }
        }

        public IAssemblyPackage GetAssemblyPackage(Guid assemblyPackageUid)
        {
            AssemblyPackageCacheEntry item = null;

            lock (this.objLock)
            {
                item = this.packages.Find(f => f.Package != null && f.Package.Uid == assemblyPackageUid);
            }

            if (item != null)
            {
                return item.Package;
            }

#pragma warning disable S1168 // Empty arrays and collections should be returned instead of null
            return null;
#pragma warning restore S1168 // Empty arrays and collections should be returned instead of null
        }

        public AssemblyRepositoryValidateResult Validate()
        {
            var result = new List<AssemblyRepositoryValidateResult.Entry>();

            foreach (var instance in this.maintenanceInstances)
            {
                try
                {
                    var executeResult = instance.Execute();

                    if (executeResult.HasError)
                    {
                        result.Add(new AssemblyRepositoryValidateResult.Entry(
                            ResourceMessageFormatter.Create(
                                () => Properties.Resources.AssemblyRepository_ValidateMaintenanceError,
                                instance.Name),
                            AssemblyRepositoryValidateResult.EntryType.Error));

                        foreach (var i in executeResult.Where(f => f.Type == AssemblyRepositoryMaintenanceExecuteResult.EntryType.Error))
                        {
                            result.Add(new AssemblyRepositoryValidateResult.Entry(
                                i.Message,
                                AssemblyRepositoryValidateResult.EntryType.Error,
                                i.Error));
                        }
                    }
                }
                catch (Exception ex)
                {
                    result.Add(new AssemblyRepositoryValidateResult.Entry(
                        ResourceMessageFormatter.Create(
                            () => Properties.Resources.AssemblyRepository_MaintenanceError,
                            instance.Name,
                            ex.Message),
                        AssemblyRepositoryValidateResult.EntryType.Error,
                        ex));
                }
            }

            return new AssemblyRepositoryValidateResult(result);
        }

        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (this.downloader != null)
            {
                this.downloader.Dispose();
                this.downloader = null;
            }

            if (this.startThread != null)
            {
                try
                {
                    this.startThread.Abort();
                }
                catch
                {
                    // ignore
                }

                this.startThread = null;
            }

            if (this.assemblyResolverManager != null)
            {
                this.assemblyResolverManager.Dispose();
                this.assemblyResolverManager = null;
            }

            foreach (var i in this.packages)
            {
                i.Dispose();
            }

            this.packages.Clear();
        }

        private sealed class DownloaderState
        {
            public IEnumerable<IAssemblyPackage> Packages { get; }

            public IEnumerable<AssemblyPackageCacheEntry> DownloadEntries { get; set; }

            public Exception Exception { get; set; }

            public Threading.AsyncResult<AssemblyPackageContainer> AsyncResult { get; }

            public AssemblyRepository Owner { get; }

            public DownloaderState(
                AssemblyRepository owner,
                IEnumerable<IAssemblyPackage> packages,
                Threading.AsyncResult<AssemblyPackageContainer> ar)
            {
                this.Owner = owner;
                this.Packages = packages;
                this.AsyncResult = ar;
            }

            public void Release()
            {
                var result = new List<IAssemblyPackage>(this.Packages);

                this.Owner.downloader.DownloadCompleted -= this.Owner.Downloader_DownloadCompleted;

                if (this.Exception != null)
                {
                    this.AsyncResult.HandleException(this.Exception, false);
                    return;
                }

                if (this.DownloadEntries != null)
                {
                    result.AddRange(
                        this.DownloadEntries
                            .Where(package => !result.Exists(f => f.Uid == package.Package.Uid))
                            .Select(package => package.Package));
                }

                this.AsyncResult.Complete(new AssemblyPackageContainer(result.Distinct()), false);
            }
        }

        private sealed class AssemblyPackageCacheEntry : IEnumerable<AssemblyPart>, IDisposable
        {
            private readonly IEnumerable<AssemblyPart> parts;
            private readonly string fileName;
            private AssemblyPackage package;

            public AssemblyPackage Package => this.package;

            public IEnumerable<AssemblyPart> Parts => this.parts ?? this.package;

            public AssemblyPackageCacheEntry(AssemblyPackage package, IEnumerable<AssemblyPart> parts, string fileName)
            {
                this.package = package;
                this.parts = parts;
                this.fileName = fileName;
            }

            public bool IsValid(AssemblyRepository assemblyRepository)
            {
                var assembliesDirectories = assemblyRepository
                    .assemblyFilesDirectories
                    .Where(f => !string.IsNullOrEmpty(f) && System.IO.Directory.Exists(f))
                    .ToList();

                if (!string.IsNullOrEmpty(this.fileName) && assembliesDirectories.Count > 0)
                {
                    var fileInfo = new System.IO.FileInfo(this.fileName);

                    if (!fileInfo.Exists)
                    {
                        return false;
                    }

                    foreach (var i in this.Parts.Select(f => f.Source))
                    {
                        foreach (var directory in assembliesDirectories)
                        {
                            var partFileInfo = new System.IO.FileInfo(System.IO.Path.Combine(directory, i));

                            if (partFileInfo.Exists)
                            {
                                return partFileInfo.LastWriteTime == fileInfo.LastWriteTime;
                            }
                        }
                    }
                }
                else if (this.Package != null)
                {
                    return true;
                }

                return false;
            }

            public void Destroy()
            {
                if (!string.IsNullOrEmpty(this.fileName))
                {
                    try
                    {
                        System.IO.File.Delete(this.fileName);
                    }
                    catch
                    {
                        // ignore
                    }
                }
            }

            public IEnumerator<AssemblyPart> GetEnumerator()
            {
                return (this.parts ?? this.package).GetEnumerator();
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator()
            {
                return (this.parts ?? this.package).GetEnumerator();
            }

            public void Dispose()
            {
                if (this.package != null)
                {
                    this.package.Dispose();
                    this.package = null;
                }

                GC.SuppressFinalize(this);
            }
        }
    }
}
