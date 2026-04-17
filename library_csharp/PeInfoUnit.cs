using System.Collections.Generic;

namespace CheatEngine.Library;

public sealed class PeSectionInfo
{
    public string Name { get; set; } = string.Empty;

    public int VirtualAddress { get; set; }

    public int VirtualSize { get; set; }

    public int SizeOfRawData { get; set; }

    public int PointerToRawData { get; set; }

    public uint Characteristics { get; set; }
}

public sealed class PeImportEntry
{
    public string ModuleName { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}

public sealed class PeExportEntry
{
    public string Name { get; set; } = string.Empty;

    public uint Ordinal { get; set; }

    public ulong Address { get; set; }
}

public sealed class PeImageInfo
{
    public string FilePath { get; set; } = string.Empty;

    public bool Is64Bit { get; set; }

    public ulong ImageBase { get; set; }

    public int EntryPoint { get; set; }

    public int CodeBase { get; set; }

    public int CodeSize { get; set; }

    public int HeaderSize { get; set; }

    public List<PeSectionInfo> Sections { get; } = new();

    public List<PeImportEntry> Imports { get; } = new();

    public List<PeExportEntry> Exports { get; } = new();
}