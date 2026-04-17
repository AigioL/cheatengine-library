using System;

namespace CheatEngine.Library;

public sealed class ScanState
{
    public MemScan MemScan { get; init; } = new();

    public FoundList FoundList { get; set; } = null!;

    public AddressList AddressList { get; init; } = new();

    public VariableType VariableType { get; set; } = VariableType.Dword;

    public CustomType? CustomType { get; set; }

    public string ScanValue1 { get; set; } = string.Empty;

    public string ScanValue2 { get; set; } = string.Empty;

    public ScanOption ScanOption { get; set; } = ScanOption.ExactValue;

    public bool Hexadecimal { get; set; }

    public bool Unicode { get; set; }

    public nuint StartAddress { get; set; }

    public nuint StopAddress { get; set; }
}

public sealed class InteractiveScanner
{
    public InteractiveScanner(nint handle)
        : this(handle, null)
    {
    }

    public InteractiveScanner(nint handle, object? listView)
    {
        Handle = handle;
        State = new ScanState();
        State.FoundList = new FoundList(listView, State.MemScan);
    }

    public nint Handle { get; }

    public ScanState State { get; }

    public void FirstScan()
    {
        State.MemScan.FirstScan(State.ScanOption, State.VariableType, RoundingType.Rounded, State.ScanValue1, State.ScanValue2, State.StartAddress, State.StopAddress, State.Hexadecimal, false, State.Unicode, false);
        State.FoundList.Initialize(State.VariableType, customType: State.CustomType);
    }

    public void NextScan()
    {
        State.MemScan.NextScan(State.ScanOption, RoundingType.Rounded, State.ScanValue1, State.ScanValue2, State.Hexadecimal, false, State.Unicode, false, false, false, string.Empty);
        State.FoundList.Reinitialize();
    }

    public MemoryRecord AddSelectedAddress(ulong resultIndex, string description = "")
    {
        var record = State.AddressList.CreateMemoryRecord();
        record.Description = description;
        record.VarType = State.VariableType;
        record.CustomType = State.CustomType;
        record.InterpretableAddress = State.FoundList.GetAddress(resultIndex).ToString();
        return record;
    }
}