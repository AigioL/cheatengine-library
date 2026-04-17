using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace CheatEngine.Library;

public sealed class ExtraSymbolDataEntry
{
    public string Name { get; set; } = string.Empty;

    public string ValueType { get; set; } = string.Empty;

    public string Position { get; set; } = string.Empty;
}

public sealed class ExtraSymbolData
{
    public List<ExtraSymbolDataEntry> Parameters { get; } = new();

    public List<ExtraSymbolDataEntry> Locals { get; } = new();
}

public sealed class CeSymbolInfo
{
    public string SearchKey { get; init; } = string.Empty;

    public string OriginalString { get; init; } = string.Empty;

    public string Module { get; init; } = string.Empty;

    public ulong Address { get; init; }

    public int Size { get; init; }

    public ExtraSymbolData? Extra { get; init; }

    public CeSymbolInfo? Previous { get; internal set; }

    public CeSymbolInfo? Next { get; internal set; }
}

public sealed class SymbolListHandler
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly SortedList<ulong, List<CeSymbolInfo>> _addressToString = new();
    private readonly Dictionary<string, CeSymbolInfo> _stringToAddress = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ExtraSymbolData> _extraSymbolDataList = new();

    public void AddExtraSymbolData(ExtraSymbolData data)
    {
        _lock.EnterWriteLock();
        try
        {
            _extraSymbolDataList.Add(data);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public CeSymbolInfo AddSymbol(string module, string searchKey, ulong address, int size, bool skipAddressToStringLookup = false, ExtraSymbolData? extraData = null)
    {
        var symbol = new CeSymbolInfo
        {
            Module = module,
            OriginalString = searchKey,
            SearchKey = searchKey.ToLowerInvariant(),
            Address = address,
            Size = size,
            Extra = extraData,
        };

        _lock.EnterWriteLock();
        try
        {
            if (!skipAddressToStringLookup)
            {
                if (!_addressToString.TryGetValue(address, out var symbols))
                {
                    symbols = new List<CeSymbolInfo>();
                    _addressToString.Add(address, symbols);
                }

                var previous = symbols.LastOrDefault();
                if (previous is not null)
                {
                    previous.Next = symbol;
                    symbol.Previous = previous;
                }

                symbols.Add(symbol);
            }

            _stringToAddress[symbol.SearchKey] = symbol;
            return symbol;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public CeSymbolInfo? FindAddress(ulong address)
    {
        _lock.EnterReadLock();
        try
        {
            if (_addressToString.TryGetValue(address, out var exact) && exact.Count > 0)
            {
                return exact[0];
            }

            foreach (var entry in _addressToString.Reverse())
            {
                foreach (var symbol in entry.Value)
                {
                    var upperBound = symbol.Size <= 0 ? symbol.Address : symbol.Address + (ulong)symbol.Size;
                    if (CeFuncProc.InRangeQ(address, symbol.Address, upperBound))
                    {
                        return symbol;
                    }
                }
            }

            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public CeSymbolInfo? FindSymbol(string symbol)
    {
        _lock.EnterReadLock();
        try
        {
            return _stringToAddress.GetValueOrDefault(symbol.ToLowerInvariant());
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public CeSymbolInfo? FindFirstSymbolFromBase(ulong baseAddress)
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var entry in _addressToString)
            {
                if (entry.Key >= baseAddress && entry.Value.Count > 0)
                {
                    return entry.Value[0];
                }
            }

            return null;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void Clear()
    {
        _lock.EnterWriteLock();
        try
        {
            _addressToString.Clear();
            _stringToAddress.Clear();
            _extraSymbolDataList.Clear();
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }
}