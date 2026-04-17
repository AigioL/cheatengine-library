using System;
using System.Collections.Generic;
using System.Linq;

namespace CheatEngine.Library;

public sealed class MemoryRecordTable
{
    private readonly List<MemoryRecord> _records = new();

    public int Count => _records.Count;

    public IReadOnlyList<MemoryRecord> Records => _records;

    public MemoryRecord this[int index] => _records[index];

    public void Add(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        _records.Add(record);
    }

    public bool Remove(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _records.Remove(record);
    }

    public void Clear()
    {
        _records.Clear();
    }

    public MemoryRecord? GetRecordById(int id)
    {
        return _records.FirstOrDefault(record => record.Id == id);
    }
}

public sealed class MemoryRecordDatabase
{
    private readonly AddressList _addressList;
    private int _nextId = 1;

    public MemoryRecordDatabase(AddressList addressList)
    {
        _addressList = addressList;
    }

    public AddressList AddressList => _addressList;

    public MemoryRecord AddAddressManually(string address, string description = "", VariableType variableType = VariableType.Dword)
    {
        var memoryRecord = _addressList.CreateMemoryRecord();
        memoryRecord.Id = _nextId++;
        memoryRecord.InterpretableAddress = address;
        memoryRecord.Description = description;
        memoryRecord.VarType = variableType;
        return memoryRecord;
    }

    public MemoryRecord AddAutoAssembleScript(string script, string description = "")
    {
        var memoryRecord = AddAddressManually("0", description, VariableType.AutoAssembler);
        memoryRecord.AutoAssemblerData.Script = script;
        return memoryRecord;
    }

    public bool DeleteAddress(int id)
    {
        var record = _addressList.GetMemoryRecordById(id);
        if (record is null)
        {
            return false;
        }

        return _addressList.RemoveMemoryRecord(record);
    }
}