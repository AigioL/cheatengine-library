using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection.PortableExecutable;
using System.Text;

namespace CheatEngine.Library;

public static class PeInfoFunctions
{
    public static PeImageInfo ReadImageInfo(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var reader = new PEReader(stream, PEStreamOptions.LeaveOpen);
        var headers = reader.PEHeaders;
        var peHeader = headers.PEHeader ?? throw new InvalidDataException("This is not a valid image");
        var info = new PeImageInfo
        {
            FilePath = filePath,
            Is64Bit = headers.PEHeader?.Magic == PEMagic.PE32Plus,
            ImageBase = (ulong)peHeader.ImageBase,
            EntryPoint = peHeader.AddressOfEntryPoint,
            CodeBase = peHeader.BaseOfCode,
            CodeSize = peHeader.SizeOfCode,
            HeaderSize = peHeader.SizeOfHeaders,
        };

        foreach (var section in headers.SectionHeaders)
        {
            info.Sections.Add(new PeSectionInfo
            {
                Name = section.Name,
                VirtualAddress = section.VirtualAddress,
                VirtualSize = section.VirtualSize,
                SizeOfRawData = section.SizeOfRawData,
                PointerToRawData = section.PointerToRawData,
                Characteristics = (uint)section.SectionCharacteristics,
            });
        }

        var image = reader.GetEntireImage().GetContent().ToArray();
        ReadExports(image, headers, info);
        ReadImports(image, headers, info);
        return info;
    }

    public static int GetCodeSize(ReadOnlySpan<byte> header)
    {
        return TryReadHeaderValue(header, 0x1C, out var value) ? value : 0;
    }

    public static int GetEntryPoint(ReadOnlySpan<byte> header)
    {
        return TryReadHeaderValue(header, 0x10, out var value) ? value : 0;
    }

    public static int GetCodeBase(ReadOnlySpan<byte> header)
    {
        return TryReadHeaderValue(header, 0x14, out var value) ? value : 0;
    }

    public static int GetDataBase(ReadOnlySpan<byte> header)
    {
        return TryReadHeaderValue(header, 0x18, out var value) ? value : 0;
    }

    public static int GetHeaderSize(ReadOnlySpan<byte> header)
    {
        return TryReadHeaderValue(header, 0x3C, out _) && header.Length >= 0x60
            ? BinaryPrimitives.ReadInt32LittleEndian(header[0x54..0x58])
            : 0;
    }

    public static IReadOnlyList<PeExportEntry> GetExportList(string filePath)
    {
        return ReadImageInfo(filePath).Exports;
    }

    private static bool TryReadHeaderValue(ReadOnlySpan<byte> header, int optionalHeaderOffset, out int value)
    {
        value = 0;
        if (header.Length < 0x40 || BinaryPrimitives.ReadUInt16LittleEndian(header) != 0x5A4D)
        {
            return false;
        }

        var peOffset = BinaryPrimitives.ReadInt32LittleEndian(header[0x3C..0x40]);
        var optionalOffset = peOffset + 0x18 + optionalHeaderOffset;
        if (optionalOffset + 4 > header.Length)
        {
            return false;
        }

        value = BinaryPrimitives.ReadInt32LittleEndian(header.Slice(optionalOffset, 4));
        return true;
    }

    private static void ReadExports(byte[] image, PEHeaders headers, PeImageInfo info)
    {
        var exportDirectory = headers.PEHeader?.ExportTableDirectory;
        if (exportDirectory is null || exportDirectory.Value.RelativeVirtualAddress == 0 || exportDirectory.Value.Size == 0)
        {
            return;
        }

        var exportOffset = RvaToOffset(headers, exportDirectory.Value.RelativeVirtualAddress);
        if (exportOffset < 0 || exportOffset + 40 > image.Length)
        {
            return;
        }

        var baseOrdinal = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 16, 4));
        var numberOfFunctions = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 20, 4));
        var numberOfNames = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 24, 4));
        var addressOfFunctions = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 28, 4));
        var addressOfNames = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 32, 4));
        var addressOfOrdinals = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(exportOffset + 36, 4));

        for (var index = 0; index < numberOfNames; index++)
        {
            var nameRvaOffset = RvaToOffset(headers, (int)addressOfNames + (index * 4));
            var nameRva = BinaryPrimitives.ReadInt32LittleEndian(image.AsSpan(nameRvaOffset, 4));
            var ordinalOffset = RvaToOffset(headers, (int)addressOfOrdinals + (index * 2));
            var ordinal = BinaryPrimitives.ReadUInt16LittleEndian(image.AsSpan(ordinalOffset, 2));
            var functionOffset = RvaToOffset(headers, (int)addressOfFunctions + (ordinal * 4));
            var functionRva = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(functionOffset, 4));
            info.Exports.Add(new PeExportEntry
            {
                Name = ReadNullTerminatedAscii(image, RvaToOffset(headers, (int)nameRva)),
                Ordinal = baseOrdinal + ordinal,
                Address = info.ImageBase + functionRva,
            });
        }

        if (numberOfNames == 0 && numberOfFunctions > 0)
        {
            for (var index = 0; index < numberOfFunctions; index++)
            {
                var functionOffset = RvaToOffset(headers, (int)addressOfFunctions + (index * 4));
                var functionRva = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(functionOffset, 4));
                info.Exports.Add(new PeExportEntry
                {
                    Name = $"Ordinal_{baseOrdinal + index}",
                    Ordinal = baseOrdinal + (uint)index,
                    Address = info.ImageBase + functionRva,
                });
            }
        }
    }

    private static void ReadImports(byte[] image, PEHeaders headers, PeImageInfo info)
    {
        var importDirectory = headers.PEHeader?.ImportTableDirectory;
        if (importDirectory is null || importDirectory.Value.RelativeVirtualAddress == 0 || importDirectory.Value.Size == 0)
        {
            return;
        }

        var descriptorOffset = RvaToOffset(headers, importDirectory.Value.RelativeVirtualAddress);
        while (descriptorOffset >= 0 && descriptorOffset + 20 <= image.Length)
        {
            var nameRva = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(descriptorOffset + 12, 4));
            var firstThunk = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(descriptorOffset + 16, 4));
            if (nameRva == 0 && firstThunk == 0)
            {
                break;
            }

            var moduleName = ReadNullTerminatedAscii(image, RvaToOffset(headers, (int)nameRva));
            var thunkOffset = RvaToOffset(headers, (int)firstThunk);
            if (thunkOffset < 0)
            {
                descriptorOffset += 20;
                continue;
            }

            while (thunkOffset + 4 <= image.Length)
            {
                var thunkData = BinaryPrimitives.ReadUInt32LittleEndian(image.AsSpan(thunkOffset, 4));
                if (thunkData == 0)
                {
                    break;
                }

                if ((thunkData & 0x80000000U) == 0)
                {
                    var importByNameOffset = RvaToOffset(headers, (int)thunkData);
                    if (importByNameOffset >= 0 && importByNameOffset + 2 < image.Length)
                    {
                        info.Imports.Add(new PeImportEntry
                        {
                            ModuleName = moduleName,
                            Name = ReadNullTerminatedAscii(image, importByNameOffset + 2),
                        });
                    }
                }

                thunkOffset += 4;
            }

            descriptorOffset += 20;
        }
    }

    private static int RvaToOffset(PEHeaders headers, int rva)
    {
        foreach (var section in headers.SectionHeaders)
        {
            var start = section.VirtualAddress;
            var end = start + Math.Max(section.VirtualSize, section.SizeOfRawData);
            if (rva >= start && rva < end)
            {
                return section.PointerToRawData + (rva - start);
            }
        }

        return -1;
    }

    private static string ReadNullTerminatedAscii(byte[] image, int offset)
    {
        if (offset < 0 || offset >= image.Length)
        {
            return string.Empty;
        }

        var end = offset;
        while (end < image.Length && image[end] != 0)
        {
            end++;
        }

        return Encoding.ASCII.GetString(image, offset, end - offset);
    }
}