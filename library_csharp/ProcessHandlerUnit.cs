using System;

namespace CheatEngine.Library;

public enum SystemArchitecture
{
    X86 = 0,
    Arm = 1,
}

public sealed class ProcessHandler
{
    private bool _is64Bit;
    private nint _processHandle;
    private int _pointerSize;
    private readonly SystemArchitecture _systemArchitecture = SystemArchitecture.X86;

    public uint ProcessId { get; set; }

    public bool Is64Bit => _is64Bit;

    public int PointerSize => _pointerSize;

    public nint ProcessHandle
    {
        get => _processHandle;
        set => SetProcessHandle(value);
    }

    public SystemArchitecture SystemArchitecture => _systemArchitecture;

    public void Open()
    {
    }

    private void SetIs64Bit(bool state)
    {
        _is64Bit = state;
        _pointerSize = state ? 8 : 4;
    }

    private void SetProcessHandle(nint processHandle)
    {
        _processHandle = processHandle;
        SetIs64Bit(NewKernelHandler.Is64BitProcess(processHandle));
    }
}

public static class ProcessHandlerUnit
{
    public static ProcessHandler Current { get; } = new();
}