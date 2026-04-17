using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CheatEngine.Library;

public sealed class SavedScanHandler
{
    private readonly string _scanDirectory;
    private readonly string _savedResultsName;
    private readonly Dictionary<string, Dictionary<ulong, byte[]>> _typedCache = new(StringComparer.Ordinal);
    private byte[] _addressBytes = [];
    private byte[] _memoryBytes = [];
    private List<BinaryScanMemoryRegion> _regions = new();
    private ScanResultStorageType _storageType;

    public SavedScanHandler(string scanDirectory, string savedResultsName)
    {
        _scanDirectory = scanDirectory;
        _savedResultsName = string.IsNullOrWhiteSpace(savedResultsName) ? "TMP" : savedResultsName;
        Reinitialize();
    }

    public bool AllowRandomAccess { get; set; }

    public bool AllowNotFound { get; set; }

    public byte[]? GetPointerToAddress(nuint address, VariableType valueType, CustomType? customType, bool recallIfNeeded = true)
    {
        if (_storageType == ScanResultStorageType.Advanced)
        {
            return GetPointerFromRegionStorage(address, valueType, customType);
        }

        if (!ScanResultBinaryFormat.StoresPreviousValue(valueType))
        {
            if (AllowNotFound)
            {
                return null;
            }

            throw new KeyNotFoundException($"Failure in finding {address:X} in the saved scan results");
        }

        var valueSize = ScanResultBinaryFormat.GetLookupValueSize(valueType, customType);
        var cacheKey = $"{(int)valueType}:{valueSize}";
        if (!_typedCache.TryGetValue(cacheKey, out var valueCache))
        {
            valueCache = ScanResultBinaryFormat
                .ReadNormalEntries(_addressBytes, _memoryBytes, valueType, valueSize)
                .Where(entry => entry.Value.Length > 0)
                .ToDictionary(entry => entry.Address, entry => entry.Value);
            _typedCache[cacheKey] = valueCache;
        }

        if (valueCache.TryGetValue(address, out var value))
        {
            return value.ToArray();
        }

        if (AllowNotFound)
        {
            return null;
        }

        throw new KeyNotFoundException($"Failure in finding {address:X} in the saved scan results");
    }

    public void Deinitialize()
    {
        _typedCache.Clear();
        _addressBytes = [];
        _memoryBytes = [];
        _regions.Clear();
    }

    public void Reinitialize()
    {
        Deinitialize();

        var addressPath = ScanResultBinaryFormat.GetAddressFilePath(_scanDirectory, _savedResultsName);
        var memoryPath = ScanResultBinaryFormat.GetMemoryFilePath(_scanDirectory, _savedResultsName);
        if (!File.Exists(addressPath))
        {
            _storageType = ScanResultStorageType.AddressList;
            return;
        }

        _addressBytes = File.ReadAllBytes(addressPath);
        _memoryBytes = File.Exists(memoryPath) ? File.ReadAllBytes(memoryPath) : [];
        _storageType = ScanResultBinaryFormat.ReadHeader(_addressBytes) == ScanResultBinaryFormat.RegionHeader
            ? ScanResultStorageType.Advanced
            : ScanResultStorageType.AddressList;

        if (_storageType == ScanResultStorageType.Advanced)
        {
            _regions = ScanResultBinaryFormat.ReadMemoryRegions(_addressBytes);
        }
    }

    private byte[]? GetPointerFromRegionStorage(nuint address, VariableType valueType, CustomType? customType)
    {
        var valueSize = ScanResultBinaryFormat.GetLookupValueSize(valueType, customType);
        if (valueSize <= 0)
        {
            if (AllowNotFound)
            {
                return null;
            }

            throw new KeyNotFoundException($"Failure in finding {address:X} in the saved scan results");
        }

        foreach (var region in _regions)
        {
            var regionEnd = region.BaseAddress + (nuint)region.MemorySize;
            if (address < region.BaseAddress || address + (nuint)valueSize > regionEnd)
            {
                continue;
            }

            var offset = checked((int)(region.StartOffset + (address - region.BaseAddress)));
            if (offset < 0 || offset + valueSize > _memoryBytes.Length)
            {
                break;
            }

            return _memoryBytes.AsSpan(offset, valueSize).ToArray();
        }

        if (AllowNotFound)
        {
            return null;
        }

        throw new KeyNotFoundException($"Failure in finding {address:X} in the saved scan results");
    }
}