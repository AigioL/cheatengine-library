using CheatEngine.Library;
using System.Runtime.Intrinsics.X86;

namespace CheatEngine.Library.Dbk32;

public static class VmxFunctions
{
    public static bool IsVirtualizationCapable()
    {
        return X86Base.IsSupported || X86Base.X64.IsSupported;
    }
}