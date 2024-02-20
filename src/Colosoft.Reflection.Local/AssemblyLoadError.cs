using System;

namespace Colosoft.Reflection.Local
{
    public class AssemblyLoadError
    {
        public AssemblyPart AssemblyPart { get; set; }

        public Exception Error { get; set; }

        public AssemblyLoadError(AssemblyPart assemblyPart, Exception error)
        {
            this.AssemblyPart = assemblyPart;
            this.Error = error;
        }
    }
}
