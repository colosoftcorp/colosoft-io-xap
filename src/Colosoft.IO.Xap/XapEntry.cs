using System;
using System.IO;

namespace Colosoft.IO.Xap
{
    public sealed class XapEntry : IDisposable
    {
        private Lazy<Stream> stream;

        public string Name { get; private set; }

        public Stream Stream
        {
            get { return this.stream.Value; }
        }

        public DateTime LastWriteTime { get; private set; }

        public XapEntry(string name, Lazy<Stream> stream, DateTime lastWriteTime)
        {
            this.Name = name ?? throw new ArgumentNullException(nameof(name));
            this.stream = stream ?? throw new ArgumentNullException(nameof(stream));
            this.LastWriteTime = lastWriteTime;
        }

        public void Dispose()
        {
            if (this.stream != null && this.stream.IsValueCreated)
            {
                this.stream.Value.Dispose();
                this.stream = null;
            }
        }
    }
}
