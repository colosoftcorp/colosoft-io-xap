using System;

namespace Colosoft.IO.Xap
{
    public class AssemblyLoadError
    {
        public Reflection.AssemblyPart AssemblyPart { get; set; }

        public Exception Error { get; set; }

        public AssemblyLoadError(Reflection.AssemblyPart assemblyPart, Exception error)
        {
            this.AssemblyPart = assemblyPart;
            this.Error = error;
        }
    }
}
