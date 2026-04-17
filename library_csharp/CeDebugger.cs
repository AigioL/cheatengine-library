using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

namespace CheatEngine.Library;

public sealed class DebugThreadEntry
{
    public nint ThreadHandle { get; init; }

    public nuint Address { get; init; }
}

public sealed class CeDebuggerSession
{
    private Process? _process;

    public int? ProcessId => _process?.Id;

    public bool Running => _process is { HasExited: false };

    public bool Attach(int processId)
    {
        _process = Process.GetProcessById(processId);
        return !_process.HasExited;
    }

    public void Detach()
    {
        _process?.Dispose();
        _process = null;
    }

    public IReadOnlyList<DebugThreadEntry> GetThreads()
    {
        if (_process is null)
        {
            return [];
        }

        return _process.Threads.Cast<ProcessThread>()
            .Select(thread => new DebugThreadEntry { ThreadHandle = thread.Id, Address = 0 })
            .ToArray();
    }
}

public static class CeDebugger
{
    public static CeDebuggerSession Session { get; } = new();

    public static bool StartDebuggerIfNeeded(int processId)
    {
        return Session.Attach(processId);
    }

    public static void StopDebugger()
    {
        Session.Detach();
    }
}