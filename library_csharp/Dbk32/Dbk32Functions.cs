using CheatEngine.Library;
using Microsoft.Win32.SafeHandles;

namespace CheatEngine.Library.Dbk32;

public static class Dbk32Functions
{
    public static bool IsDriverLoaded(string driverPath)
    {
        return System.IO.File.Exists(driverPath);
    }

    public static SafeFileHandle OpenProcess(uint access, bool inheritHandle, uint processId)
    {
        return NewKernelHandler.OpenProcessHandle(processId, (access & 0x0020U) != 0 || (access & 0x0008U) != 0);
    }
}