using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CheatEngine.Library;

public sealed class FoundList
{
    private readonly MemScan _memScan;
    private string _listName;
    private VariableType _variableType;
    private CustomType? _customType;
    private int _varLength;
    private bool _hexadecimal;
    private bool _signed;
    private bool _unicode;
    private List<ScanResultEntry> _entries = new();

    public FoundList(object? foundList, MemScan memScan, string listName = "")
    {
        _memScan = memScan;
        _listName = string.IsNullOrWhiteSpace(listName) ? "TMP" : listName;
    }

    public MemScan MemScan => _memScan;

    public VariableType VariableType => _variableType;

    public CustomType? CustomType => _customType;

    public bool IsHexadecimal => _hexadecimal;

    public bool IsSigned => _signed;

    public bool IsUnicode => _unicode;

    public bool IsUnknownInitialValue { get; private set; }

    public ulong Count => (ulong)_entries.Count;

    public string ListName
    {
        get => _listName;
        set => _listName = value;
    }

    public int GetVarLength()
    {
        return _varLength;
    }

    public void DeleteAddress(int index)
    {
        var addressPath = ScanResultBinaryFormat.GetAddressFilePath(_memScan.ScanResultFolder, _listName);
        if (!File.Exists(addressPath))
        {
            return;
        }

        var addressBytes = File.ReadAllBytes(addressPath);
        if (ScanResultBinaryFormat.ReadHeader(addressBytes) == ScanResultBinaryFormat.RegionHeader)
        {
            return;
        }

        var memoryPath = ScanResultBinaryFormat.GetMemoryFilePath(_memScan.ScanResultFolder, _listName);
        var memoryBytes = File.Exists(memoryPath) ? File.ReadAllBytes(memoryPath) : [];
        var storedValueSize = ScanResultBinaryFormat.GetStoredValueSize(_variableType, _varLength, _customType);
        var entries = ScanResultBinaryFormat.ReadNormalEntries(addressBytes, memoryBytes, _variableType, storedValueSize);
        entries.RemoveAt(index);

        ScanResultBinaryFormat.WriteNormalFiles(_memScan.ScanResultFolder, "NEW", entries, _variableType, storedValueSize, _customType);

        Deinitialize();

        if (File.Exists(memoryPath))
        {
            File.Delete(memoryPath);
        }

        File.Delete(addressPath);
        File.Move(ScanResultBinaryFormat.GetAddressFilePath(_memScan.ScanResultFolder, "NEW"), addressPath, overwrite: true);
        File.Move(ScanResultBinaryFormat.GetMemoryFilePath(_memScan.ScanResultFolder, "NEW"), memoryPath, overwrite: true);

        if (string.Equals(_listName, "TMP", StringComparison.OrdinalIgnoreCase))
        {
            _memScan.RemoveResultAt(index);
        }

        Reinitialize();
    }

    public void Clear()
    {
        _entries.Clear();
    }

    public void RefetchValueList()
    {
        ResetValues();
    }

    public void ResetValues()
    {
        foreach (var entry in _entries)
        {
            entry.DisplayValue = ByteInterpreter.ReadAndParseAddress((nuint)entry.Address, _variableType, _customType, _hexadecimal, _signed, _varLength);
        }
    }

    public long Initialize()
    {
        return Initialize(_variableType, _varLength, _hexadecimal, _signed, false, _unicode, _customType);
    }

    public long Initialize(VariableType variableType, CustomType? customType = null)
    {
        return Initialize(variableType, 0, false, false, false, false, customType);
    }

    public long Initialize(VariableType variableType, int varLength, bool hexadecimal, bool signed, bool binaryAsDecimal, bool unicode, CustomType? customType = null)
    {
        _variableType = variableType;
        _customType = customType;
        _varLength = varLength <= 0 ? MemScan.GetVariableSize(variableType, string.Empty, unicode, customType) : varLength;
        _hexadecimal = hexadecimal;
        _signed = signed;
        _unicode = unicode;

        var addressPath = ScanResultBinaryFormat.GetAddressFilePath(_memScan.ScanResultFolder, _listName);
        var memoryPath = ScanResultBinaryFormat.GetMemoryFilePath(_memScan.ScanResultFolder, _listName);
        if (!File.Exists(addressPath))
        {
            _entries.Clear();
            IsUnknownInitialValue = false;
            return 0;
        }

        var addressBytes = File.ReadAllBytes(addressPath);
        if (ScanResultBinaryFormat.ReadHeader(addressBytes) == ScanResultBinaryFormat.RegionHeader)
        {
            _entries.Clear();
            IsUnknownInitialValue = true;
            return 0;
        }

        var memoryBytes = File.Exists(memoryPath) ? File.ReadAllBytes(memoryPath) : [];
        var storedValueSize = ScanResultBinaryFormat.GetStoredValueSize(variableType, _varLength, customType);
        _entries = ScanResultBinaryFormat.ReadNormalEntries(addressBytes, memoryBytes, variableType, storedValueSize);
        IsUnknownInitialValue = false;

        foreach (var entry in _entries)
        {
            entry.DisplayValue = entry.Value.Length > 0
                ? ByteInterpreter.ReadAndParsePointer(entry.Value, variableType, customType, hexadecimal, signed, _varLength)
                : ByteInterpreter.ReadAndParseAddress((nuint)entry.Address, variableType, customType, hexadecimal, signed, _varLength);
        }

        return _entries.Count;
    }

    public long Reinitialize()
    {
        return Initialize(_variableType, _varLength, _hexadecimal, _signed, false, _unicode, _customType);
    }

    public void Deinitialize()
    {
        _entries.Clear();
    }

    public uint GetStartBit(int index)
    {
        return _entries[index].Extra;
    }

    public GroupAddress? GetGroupAddress(ulong index)
    {
        if (index >= (ulong)_entries.Count)
        {
            return null;
        }

        return new GroupAddress((nuint)_entries[(int)index].Address, _entries[(int)index].GroupOffsets ?? Array.Empty<uint>());
    }

    public nuint GetAddressOnly(ulong index, out uint extra, GroupAddress?[]? groupData = null)
    {
        var entry = _entries[(int)index];
        extra = entry.Extra;
        return (nuint)entry.Address;
    }

    public nuint GetAddress(ulong index, out uint extra, out string value)
    {
        var entry = _entries[(int)index];
        extra = entry.Extra;
        value = entry.DisplayValue;
        return (nuint)entry.Address;
    }

    public nuint GetAddress(ulong index)
    {
        return (nuint)_entries[(int)index].Address;
    }

    public ulong FindClosestAddress(nuint address)
    {
        if (_entries.Count == 0)
        {
            return 0;
        }

        var closest = _entries
            .Select((entry, index) => new { Index = index, Delta = Math.Abs((long)entry.Address - (long)address) })
            .OrderBy(item => item.Delta)
            .First();
        return (ulong)closest.Index;
    }

    public bool InModule(int index)
    {
        return SymbolHandler.Default.InModule((nuint)_entries[index].Address);
    }

    public string GetModuleNamePlusOffset(int index)
    {
        return SymbolHandler.Default.GetNameFromAddress((nuint)_entries[index].Address);
    }

    public void RebaseAddresslist(int index)
    {
    }

    public void RebaseAddresslistAgain()
    {
    }
}