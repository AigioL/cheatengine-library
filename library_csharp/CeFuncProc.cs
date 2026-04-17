using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;

namespace CheatEngine.Library;

public enum ScanOption
{
    UnknownValue = 0,
    ExactValue = 1,
    ValueBetween = 2,
    BiggerThan = 3,
    SmallerThan = 4,
    IncreasedValue = 5,
    IncreasedValueBy = 6,
    DecreasedValue = 7,
    DecreasedValueBy = 8,
    Changed = 9,
    Unchanged = 10,
    Custom = 11,
}

public enum ScanType
{
    NewScan,
    FirstScan,
    NextScan,
}

public enum RoundingType
{
    Rounded = 0,
    ExtremeRounded = 1,
    Truncated = 2,
}

public enum VariableType
{
    Byte = 0,
    Word = 1,
    Dword = 2,
    Qword = 3,
    Single = 4,
    Double = 5,
    String = 6,
    UnicodeString = 7,
    ByteArray = 8,
    Binary = 9,
    All = 10,
    AutoAssembler = 11,
    Pointer = 12,
    Custom = 13,
    Grouped = 14,
    ByteArrays = 15,
}

public enum CustomScanType
{
    None,
    AutoAssembler,
    Cpp,
    DllFunction,
}

public enum FastScanMethod
{
    NotAligned = 0,
    Aligned = 1,
    LastDigits = 2,
}

public enum AccessRight
{
    Execute,
    Read,
    Write,
}

[StructLayout(LayoutKind.Sequential, Pack = 2, Size = 10)]
public readonly record struct KeyCombo
{
    public readonly ushort Key0;
    public readonly ushort Key1;
    public readonly ushort Key2;
    public readonly ushort Key3;
    public readonly ushort Key4;

    public KeyCombo(ushort key0, ushort key1 = 0, ushort key2 = 0, ushort key3 = 0, ushort key4 = 0)
    {
        Key0 = key0;
        Key1 = key1;
        Key2 = key2;
        Key3 = key3;
        Key4 = key4;
    }

    public ushort this[int index] => index switch
    {
        0 => Key0,
        1 => Key1,
        2 => Key2,
        3 => Key3,
        4 => Key4,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    public ushort[] ToArray() => [Key0, Key1, Key2, Key3, Key4];
}

[StructLayout(LayoutKind.Sequential)]
public readonly record struct MemoryRegion
{
    public readonly nuint BaseAddress;
    public readonly ulong MemorySize;

    [MarshalAs(UnmanagedType.I1)]
    public readonly bool IsChild;

    public readonly nint StartAddress;

    public MemoryRegion(nuint baseAddress, ulong memorySize, bool isChild, nint startAddress)
    {
        BaseAddress = baseAddress;
        MemorySize = memorySize;
        IsChild = isChild;
        StartAddress = startAddress;
    }
}

public sealed record GroupAddress(nuint Address, IReadOnlyList<uint> Offsets);

[StructLayout(LayoutKind.Sequential)]
public readonly record struct BitAddress
{
    public readonly nuint Address;
    public readonly nuint Bit;

    public BitAddress(nuint address, nuint bit)
    {
        Address = address;
        Bit = bit;
    }
}

public sealed record CePointer(nuint Address, string InterpretableAddress, int Offset);

public sealed class ModuleData
{
    public nuint ModuleAddress { get; set; }

    public uint ModuleSize { get; set; }
}

public sealed record CeAlloc(nuint Address, string? VarName, uint Size, nuint Preferred);

public static class CeFuncProc
{
    public static ProcessHandler ProcessHandler => ProcessHandlerUnit.Current;

    public static ulong StrToQWordEx(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new FormatException("Invalid integer");
        }

        var trimmed = value.Trim();
        if (trimmed.StartsWith("-$", StringComparison.Ordinal))
        {
            var negativeHex = ulong.Parse(trimmed[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return unchecked((ulong)-checked((long)negativeHex));
        }

        if (trimmed.StartsWith("-0x", StringComparison.OrdinalIgnoreCase))
        {
            var negativeHex = ulong.Parse(trimmed[3..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
            return unchecked((ulong)-checked((long)negativeHex));
        }

        if (trimmed[0] == '-')
        {
            return unchecked((ulong)long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture));
        }

        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            return ulong.Parse(trimmed[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return ulong.Parse(trimmed[2..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return ulong.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture);
    }

    public static VariableType OldVarTypeToNewVarType(int value)
    {
        return value switch
        {
            0 => VariableType.Byte,
            1 => VariableType.Word,
            2 => VariableType.Dword,
            3 => VariableType.Single,
            4 => VariableType.Double,
            5 => VariableType.Binary,
            6 => VariableType.Qword,
            7 => VariableType.String,
            8 => VariableType.ByteArray,
            10 => VariableType.Custom,
            255 => VariableType.AutoAssembler,
            _ => VariableType.Dword,
        };
    }

    public static string VariableTypeToString(VariableType variableType)
    {
        return variableType switch
        {
            VariableType.All => "All",
            VariableType.Binary => "Binary",
            VariableType.ByteArray => "Array of byte",
            VariableType.Byte => "Byte",
            VariableType.Word => "2 Bytes",
            VariableType.Dword => "4 Bytes",
            VariableType.Qword => "8 Bytes",
            VariableType.Single => "Float",
            VariableType.Double => "Double",
            VariableType.String => "String",
            VariableType.UnicodeString => "Unicode String",
            VariableType.Pointer => "Pointer",
            VariableType.AutoAssembler => "Auto Assembler Script",
            VariableType.Custom => "Custom",
            VariableType.Grouped => "Grouped",
            VariableType.ByteArrays => "Array of byte arrays",
            _ => variableType.ToString(),
        };
    }

    public static VariableType StringToVariableType(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "all" => VariableType.All,
            "binary" => VariableType.Binary,
            "array of byte" => VariableType.ByteArray,
            "byte" => VariableType.Byte,
            "2 bytes" => VariableType.Word,
            "4 bytes" => VariableType.Dword,
            "8 bytes" => VariableType.Qword,
            "float" => VariableType.Single,
            "double" => VariableType.Double,
            "string" => VariableType.String,
            "unicode string" => VariableType.UnicodeString,
            "pointer" => VariableType.Pointer,
            "custom" => VariableType.Custom,
            "grouped" => VariableType.Grouped,
            "auto assembler script" => VariableType.AutoAssembler,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown variable type."),
        };
    }

    public static string ConvertHexStrToRealStr(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return value.StartsWith('$') ? value[1..] : value;
    }

    public static int HexStrToInt(string value)
    {
        return int.Parse(ConvertHexStrToRealStr(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static long HexStrToInt64(string value)
    {
        return long.Parse(ConvertHexStrToRealStr(value), NumberStyles.HexNumber, CultureInfo.InvariantCulture);
    }

    public static string IntToHexSigned(long value, int digits)
    {
        return value < 0
            ? "-" + unchecked((ulong)(-value)).ToString($"X{digits}", CultureInfo.InvariantCulture)
            : unchecked((ulong)value).ToString($"X{digits}", CultureInfo.InvariantCulture);
    }

    public static string KeyToStr(ushort key)
    {
        return key switch
        {
            0 => string.Empty,
            0x10 => "Shift",
            0x11 => "Ctrl",
            0x12 => "Alt",
            _ when key >= 0x70 && key <= 0x87 => $"F{key - 0x6F}",
            _ when key >= (ushort)'A' && key <= (ushort)'Z' => ((char)key).ToString(),
            _ when key >= (ushort)'0' && key <= (ushort)'9' => ((char)key).ToString(),
            _ => key.ToString(CultureInfo.InvariantCulture),
        };
    }

    public static string ConvertKeyComboToString(KeyCombo combo)
    {
        var parts = new List<string>(5);
        foreach (var key in combo.ToArray())
        {
            if (key != 0)
            {
                parts.Add(KeyToStr(key));
            }
        }

        return string.Join('+', parts);
    }

    public static void ConvertStringToBytes(string scanValue, bool hex, List<int> bytes)
    {
        ArgumentNullException.ThrowIfNull(scanValue);
        ArgumentNullException.ThrowIfNull(bytes);

        bytes.Clear();
        if (!hex)
        {
            foreach (var character in scanValue)
            {
                bytes.Add(character);
            }

            return;
        }

        foreach (var part in scanValue.Split([' ', '-', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (part == "*" || part == "??")
            {
                bytes.Add(-1);
                continue;
            }

            bytes.Add(int.Parse(part, NumberStyles.HexNumber, CultureInfo.InvariantCulture));
        }
    }

    public static int GetBitCount(ulong value)
    {
        var count = 0;
        while (value != 0)
        {
            value &= value - 1;
            count++;
        }

        return count;
    }

    public static int GetBit(int bitNumber, ulong value)
    {
        return (int)((value >> bitNumber) & 1UL);
    }

    public static byte SetBit(int bitNumber, byte value, int state)
    {
        if (state == 0)
        {
            return (byte)(value & ~(1 << bitNumber));
        }

        return (byte)(value | (1 << bitNumber));
    }

    public static uint SetBit(int bitNumber, uint value, int state)
    {
        if (state == 0)
        {
            return value & ~(1U << bitNumber);
        }

        return value | (1U << bitNumber);
    }

    public static ulong SetBit(int bitNumber, ulong value, int state)
    {
        if (state == 0)
        {
            return value & ~(1UL << bitNumber);
        }

        return value | (1UL << bitNumber);
    }

    public static bool InRangeQ(ulong value, ulong min, ulong max)
    {
        return value >= min && value <= max;
    }

    public static bool InRangeX(nuint value, nuint min, nuint max)
    {
        return value >= min && value <= max;
    }

    public static nuint MinX(nuint left, nuint right)
    {
        return left < right ? left : right;
    }

    public static nuint MaxX(nuint left, nuint right)
    {
        return left > right ? left : right;
    }

    public static string ByteStringToText(string value, bool hex)
    {
        var bytes = new List<int>();
        ConvertStringToBytes(value, hex, bytes);
        var builder = new StringBuilder(bytes.Count);
        foreach (var item in bytes)
        {
            if (item >= 0)
            {
                builder.Append((char)item);
            }
        }

        return builder.ToString();
    }
}