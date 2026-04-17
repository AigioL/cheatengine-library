using System;
using System.IO;
using System.Runtime.InteropServices;

namespace CheatEngine.Library;

public static class Dbk64SecondaryLoader
{
    public static nint TryLoad(string libraryPath)
    {
        if (!File.Exists(libraryPath))
        {
            return 0;
        }

        try
        {
            return NativeLibrary.Load(libraryPath);
        }
        catch
        {
            return 0;
        }
    }
}