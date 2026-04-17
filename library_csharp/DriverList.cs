using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CheatEngine.Library;

public static class DriverList
{
    public static IReadOnlyList<string> GetDriverList()
    {
        var driverDirectory = Path.Combine(Environment.SystemDirectory, "drivers");
        if (!Directory.Exists(driverDirectory))
        {
            return [];
        }

        return Directory
            .EnumerateFiles(driverDirectory, "*.sys", SearchOption.TopDirectoryOnly)
            .Select(Path.GetFileName)
            .Where(name => !string.IsNullOrEmpty(name))
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray()!;
    }

    public static void Fill(ICollection<string> target)
    {
        foreach (var driver in GetDriverList())
        {
            target.Add(driver);
        }
    }
}