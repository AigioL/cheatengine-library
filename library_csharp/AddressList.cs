using System;
using System.Collections.Generic;
using System.Linq;

namespace CheatEngine.Library;

public sealed class AddressList
{
    private readonly List<MemoryRecord> _records = new();

    public AddressList()
    {
        Database = new MemoryRecordDatabase(this);
    }

    public MemoryRecordDatabase Database { get; }

    public int Count => _records.Count;

    public IReadOnlyList<MemoryRecord> Records => _records;

    public MemoryRecord this[int index] => _records[index];

    public MemoryRecord CreateMemoryRecord()
    {
        var record = new MemoryRecord(this);
        _records.Add(record);
        return record;
    }

    public bool RemoveMemoryRecord(MemoryRecord record)
    {
        ArgumentNullException.ThrowIfNull(record);
        return _records.Remove(record);
    }

    public void Clear()
    {
        _records.Clear();
    }

    public MemoryRecord? GetMemoryRecordById(int id)
    {
        return _records.FirstOrDefault(record => record.Id == id);
    }

    public MemoryRecord? GetMemoryRecordByDescription(string description)
    {
        return _records.FirstOrDefault(record => string.Equals(record.Description, description, StringComparison.OrdinalIgnoreCase));
    }

    public IEnumerable<MemoryRecord> EnumerateVisibleRecords()
    {
        return _records.Where(record => record.Parent is null || !record.Parent.Options.HasFlag(MemoryRecordOptions.HideChildren));
    }
}