using System.Collections.Generic;

namespace CheatEngine.Library;

public sealed class PeInfo
{
    public PeInfo(IEnumerable<string>? moduleList = null)
    {
        Modules = moduleList is null ? [] : [.. moduleList];
    }

    public List<string> Modules { get; }

    public PeImageInfo? CurrentImage { get; private set; }

    public void SetModule(string modulePath)
    {
        CurrentImage = PeInfoFunctions.ReadImageInfo(modulePath);
    }

    public void ParsePe(string modulePath)
    {
        SetModule(modulePath);
    }
}