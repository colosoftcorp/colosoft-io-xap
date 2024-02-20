using System;

namespace Colosoft.IO.Xap
{
    internal class StreamResourceInfo
    {
        public string ContentType { get; }

        public System.IO.Stream Stream { get; }

        public StreamResourceInfo(System.IO.Stream stream, string contentType)
        {
            if (stream is null)
            {
                throw new ArgumentNullException(nameof(stream));
            }

            this.ContentType = contentType;
            this.Stream = stream;
        }
    }
}
