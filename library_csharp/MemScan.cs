using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.Library;

public sealed class ScanResultEntry
{
    public ulong Address { get; set; }

    public uint Extra { get; set; }

    public byte[] Value { get; set; } = [];

    public byte[]? PreviousValue { get; set; }

    public string DisplayValue { get; set; } = string.Empty;

    public uint[]? GroupOffsets { get; set; }
}

public sealed class MemScan : IDisposable
{
    private const uint MemCommit = 0x1000;
    private const uint PageGuard = 0x100;
    private const uint PageNoAccess = 0x01;
    private const int MaxScanChunkSize = 64 * 1024 * 1024;

    private readonly object _syncRoot = new();
    private readonly string _scanResultFolder;
    private VariableType _lastVariableType = VariableType.Dword;
    private ScanOption _lastScanOption = ScanOption.ExactValue;
    private CustomType? _lastCustomType;
    private bool _lastHexadecimal;
    private bool _lastUnicode;
    private int _lastByteSize;
    private nint _notifyWindow;
    private int _notifyMessage;
    private nint _unknownScanBufferBase;
    private List<BinaryScanMemoryRegion> _unknownScanRegions = new();
    private byte[] _unknownScanBytes = [];
    private List<ScanResultEntry> _results = new();

    public MemScan(object? progressBar = null)
    {
        _scanResultFolder = Path.Combine(Path.GetTempPath(), "CheatEngine.Library", "scan-" + Guid.NewGuid().ToString("N", CultureInfo.InvariantCulture)) + Path.DirectorySeparatorChar;
        Directory.CreateDirectory(_scanResultFolder);
    }

    ~MemScan()
    {
        Dispose(disposing: false);
    }

    public int NextScanCount { get; private set; }

    public string ScanResultFolder => _scanResultFolder;

    public IReadOnlyList<ScanResultEntry> Results
    {
        get
        {
            lock (_syncRoot)
            {
                return _results.ToArray();
            }
        }
    }

    public void SetScanDoneCallback(nint notifyWindow, int notifyMessage)
    {
        _notifyWindow = notifyWindow;
        _notifyMessage = notifyMessage;
    }

    public void FirstScan(ScanOption scanOption, VariableType variableType, RoundingType roundingType, string scanValue1, string scanValue2, nuint startAddress, nuint stopAddress, bool hexadecimal, bool binaryStringAsDecimal, bool unicode, bool caseSensitive, FastScanMethod fastScanMethod = FastScanMethod.NotAligned, string fastScanParameter = "", CustomType? customType = null)
    {
        ClearUnknownScanSnapshot();

        _lastScanOption = scanOption;
        _lastVariableType = variableType;
        _lastCustomType = customType;
        _lastHexadecimal = hexadecimal;
        _lastUnicode = unicode;
        _lastByteSize = GetVariableSize(variableType, scanValue1, unicode, customType);

        var results = ScanMemory(scanOption, variableType, scanValue1, scanValue2, startAddress, stopAddress, hexadecimal, unicode, caseSensitive, fastScanMethod, fastScanParameter, customType);
        lock (_syncRoot)
        {
            _results = results;
            NextScanCount = 0;
        }

        WriteCurrentResultFiles("TMP");
        SaveFirstScan.Save(_scanResultFolder);
        InvokeScanDone();
    }

    public void NextScan(ScanOption scanOption, RoundingType roundingType, string scanValue1, string scanValue2, bool hexadecimal, bool binaryStringAsDecimal, bool unicode, bool caseSensitive, bool percentage, bool compareToSavedScan, string savedScanName)
    {
        _lastScanOption = scanOption;
        List<ScanResultEntry> source;
        lock (_syncRoot)
        {
            source = _results.ToList();
        }

        SavedScanHandler? savedScanHandler = null;
        if (compareToSavedScan)
        {
            savedScanHandler = new SavedScanHandler(_scanResultFolder, savedScanName);
            savedScanHandler.AllowNotFound = true;
        }

        var filtered = new List<ScanResultEntry>(source.Count);
        try
        {
            foreach (var result in source)
            {
                var previous = savedScanHandler?.GetPointerToAddress((nuint)result.Address, _lastVariableType, _lastCustomType) ?? result.PreviousValue ?? result.Value;
                var current = ReadBytes((nuint)result.Address, result.Value.Length);
                if (current is null)
                {
                    continue;
                }

                if (MatchesNextScan(scanOption, current, previous, scanValue1, scanValue2, hexadecimal, percentage, _lastVariableType, _lastCustomType))
                {
                    filtered.Add(new ScanResultEntry
                    {
                        Address = result.Address,
                        Extra = result.Extra,
                        Value = current,
                        PreviousValue = previous,
                        GroupOffsets = result.GroupOffsets?.ToArray(),
                        DisplayValue = ByteInterpreter.ReadAndParsePointer(current, _lastVariableType, _lastCustomType, hexadecimal, false, current.Length),
                    });
                }
            }
        }
        finally
        {
            savedScanHandler?.Deinitialize();
        }

        lock (_syncRoot)
        {
            _results = filtered;
            NextScanCount++;
        }

        ClearUnknownScanSnapshot();
        WriteCurrentResultFiles("TMP");
        InvokeScanDone();
    }

    public void SaveCurrentResults(string resultName)
    {
        ScanResultBinaryFormat.CopyResultFiles(_scanResultFolder, "TMP", resultName);
    }

    public void RemoveResultAt(int index)
    {
        lock (_syncRoot)
        {
            _results.RemoveAt(index);
        }

        if (_lastScanOption != ScanOption.UnknownValue)
        {
            WriteCurrentResultFiles("TMP");
        }
    }

    public void Dispose()
    {
        Dispose(disposing: true);
        GC.SuppressFinalize(this);
    }

    internal static int GetVariableSize(VariableType variableType, string scanValue, bool unicode, CustomType? customType)
    {
        return variableType switch
        {
            VariableType.Byte => 1,
            VariableType.Word => 2,
            VariableType.Dword or VariableType.Single => 4,
            VariableType.Qword or VariableType.Double or VariableType.Pointer => 8,
            VariableType.String => unicode ? scanValue.Length * 2 : scanValue.Length,
            VariableType.UnicodeString => scanValue.Length * 2,
            VariableType.ByteArray => ParseBytePattern(scanValue).Length,
            VariableType.Custom when customType is not null => customType.ByteSize,
            _ => 1,
        };
    }

    private void WriteCurrentResultFiles(string resultName)
    {
        List<ScanResultEntry> results;
        lock (_syncRoot)
        {
            results = _results.Select(result => new ScanResultEntry
            {
                Address = result.Address,
                Extra = result.Extra,
                Value = result.Value.ToArray(),
                PreviousValue = result.PreviousValue?.ToArray(),
                GroupOffsets = result.GroupOffsets?.ToArray(),
                DisplayValue = result.DisplayValue,
            }).ToList();
        }

        if (_lastScanOption == ScanOption.UnknownValue && _unknownScanRegions.Count > 0)
        {
            ScanResultBinaryFormat.WriteRegionFiles(_scanResultFolder, resultName, _unknownScanRegions, _unknownScanBytes, _unknownScanBufferBase);
            return;
        }

        var storedValueSize = ScanResultBinaryFormat.GetStoredValueSize(_lastVariableType, _lastByteSize, _lastCustomType);
        ScanResultBinaryFormat.WriteNormalFiles(_scanResultFolder, resultName, results, _lastVariableType, storedValueSize, _lastCustomType);
    }

    private List<ScanResultEntry> ScanMemory(ScanOption scanOption, VariableType variableType, string scanValue1, string scanValue2, nuint startAddress, nuint stopAddress, bool hexadecimal, bool unicode, bool caseSensitive, FastScanMethod fastScanMethod, string fastScanParameter, CustomType? customType)
    {
        var results = new List<ScanResultEntry>();
        var regionBytes = scanOption == ScanOption.UnknownValue ? new MemoryStream() : null;
        var regions = scanOption == ScanOption.UnknownValue ? new List<BinaryScanMemoryRegion>() : null;
        var handle = ResolveProcessHandle();
        if (stopAddress == 0)
        {
            stopAddress = nuint.MaxValue;
        }

        var alignment = fastScanMethod == FastScanMethod.Aligned && int.TryParse(fastScanParameter, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedAlignment)
            ? Math.Max(parsedAlignment, 1)
            : 1;
        var valueSize = variableType == VariableType.Grouped ? 0 : GetVariableSize(variableType, scanValue1, unicode, customType);
        var overlap = GetScanOverlap(variableType, scanValue1, unicode, customType);

        foreach (var (regionStart, regionSize) in EnumerateReadableRegions(handle, startAddress, stopAddress))
        {
            var chunkStart = regionStart;
            var regionEnd = regionStart + regionSize;
            while (chunkStart < regionEnd)
            {
                var remaining = regionEnd - chunkStart;
                var coreSize = (int)Math.Min((ulong)remaining, (ulong)MaxScanChunkSize);
                if (coreSize <= 0)
                {
                    break;
                }

                var isLastChunk = remaining <= (nuint)MaxScanChunkSize;
                var bytesToRead = isLastChunk
                    ? coreSize
                    : checked((int)Math.Min((ulong)remaining, (ulong)(coreSize + overlap)));

                var buffer = new byte[bytesToRead];
                if (!NewKernelHandler.ReadProcessMemory(handle, chunkStart, buffer, out _))
                {
                    chunkStart += (nuint)coreSize;
                    continue;
                }

                var scanLength = isLastChunk ? buffer.Length : coreSize;
                if (variableType == VariableType.Grouped)
                {
                    SearchGrouped(results, buffer, chunkStart, scanValue1, hexadecimal, scanLength);
                    chunkStart += (nuint)coreSize;
                    continue;
                }

                if (scanOption == ScanOption.UnknownValue)
                {
                    AppendUnknownScanRegion(regions!, regionBytes!, chunkStart, buffer.AsSpan(0, scanLength).ToArray());

                    for (var offset = 0; offset < scanLength && offset + valueSize <= buffer.Length; offset += Math.Max(valueSize, alignment))
                    {
                        var value = buffer.AsSpan(offset, valueSize).ToArray();
                        results.Add(new ScanResultEntry
                        {
                            Address = (ulong)(chunkStart + (nuint)offset),
                            Value = value,
                            PreviousValue = value.ToArray(),
                            DisplayValue = ByteInterpreter.ReadAndParsePointer(value, variableType, customType, hexadecimal, false, valueSize),
                        });
                    }

                    chunkStart += (nuint)coreSize;
                    continue;
                }

                if (variableType == VariableType.ByteArray)
                {
                    SearchByteArray(results, buffer, chunkStart, scanValue1, hexadecimal, scanLength);
                    chunkStart += (nuint)coreSize;
                    continue;
                }

                if (variableType is VariableType.String or VariableType.UnicodeString)
                {
                    SearchString(results, buffer, chunkStart, scanValue1, variableType == VariableType.UnicodeString || unicode, caseSensitive, scanLength);
                    chunkStart += (nuint)coreSize;
                    continue;
                }

                for (var offset = 0; offset < scanLength && offset + valueSize <= buffer.Length; offset += alignment)
                {
                    var candidate = buffer.AsSpan(offset, valueSize).ToArray();
                    if (!MatchesFirstScan(scanOption, candidate, scanValue1, scanValue2, hexadecimal, variableType, customType))
                    {
                        continue;
                    }

                    results.Add(new ScanResultEntry
                    {
                        Address = (ulong)(chunkStart + (nuint)offset),
                        Value = candidate,
                        PreviousValue = candidate.ToArray(),
                        DisplayValue = ByteInterpreter.ReadAndParsePointer(candidate, variableType, customType, hexadecimal, false, valueSize),
                    });
                }

                chunkStart += (nuint)coreSize;
            }
        }

        if (scanOption == ScanOption.UnknownValue)
        {
            SetUnknownScanSnapshot(regions!, regionBytes!.ToArray());
        }

        return results;
    }

    private static IEnumerable<(nuint Start, nuint Size)> EnumerateReadableRegions(nint processHandle, nuint startAddress, nuint stopAddress)
    {
        var currentBaseAddress = startAddress;
        while (NewKernelHandler.VirtualQueryEx(processHandle, currentBaseAddress, out var memoryInformation) != 0
            && currentBaseAddress < stopAddress
            && currentBaseAddress + memoryInformation.RegionSize > currentBaseAddress)
        {
            var regionBase = unchecked((nuint)memoryInformation.BaseAddress);
            var regionSize = memoryInformation.RegionSize;
            var nextRegion = regionBase + regionSize;
            if (nextRegion <= currentBaseAddress)
            {
                yield break;
            }

            currentBaseAddress = nextRegion;

            if (regionBase < startAddress)
            {
                var trim = startAddress - regionBase;
                if (regionSize <= trim)
                {
                    continue;
                }

                regionBase = startAddress;
                regionSize -= trim;
            }

            if (regionBase >= stopAddress)
            {
                yield break;
            }

            if (regionBase + regionSize > stopAddress)
            {
                regionSize = stopAddress - regionBase;
            }

            var validRegion = memoryInformation.State == MemCommit
                && (memoryInformation.Protect & PageGuard) == 0
                && (memoryInformation.Protect & PageNoAccess) == 0
                && regionSize > 0;

            if (!validRegion)
            {
                continue;
            }

            yield return (regionBase, regionSize);
        }
    }

    private static int GetScanOverlap(VariableType variableType, string scanValue, bool unicode, CustomType? customType)
    {
        return variableType switch
        {
            VariableType.Grouped => Math.Max(new GroupScanCommandParser(scanValue).BlockSize - 1, 0),
            VariableType.ByteArray => Math.Max(ParseBytePattern(scanValue).Length - 1, 0),
            VariableType.String => Math.Max((unicode ? Encoding.Unicode : Encoding.Default).GetByteCount(scanValue) - 1, 0),
            VariableType.UnicodeString => Math.Max(Encoding.Unicode.GetByteCount(scanValue) - 1, 0),
            _ => Math.Max(GetVariableSize(variableType, scanValue, unicode, customType) - 1, 0),
        };
    }

    private static void SearchByteArray(List<ScanResultEntry> results, byte[] buffer, nuint baseAddress, string pattern, bool hexadecimal, int searchLength = -1)
    {
        var needle = ParseBytePattern(pattern);
        if (needle.Length == 0)
        {
            return;
        }

        var maxStartOffset = searchLength < 0 ? buffer.Length : Math.Min(searchLength, buffer.Length);
        for (var offset = 0; offset < maxStartOffset && offset + needle.Length <= buffer.Length; offset++)
        {
            var matched = true;
            for (var index = 0; index < needle.Length; index++)
            {
                if (needle[index] >= 0 && buffer[offset + index] != needle[index])
                {
                    matched = false;
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            var value = buffer.AsSpan(offset, needle.Length).ToArray();
            results.Add(new ScanResultEntry
            {
                Address = (ulong)(baseAddress + (nuint)offset),
                Value = value,
                PreviousValue = value.ToArray(),
                DisplayValue = ByteInterpreter.ReadAndParsePointer(value, VariableType.ByteArray, null, hexadecimal, false, value.Length),
            });
        }
    }

    private static void SearchString(List<ScanResultEntry> results, byte[] buffer, nuint baseAddress, string value, bool unicode, bool caseSensitive, int searchLength = -1)
    {
        var encoding = unicode ? Encoding.Unicode : Encoding.Default;
        var needle = encoding.GetBytes(caseSensitive ? value : value.ToUpperInvariant());
        if (needle.Length == 0)
        {
            return;
        }

        var maxStartOffset = searchLength < 0 ? buffer.Length : Math.Min(searchLength, buffer.Length);
        for (var offset = 0; offset < maxStartOffset && offset + needle.Length <= buffer.Length; offset++)
        {
            var slice = buffer.AsSpan(offset, needle.Length).ToArray();
            var candidate = unicode ? Encoding.Unicode.GetString(slice) : Encoding.Default.GetString(slice);
            if (!caseSensitive)
            {
                candidate = candidate.ToUpperInvariant();
            }

            if (!string.Equals(candidate, caseSensitive ? value : value.ToUpperInvariant(), StringComparison.Ordinal))
            {
                continue;
            }

            results.Add(new ScanResultEntry
            {
                Address = (ulong)(baseAddress + (nuint)offset),
                Value = slice,
                PreviousValue = slice.ToArray(),
                DisplayValue = unicode ? Encoding.Unicode.GetString(slice) : Encoding.Default.GetString(slice),
            });
        }
    }

    private static void SearchGrouped(List<ScanResultEntry> results, byte[] buffer, nuint baseAddress, string command, bool hexadecimal, int searchLength = -1)
    {
        var parser = new GroupScanCommandParser(command);
        if (parser.BlockSize <= 0)
        {
            return;
        }

        var maxStartOffset = searchLength < 0 ? buffer.Length : Math.Min(searchLength, buffer.Length);
        for (var offset = 0; offset < maxStartOffset && offset + parser.BlockSize <= buffer.Length; offset++)
        {
            var matched = true;
            foreach (var element in parser.Elements)
            {
                if (element.Offset + element.ByteSize > parser.BlockSize)
                {
                    matched = false;
                    break;
                }

                var slice = buffer.AsSpan(offset + element.Offset, element.ByteSize).ToArray();
                if (element.Wildcard)
                {
                    continue;
                }

                var interpreted = ByteInterpreter.ReadAndParsePointer(slice, element.VariableType, element.CustomType, false, false, element.ByteSize);
                if (element.VariableType is VariableType.String or VariableType.UnicodeString)
                {
                    matched = string.Equals(interpreted, element.UserValue, StringComparison.Ordinal);
                }
                else if (element.VariableType is VariableType.Single or VariableType.Double)
                {
                    matched = Math.Abs(double.Parse(interpreted, CultureInfo.InvariantCulture) - element.ValueFloat) < 0.0001d;
                }
                else
                {
                    matched = CeFuncProc.StrToQWordEx(interpreted) == element.ValueInt;
                }

                if (!matched)
                {
                    break;
                }
            }

            if (!matched)
            {
                continue;
            }

            var value = buffer.AsSpan(offset, parser.BlockSize).ToArray();
            results.Add(new ScanResultEntry
            {
                Address = (ulong)(baseAddress + (nuint)offset),
                Value = value,
                PreviousValue = value.ToArray(),
                GroupOffsets = parser.Elements.Select(element => unchecked((uint)element.Offset)).ToArray(),
                DisplayValue = Convert.ToHexString(value),
            });
        }
    }

    private static bool MatchesFirstScan(ScanOption scanOption, byte[] candidate, string scanValue1, string scanValue2, bool hexadecimal, VariableType variableType, CustomType? customType)
    {
        if (scanOption == ScanOption.ExactValue)
        {
            if (variableType is VariableType.Byte or VariableType.Word or VariableType.Dword or VariableType.Qword)
            {
                return EqualsInteger(candidate, CeFuncProc.StrToQWordEx(hexadecimal ? "$" + scanValue1 : scanValue1));
            }

            if (variableType is VariableType.Single or VariableType.Double)
            {
                var value = double.Parse(scanValue1, CultureInfo.InvariantCulture);
                return Math.Abs(ReadFloating(candidate, variableType) - value) < 0.0001d;
            }
        }

        if (scanOption == ScanOption.ValueBetween)
        {
            var left = double.Parse(scanValue1, CultureInfo.InvariantCulture);
            var right = double.Parse(scanValue2, CultureInfo.InvariantCulture);
            var value = ReadFloating(candidate, variableType);
            return value >= Math.Min(left, right) && value <= Math.Max(left, right);
        }

        if (scanOption == ScanOption.BiggerThan)
        {
            return ReadFloating(candidate, variableType) > double.Parse(scanValue1, CultureInfo.InvariantCulture);
        }

        if (scanOption == ScanOption.SmallerThan)
        {
            return ReadFloating(candidate, variableType) < double.Parse(scanValue1, CultureInfo.InvariantCulture);
        }

        return false;
    }

    private static bool MatchesNextScan(ScanOption scanOption, byte[] current, byte[] previous, string scanValue1, string scanValue2, bool hexadecimal, bool percentage, VariableType variableType, CustomType? customType)
    {
        if (scanOption == ScanOption.Changed)
        {
            return !current.AsSpan().SequenceEqual(previous);
        }

        if (scanOption == ScanOption.Unchanged)
        {
            return current.AsSpan().SequenceEqual(previous);
        }

        var currentValue = ReadFloating(current, variableType);
        var previousValue = ReadFloating(previous, variableType);
        var target = string.IsNullOrWhiteSpace(scanValue1) ? 0d : double.Parse(scanValue1, CultureInfo.InvariantCulture);
        if (percentage && previousValue != 0)
        {
            target = previousValue * (target / 100d);
        }

        return scanOption switch
        {
            ScanOption.IncreasedValue => currentValue > previousValue,
            ScanOption.IncreasedValueBy => Math.Abs(currentValue - previousValue - target) < 0.0001d,
            ScanOption.DecreasedValue => currentValue < previousValue,
            ScanOption.DecreasedValueBy => Math.Abs(previousValue - currentValue - target) < 0.0001d,
            ScanOption.BiggerThan => currentValue > target,
            ScanOption.SmallerThan => currentValue < target,
            ScanOption.ExactValue => MatchesFirstScan(ScanOption.ExactValue, current, scanValue1, scanValue2, hexadecimal, variableType, customType),
            ScanOption.ValueBetween => MatchesFirstScan(ScanOption.ValueBetween, current, scanValue1, scanValue2, hexadecimal, variableType, customType),
            _ => false,
        };
    }

    private static bool EqualsInteger(byte[] candidate, ulong searchValue)
    {
        return candidate.Length switch
        {
            1 => candidate[0] == (byte)searchValue,
            2 => BinaryPrimitives.ReadUInt16LittleEndian(candidate) == (ushort)searchValue,
            4 => BinaryPrimitives.ReadUInt32LittleEndian(candidate) == (uint)searchValue,
            8 => BinaryPrimitives.ReadUInt64LittleEndian(candidate) == searchValue,
            _ => false,
        };
    }

    private static double ReadFloating(byte[] candidate, VariableType variableType)
    {
        return variableType switch
        {
            VariableType.Byte => candidate[0],
            VariableType.Word => BinaryPrimitives.ReadUInt16LittleEndian(candidate),
            VariableType.Dword => BinaryPrimitives.ReadUInt32LittleEndian(candidate),
            VariableType.Qword => BinaryPrimitives.ReadUInt64LittleEndian(candidate),
            VariableType.Single => BitConverter.ToSingle(candidate, 0),
            VariableType.Double => BitConverter.ToDouble(candidate, 0),
            _ => 0,
        };
    }

    private byte[]? ReadBytes(nuint address, int size)
    {
        var buffer = new byte[size];
        return NewKernelHandler.ReadProcessMemory(ResolveProcessHandle(), address, buffer, out _) ? buffer : null;
    }

    private static int[] ParseBytePattern(string pattern)
    {
        var items = new List<int>();
        CeFuncProc.ConvertStringToBytes(pattern, true, items);
        return items.ToArray();
    }

    private nint ResolveProcessHandle()
    {
        return CeFuncProc.ProcessHandler.ProcessHandle != 0 ? CeFuncProc.ProcessHandler.ProcessHandle : Process.GetCurrentProcess().Handle;
    }

    private Process ResolveProcess()
    {
        return CeFuncProc.ProcessHandler.ProcessId != 0 ? Process.GetProcessById((int)CeFuncProc.ProcessHandler.ProcessId) : Process.GetCurrentProcess();
    }

    private void InvokeScanDone()
    {
        _ = _notifyWindow;
        _ = _notifyMessage;
    }

    private void AppendUnknownScanRegion(List<BinaryScanMemoryRegion> regions, MemoryStream regionBytes, nuint baseAddress, byte[] buffer)
    {
        if (regions.Count > 0)
        {
            var last = regions[^1];
            if (last.BaseAddress + (nuint)last.MemorySize == baseAddress)
            {
                regions[^1] = last with { MemorySize = last.MemorySize + (ulong)buffer.Length };
                regionBytes.Write(buffer, 0, buffer.Length);
                return;
            }
        }

        regions.Add(new BinaryScanMemoryRegion(baseAddress, (ulong)buffer.Length, regions.Count > 0, (nuint)regionBytes.Length));
        regionBytes.Write(buffer, 0, buffer.Length);
    }

    private void SetUnknownScanSnapshot(List<BinaryScanMemoryRegion> regions, byte[] regionBytes)
    {
        ClearUnknownScanSnapshot();
        _unknownScanRegions = regions;
        _unknownScanBytes = regionBytes;
        if (regionBytes.Length == 0)
        {
            return;
        }

        _unknownScanBufferBase = Marshal.AllocHGlobal(regionBytes.Length);
        Marshal.Copy(regionBytes, 0, _unknownScanBufferBase, regionBytes.Length);
    }

    private void Dispose(bool disposing)
    {
        if (disposing)
        {
            lock (_syncRoot)
            {
                _results.Clear();
            }
        }

        ClearUnknownScanSnapshot();
    }

    private void ClearUnknownScanSnapshot()
    {
        if (_unknownScanBufferBase != 0)
        {
            Marshal.FreeHGlobal(_unknownScanBufferBase);
            _unknownScanBufferBase = 0;
        }

        _unknownScanRegions.Clear();
        _unknownScanBytes = [];
    }
}