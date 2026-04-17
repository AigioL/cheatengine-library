using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.Library;

internal enum ScanResultStorageType
{
    AddressList,
    Advanced,
}

internal sealed record BinaryScanMemoryRegion(nuint BaseAddress, ulong MemorySize, bool IsChild, nuint StartOffset);

internal static class ScanResultBinaryFormat
{
    internal const string RegionHeader = "REGION";
    internal const string NormalHeader = "NORMAL";
    internal const int HeaderSize = 7;

    internal static int PointerSize => IntPtr.Size;

    internal static string GetAddressFilePath(string folder, string resultName)
    {
        return Path.Combine(folder, $"Addresses.{NormalizeResultName(resultName)}");
    }

    internal static string GetMemoryFilePath(string folder, string resultName)
    {
        return Path.Combine(folder, $"Memory.{NormalizeResultName(resultName)}");
    }

    internal static void CopyResultFiles(string folder, string sourceName, string targetName)
    {
        var sourceAddress = GetAddressFilePath(folder, sourceName);
        var sourceMemory = GetMemoryFilePath(folder, sourceName);
        var targetAddress = GetAddressFilePath(folder, targetName);
        var targetMemory = GetMemoryFilePath(folder, targetName);

        if (File.Exists(sourceAddress))
        {
            File.Copy(sourceAddress, targetAddress, overwrite: true);
        }

        if (File.Exists(sourceMemory))
        {
            File.Copy(sourceMemory, targetMemory, overwrite: true);
        }
        else
        {
            File.WriteAllBytes(targetMemory, []);
        }
    }

    internal static void WriteNormalFiles(string folder, string resultName, IReadOnlyList<ScanResultEntry> results, VariableType variableType, int valueSize, CustomType? customType)
    {
        using var addressStream = new FileStream(GetAddressFilePath(folder, resultName), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
        using var memoryStream = new FileStream(GetMemoryFilePath(folder, resultName), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
        using var addressWriter = new BinaryWriter(addressStream, Encoding.ASCII, leaveOpen: true);

        WriteHeader(addressStream, NormalHeader);

        if (variableType == VariableType.Grouped)
        {
            var offsetCount = results.Count > 0 ? results[0].GroupOffsets?.Length ?? 0 : 0;
            addressWriter.Write(offsetCount);
            foreach (var result in results)
            {
                WritePointer(addressWriter, (nuint)result.Address);
                var offsets = result.GroupOffsets ?? Array.Empty<uint>();
                for (var index = 0; index < offsetCount; index++)
                {
                    addressWriter.Write(index < offsets.Length ? offsets[index] : 0U);
                }
            }

            return;
        }

        if (variableType is VariableType.Binary or VariableType.All)
        {
            foreach (var result in results)
            {
                WritePointer(addressWriter, (nuint)result.Address);
                WritePointer(addressWriter, result.Extra);
            }

            if (StoresPreviousValue(variableType))
            {
                foreach (var result in results)
                {
                    WriteFixedValue(memoryStream, result.Value, valueSize);
                }
            }

            return;
        }

        foreach (var result in results)
        {
            WritePointer(addressWriter, (nuint)result.Address);
        }

        if (!StoresPreviousValue(variableType))
        {
            return;
        }

        foreach (var result in results)
        {
            WriteFixedValue(memoryStream, result.Value, valueSize);
        }
    }

    internal static void WriteRegionFiles(string folder, string resultName, IReadOnlyList<BinaryScanMemoryRegion> regions, ReadOnlySpan<byte> memoryBytes, nint basePointer)
    {
        using var addressStream = new FileStream(GetAddressFilePath(folder, resultName), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
        using var memoryStream = new FileStream(GetMemoryFilePath(folder, resultName), System.IO.FileMode.Create, System.IO.FileAccess.Write, System.IO.FileShare.None);
        using var addressWriter = new BinaryWriter(addressStream, Encoding.ASCII, leaveOpen: true);

        WriteHeader(addressStream, RegionHeader);
        foreach (var region in regions)
        {
            WriteMemoryRegion(addressWriter, region, basePointer);
        }

        memoryStream.Write(memoryBytes);
    }

    internal static ScanResultStorageType ReadStorageType(string folder, string resultName)
    {
        using var stream = File.OpenRead(GetAddressFilePath(folder, resultName));
        return ReadHeader(stream) == RegionHeader ? ScanResultStorageType.Advanced : ScanResultStorageType.AddressList;
    }

    internal static List<BinaryScanMemoryRegion> ReadMemoryRegions(ReadOnlySpan<byte> addressBytes)
    {
        var regions = new List<BinaryScanMemoryRegion>();
        if (addressBytes.Length < HeaderSize || ReadHeader(addressBytes) != RegionHeader)
        {
            return regions;
        }

        var offset = HeaderSize;
        var regionSize = GetMemoryRegionSize();
        nuint currentStartOffset = 0;
        while (offset + regionSize <= addressBytes.Length)
        {
            var baseAddress = ReadPointer(addressBytes, offset);
            offset += PointerSize;
            var memorySize = BinaryPrimitives.ReadUInt64LittleEndian(addressBytes.Slice(offset, sizeof(ulong)));
            offset += sizeof(ulong);
            var isChild = addressBytes[offset] != 0;
            offset += 1;
            offset += GetMemoryRegionPadding();
            _ = ReadPointer(addressBytes, offset);
            offset += PointerSize;

            regions.Add(new BinaryScanMemoryRegion(baseAddress, memorySize, isChild, currentStartOffset));
            currentStartOffset += (nuint)memorySize;
        }

        return regions;
    }

    internal static List<ScanResultEntry> ReadNormalEntries(ReadOnlySpan<byte> addressBytes, ReadOnlySpan<byte> memoryBytes, VariableType variableType, int valueSize)
    {
        var entries = new List<ScanResultEntry>();
        if (addressBytes.Length < HeaderSize || ReadHeader(addressBytes) != NormalHeader)
        {
            return entries;
        }

        var addressOffset = HeaderSize;
        var memoryOffset = 0;

        if (variableType == VariableType.Grouped)
        {
            if (addressBytes.Length < addressOffset + sizeof(int))
            {
                return entries;
            }

            var offsetCount = BinaryPrimitives.ReadInt32LittleEndian(addressBytes.Slice(addressOffset, sizeof(int)));
            addressOffset += sizeof(int);
            var entrySize = PointerSize + (offsetCount * sizeof(uint));
            while (addressOffset + entrySize <= addressBytes.Length)
            {
                var address = ReadPointer(addressBytes, addressOffset);
                addressOffset += PointerSize;

                var offsets = new uint[offsetCount];
                for (var index = 0; index < offsetCount; index++)
                {
                    offsets[index] = BinaryPrimitives.ReadUInt32LittleEndian(addressBytes.Slice(addressOffset, sizeof(uint)));
                    addressOffset += sizeof(uint);
                }

                entries.Add(new ScanResultEntry
                {
                    Address = address,
                    GroupOffsets = offsets,
                    Value = [],
                });
            }

            return entries;
        }

        if (variableType is VariableType.Binary or VariableType.All)
        {
            var entrySize = PointerSize * 2;
            while (addressOffset + entrySize <= addressBytes.Length)
            {
                var address = ReadPointer(addressBytes, addressOffset);
                addressOffset += PointerSize;
                var extra = ReadPointer(addressBytes, addressOffset);
                addressOffset += PointerSize;

                entries.Add(new ScanResultEntry
                {
                    Address = address,
                    Extra = (uint)extra,
                    Value = ReadValue(memoryBytes, ref memoryOffset, valueSize, StoresPreviousValue(variableType)),
                });
            }

            return entries;
        }

        while (addressOffset + PointerSize <= addressBytes.Length)
        {
            var address = ReadPointer(addressBytes, addressOffset);
            addressOffset += PointerSize;
            entries.Add(new ScanResultEntry
            {
                Address = address,
                Value = ReadValue(memoryBytes, ref memoryOffset, valueSize, StoresPreviousValue(variableType)),
            });
        }

        return entries;
    }

    internal static int GetStoredValueSize(VariableType variableType, int byteSize, CustomType? customType)
    {
        return variableType switch
        {
            VariableType.Byte => 1,
            VariableType.Word => 2,
            VariableType.Dword or VariableType.Single => 4,
            VariableType.Qword or VariableType.Double => 8,
            VariableType.Pointer => PointerSize,
            VariableType.All => byteSize <= 0 ? 8 : byteSize,
            VariableType.Custom when customType is not null => customType.ByteSize,
            _ => byteSize,
        };
    }

    internal static int GetLookupValueSize(VariableType variableType, CustomType? customType)
    {
        return variableType switch
        {
            VariableType.Byte => 1,
            VariableType.Word => 2,
            VariableType.Dword or VariableType.Single => 4,
            VariableType.Qword or VariableType.Double => 8,
            VariableType.Pointer => PointerSize,
            VariableType.All => 8,
            VariableType.Custom when customType is not null => customType.ByteSize,
            _ => 0,
        };
    }

    internal static bool StoresPreviousValue(VariableType variableType)
    {
        return variableType is not VariableType.String
            and not VariableType.UnicodeString
            and not VariableType.ByteArray
            and not VariableType.ByteArrays
            and not VariableType.Binary
            and not VariableType.Grouped;
    }

    internal static string ReadHeader(Stream stream)
    {
        Span<byte> buffer = stackalloc byte[HeaderSize];
        stream.ReadExactly(buffer);
        return ReadHeader(buffer);
    }

    internal static string ReadHeader(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < HeaderSize)
        {
            return string.Empty;
        }

        var length = Math.Min(bytes[0], (byte)6);
        return Encoding.ASCII.GetString(bytes.Slice(1, length));
    }

    private static string NormalizeResultName(string resultName)
    {
        return string.IsNullOrWhiteSpace(resultName) ? "TMP" : resultName;
    }

    private static void WriteHeader(Stream stream, string value)
    {
        Span<byte> header = stackalloc byte[HeaderSize];
        var bytes = Encoding.ASCII.GetBytes(value);
        header[0] = (byte)Math.Min(bytes.Length, 6);
        bytes.AsSpan(0, Math.Min(bytes.Length, 6)).CopyTo(header[1..]);
        stream.Write(header);
    }

    private static void WriteMemoryRegion(BinaryWriter writer, BinaryScanMemoryRegion region, nint basePointer)
    {
        WritePointer(writer, region.BaseAddress);
        writer.Write(region.MemorySize);
        writer.Write(region.IsChild ? (byte)1 : (byte)0);

        var padding = GetMemoryRegionPadding();
        for (var index = 0; index < padding; index++)
        {
            writer.Write((byte)0);
        }

        var rawStartAddress = basePointer != 0 ? unchecked((nuint)((ulong)basePointer + region.StartOffset)) : region.StartOffset;
        WritePointer(writer, rawStartAddress);
    }

    private static int GetMemoryRegionSize()
    {
        return Marshal.SizeOf<MemoryRegion>();
    }

    private static int GetMemoryRegionPadding()
    {
        return checked((int)Marshal.OffsetOf<MemoryRegion>(nameof(MemoryRegion.StartAddress)) - (PointerSize + sizeof(ulong) + 1));
    }

    private static void WritePointer(BinaryWriter writer, nuint value)
    {
        if (PointerSize == sizeof(uint))
        {
            writer.Write((uint)value);
            return;
        }

        writer.Write((ulong)value);
    }

    private static nuint ReadPointer(ReadOnlySpan<byte> bytes, int offset)
    {
        return PointerSize == sizeof(uint)
            ? BinaryPrimitives.ReadUInt32LittleEndian(bytes.Slice(offset, sizeof(uint)))
            : unchecked((nuint)BinaryPrimitives.ReadUInt64LittleEndian(bytes.Slice(offset, sizeof(ulong))));
    }

    private static void WriteFixedValue(Stream stream, byte[] value, int valueSize)
    {
        if (valueSize <= 0)
        {
            return;
        }

        if (value.Length >= valueSize)
        {
            stream.Write(value, 0, valueSize);
            return;
        }

        stream.Write(value, 0, value.Length);
        if (value.Length == valueSize)
        {
            return;
        }

        Span<byte> padding = stackalloc byte[Math.Min(64, valueSize - value.Length)];
        var left = valueSize - value.Length;
        while (left > 0)
        {
            var chunk = Math.Min(left, padding.Length);
            stream.Write(padding[..chunk]);
            left -= chunk;
        }
    }

    private static byte[] ReadValue(ReadOnlySpan<byte> memoryBytes, ref int memoryOffset, int valueSize, bool shouldRead)
    {
        if (!shouldRead || valueSize <= 0)
        {
            return [];
        }

        if (memoryOffset + valueSize > memoryBytes.Length)
        {
            var remaining = Math.Max(0, memoryBytes.Length - memoryOffset);
            var value = new byte[valueSize];
            if (remaining > 0)
            {
                memoryBytes.Slice(memoryOffset, remaining).CopyTo(value);
                memoryOffset += remaining;
            }

            return value;
        }

        var result = memoryBytes.Slice(memoryOffset, valueSize).ToArray();
        memoryOffset += valueSize;
        return result;
    }
}