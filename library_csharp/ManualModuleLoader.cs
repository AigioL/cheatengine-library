using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace CheatEngine.Library;

public sealed class ModuleLoader : IDisposable
{
    private nint _handle;

    public ModuleLoader(string filename)
    {
        Filename = filename;
        ExportTable = new List<string>();

        if (File.Exists(filename))
        {
            _handle = NativeLibrary.Load(filename);
            Loaded = _handle != 0;
            if (Loaded)
            {
                var info = PeInfoFunctions.ReadImageInfo(filename);
                EntryPoint = info.ImageBase + (ulong)info.EntryPoint;
                ExportTable.AddRange(info.Exports.Select(exportEntry => $"{exportEntry.Address:X} - {exportEntry.Name}"));
            }
        }
    }

    public string Filename { get; }

    public bool Loaded { get; }

    public ulong EntryPoint { get; }

    public List<string> ExportTable { get; }

    public void Dispose()
    {
        if (_handle != 0)
        {
            NativeLibrary.Free(_handle);
            _handle = 0;
        }
    }
}