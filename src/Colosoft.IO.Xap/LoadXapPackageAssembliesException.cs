using System;
using System.Linq;

namespace Colosoft.IO.Xap
{
    [Serializable]
    public partial class LoadXapPackageAssembliesException : XapPackageException
    {
#pragma warning disable CA1819 // Properties should not return arrays
        public AssemblyLoadError[] Errors { get; }
#pragma warning restore CA1819 // Properties should not return arrays

        public LoadXapPackageAssembliesException(string message, AssemblyLoadError[] errors)
            : base(message, new AggregateException(message, errors.Select(f => f.Error).ToArray()))
        {
            this.Errors = errors;
        }

        public LoadXapPackageAssembliesException()
        {
        }

        public LoadXapPackageAssembliesException(string message)
            : base(message)
        {
        }

        public LoadXapPackageAssembliesException(string message, Exception innerException)
            : base(message, innerException)
        {
        }

        protected LoadXapPackageAssembliesException(System.Runtime.Serialization.SerializationInfo serializationInfo, System.Runtime.Serialization.StreamingContext streamingContext)
            : base(serializationInfo, streamingContext)
        {
        }
    }
}
