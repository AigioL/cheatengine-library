using System;
using System.Diagnostics.CodeAnalysis;

namespace CheatEngine.Library;

public static class PascalPorting
{
    [DoesNotReturn]
    public static void NotSupported(string unitName, string? memberName = null)
    {
        throw new NotSupportedException(
            memberName is null
                ? $"The Pascal unit '{unitName}' has not been ported to C# yet."
                : $"The Pascal member '{unitName}.{memberName}' has not been ported to C# yet.");
    }
}