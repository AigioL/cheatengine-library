using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;

namespace CheatEngine.Library;

public static class DebugHelper
{
    public static string FormatAddress(nuint address, int digits = 8)
    {
        return address.ToString($"X{digits}", CultureInfo.InvariantCulture);
    }

    public static IReadOnlyList<int> EnumerateThreadIds(int processId)
    {
        using var process = Process.GetProcessById(processId);
        return process.Threads.Cast<ProcessThread>().Select(thread => thread.Id).OrderBy(id => id).ToArray();
    }

    public static bool IsDebuggerAttached()
    {
        return Debugger.IsAttached;
    }

    public static void WriteTrace(string message)
    {
        Debug.WriteLine(message);
        Trace.WriteLine(message);
    }
}