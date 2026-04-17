using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace CheatEngine.Library;

[Flags]
public enum MemoryRecordOptions
{
    None = 0,
    Frozen = 1 << 0,
    ShowAsHex = 1 << 1,
    ShowAsSigned = 1 << 2,
    HideChildren = 1 << 3,
    BindActivation = 1 << 4,
}

public enum MemoryRecordHotkeyAction
{
    ToggleActivation,
    Activate,
    Deactivate,
    SetValue,
    IncreaseValue,
    DecreaseValue,
}

public sealed class MemoryRecordHotkey : IDisposable
{
    private readonly GenericHotkey _hotkey;

    public MemoryRecordHotkey(MemoryRecord owner, KeyCombo keys, MemoryRecordHotkeyAction action, string? value = null, int delayBetweenActivate = 250)
    {
        Owner = owner;
        Keys = keys;
        Action = action;
        Value = value;
        _hotkey = new GenericHotkey(0, _ => Execute(), keys)
        {
            DelayBetweenActivate = delayBetweenActivate,
        };
    }

    public MemoryRecord Owner { get; }

    public KeyCombo Keys { get; }

    public MemoryRecordHotkeyAction Action { get; }

    public string? Value { get; set; }

    public void Execute()
    {
        switch (Action)
        {
            case MemoryRecordHotkeyAction.ToggleActivation:
                Owner.Active = !Owner.Active;
                break;
            case MemoryRecordHotkeyAction.Activate:
                Owner.Active = true;
                break;
            case MemoryRecordHotkeyAction.Deactivate:
                Owner.Active = false;
                break;
            case MemoryRecordHotkeyAction.SetValue:
                if (!string.IsNullOrWhiteSpace(Value))
                {
                    Owner.Value = Value;
                }

                break;
            case MemoryRecordHotkeyAction.IncreaseValue:
                Owner.AdjustNumericalValue(Value ?? "1", increase: true);
                break;
            case MemoryRecordHotkeyAction.DecreaseValue:
                Owner.AdjustNumericalValue(Value ?? "1", increase: false);
                break;
        }
    }

    public void Dispose()
    {
        _hotkey.Dispose();
    }
}

public sealed class AutoAssemblerData
{
    public string Script { get; set; } = string.Empty;

    public List<CeAlloc> Allocations { get; } = new();

    public List<string> RegisteredSymbols { get; } = new();
}

public sealed class MemoryRecord
{
    private readonly List<MemoryRecord> _children = new();
    private readonly List<MemoryRecordHotkey> _hotkeys = new();
    private string _interpretableAddress = "0";
    private bool _active;
    private bool _allowIncrease;

    public MemoryRecord(AddressList owner)
    {
        Owner = owner;
    }

    public event Action<MemoryRecord>? Changed;

    public event Func<MemoryRecord, bool, bool>? ActivationChanging;

    public event Action<MemoryRecord, bool>? ActivationChanged;

    public AddressList Owner { get; }

    public MemoryRecord? Parent { get; private set; }

    public IReadOnlyList<MemoryRecord> Children => _children;

    public IReadOnlyList<MemoryRecordHotkey> Hotkeys => _hotkeys;

    public int Id { get; set; }

    public string Description { get; set; } = string.Empty;

    public string InterpretableAddress
    {
        get => _interpretableAddress;
        set
        {
            _interpretableAddress = string.IsNullOrWhiteSpace(value) ? "0" : value.Trim();
            Changed?.Invoke(this);
        }
    }

    public VariableType VarType { get; set; } = VariableType.Dword;

    public CustomType? CustomType { get; set; }

    public int StringSize { get; set; } = 16;

    public bool Unicode { get; set; }

    public bool Hexadecimal
    {
        get => Options.HasFlag(MemoryRecordOptions.ShowAsHex);
        set => Options = value ? Options | MemoryRecordOptions.ShowAsHex : Options & ~MemoryRecordOptions.ShowAsHex;
    }

    public bool Signed
    {
        get => Options.HasFlag(MemoryRecordOptions.ShowAsSigned);
        set => Options = value ? Options | MemoryRecordOptions.ShowAsSigned : Options & ~MemoryRecordOptions.ShowAsSigned;
    }

    public MemoryRecordOptions Options { get; set; }

    public int Color { get; set; }

    public bool AllowIncrease
    {
        get => _allowIncrease;
        set => _allowIncrease = value;
    }

    public bool IsPointer { get; set; }

    public List<int> Offsets { get; } = new();

    public AutoAssemblerData AutoAssemblerData { get; } = new();

    public bool Active
    {
        get => _active;
        set
        {
            if (_active == value)
            {
                return;
            }

            if (ActivationChanging is not null)
            {
                foreach (Func<MemoryRecord, bool, bool> handler in ActivationChanging.GetInvocationList())
                {
                    if (!handler(this, value))
                    {
                        return;
                    }
                }
            }

            if (VarType == VariableType.AutoAssembler)
            {
                if (value)
                {
                    var allocations = AutoAssemblerData.Allocations;
                    allocations.Clear();
                    AutoAssemblerData.RegisteredSymbols.Clear();
                    AutoAssembler.AutoAssemble(AutoAssemblerData.Script, true, allocations, AutoAssemblerData.RegisteredSymbols);
                }
                else
                {
                    AutoAssembler.AutoAssemble(AutoAssemblerData.Script, false, AutoAssemblerData.Allocations, AutoAssemblerData.RegisteredSymbols);
                    AutoAssemblerData.Allocations.Clear();
                    AutoAssemblerData.RegisteredSymbols.Clear();
                }
            }

            _active = value;
            ActivationChanged?.Invoke(this, value);
            Changed?.Invoke(this);
        }
    }

    public string Value
    {
        get
        {
            if (VarType == VariableType.AutoAssembler)
            {
                return AutoAssemblerData.Script;
            }

            return ByteInterpreter.ReadAndParseAddress(GetRealAddress(), VarType, CustomType, Hexadecimal, Signed, GetByteSize());
        }

        set
        {
            if (VarType == VariableType.AutoAssembler)
            {
                AutoAssemblerData.Script = value;
                Changed?.Invoke(this);
                return;
            }

            ByteInterpreter.ParseStringAndWriteToAddress(value, GetRealAddress(), VarType, Hexadecimal, CustomType);
            Changed?.Invoke(this);
        }
    }

    public void AddChild(MemoryRecord child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (child.Parent is not null)
        {
            child.Parent.RemoveChild(child);
        }

        child.Parent = this;
        _children.Add(child);
    }

    public bool RemoveChild(MemoryRecord child)
    {
        ArgumentNullException.ThrowIfNull(child);
        if (_children.Remove(child))
        {
            child.Parent = null;
            return true;
        }

        return false;
    }

    public MemoryRecordHotkey AddHotkey(KeyCombo keys, MemoryRecordHotkeyAction action, string? value = null, int delayBetweenActivate = 250)
    {
        var hotkey = new MemoryRecordHotkey(this, keys, action, value, delayBetweenActivate);
        _hotkeys.Add(hotkey);
        return hotkey;
    }

    public void DeleteHotkey(MemoryRecordHotkey hotkey)
    {
        if (_hotkeys.Remove(hotkey))
        {
            hotkey.Dispose();
        }
    }

    public nuint GetBaseAddress()
    {
        if (nuint.TryParse(_interpretableAddress, NumberStyles.Integer, CultureInfo.InvariantCulture, out var plain))
        {
            return plain;
        }

            return AddressParserGlobal.GetAddress(_interpretableAddress);
    }

    public nuint GetRealAddress()
    {
        var address = GetBaseAddress();
        if (!IsPointer || Offsets.Count == 0)
        {
            return address;
        }

        var pointerSize = CeFuncProc.ProcessHandler.Is64Bit ? 8 : 4;
        Span<byte> buffer = stackalloc byte[pointerSize];
        foreach (var offset in Offsets)
        {
            if (!NewKernelHandler.ReadProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, buffer, out _))
            {
                return 0;
            }

            address = pointerSize == 8
                ? (nuint)BitConverter.ToUInt64(buffer)
                : BitConverter.ToUInt32(buffer);
            address += (nuint)offset;
        }

        return address;
    }

    public int GetByteSize()
    {
        return VarType switch
        {
            VariableType.String => StringSize,
            VariableType.UnicodeString => StringSize * 2,
            VariableType.Custom when CustomType is not null => CustomType.ByteSize,
            _ => MemScan.GetVariableSize(VarType, string.Empty, Unicode, CustomType),
        };
    }

    public void AdjustNumericalValue(string delta, bool increase)
    {
        var current = Value;
        if (!double.TryParse(current, NumberStyles.Float, CultureInfo.InvariantCulture, out var currentValue))
        {
            return;
        }

        if (!double.TryParse(delta, NumberStyles.Float, CultureInfo.InvariantCulture, out var deltaValue))
        {
            deltaValue = CeFuncProc.StrToQWordEx(delta);
        }

        var next = increase ? currentValue + deltaValue : currentValue - deltaValue;
        Value = next.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString()
    {
        return string.IsNullOrWhiteSpace(Description) ? InterpretableAddress : Description;
    }
}