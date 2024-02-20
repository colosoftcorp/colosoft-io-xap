using System;
using System.Runtime.Serialization;

namespace Colosoft.IO.Xap
{
    [Serializable]
    public class XapPackageException : Exception
    {
        public XapPackageException()
        {
        }

        public XapPackageException(string message)
            : base(message)
        {
        }

        public XapPackageException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected XapPackageException(SerializationInfo serializationInfo, StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }
    }
}
