using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace CheatEngine.Library;

public static class ByteInterpreter
{
    public static Func<nuint, VariableType, VariableType>? OnAutoGuessRoutine { get; set; }

    public static bool IsHumanReadableInteger(int value)
    {
        return value is > -1000000 and < 1000000;
    }

    public static VariableType FindTypeOfData(nuint address, byte[] buffer, int size, CustomType? customType = null)
    {
        if (customType is not null && size == customType.ByteSize)
        {
            return VariableType.Custom;
        }

        var result = size switch
        {
            1 => VariableType.Byte,
            2 => VariableType.Word,
            4 => VariableType.Dword,
            8 => VariableType.Qword,
            _ => VariableType.ByteArray,
        };

        return OnAutoGuessRoutine?.Invoke(address, result) ?? result;
    }

    public static string DataToString(byte[] buffer, int size, VariableType variableType)
    {
        return variableType switch
        {
            VariableType.Byte => $"(byte){buffer[0]:X2}({buffer[0]})",
            VariableType.Word => $"(word){BinaryPrimitives.ReadUInt16LittleEndian(buffer):X4}({BinaryPrimitives.ReadUInt16LittleEndian(buffer)})",
            VariableType.Dword => $"(dword){BinaryPrimitives.ReadUInt32LittleEndian(buffer):X8}({BinaryPrimitives.ReadUInt32LittleEndian(buffer)})",
            VariableType.Qword => $"(qword){BinaryPrimitives.ReadUInt64LittleEndian(buffer):X16}({BinaryPrimitives.ReadUInt64LittleEndian(buffer)})",
            VariableType.Single => $"(float){BitConverter.ToSingle(buffer, 0):F2}",
            VariableType.Double => $"(double){BitConverter.ToDouble(buffer, 0):F2}",
            VariableType.String => Encoding.Default.GetString(buffer, 0, size),
            VariableType.UnicodeString => Encoding.Unicode.GetString(buffer, 0, size),
            VariableType.Pointer => $"(pointer){SymbolHandler.Default.GetNameFromAddress((nuint)(Environment.Is64BitProcess ? BinaryPrimitives.ReadUInt64LittleEndian(buffer) : BinaryPrimitives.ReadUInt32LittleEndian(buffer)))}",
            _ => Convert.ToHexString(buffer.AsSpan(0, size)),
        };
    }

    public static string ReadAndParsePointer(byte[] buffer, VariableType variableType, CustomType? customType = null, bool showAsHexadecimal = false, bool showAsSigned = false, int byteSize = 1)
    {
        return variableType switch
        {
            VariableType.Byte => showAsHexadecimal ? buffer[0].ToString("X2", CultureInfo.InvariantCulture) : (showAsSigned ? unchecked((sbyte)buffer[0]).ToString(CultureInfo.InvariantCulture) : buffer[0].ToString(CultureInfo.InvariantCulture)),
            VariableType.Word => showAsHexadecimal ? BinaryPrimitives.ReadUInt16LittleEndian(buffer).ToString("X4", CultureInfo.InvariantCulture) : (showAsSigned ? BinaryPrimitives.ReadInt16LittleEndian(buffer).ToString(CultureInfo.InvariantCulture) : BinaryPrimitives.ReadUInt16LittleEndian(buffer).ToString(CultureInfo.InvariantCulture)),
            VariableType.Dword => showAsHexadecimal ? BinaryPrimitives.ReadUInt32LittleEndian(buffer).ToString("X8", CultureInfo.InvariantCulture) : (showAsSigned ? BinaryPrimitives.ReadInt32LittleEndian(buffer).ToString(CultureInfo.InvariantCulture) : BinaryPrimitives.ReadUInt32LittleEndian(buffer).ToString(CultureInfo.InvariantCulture)),
            VariableType.Qword => showAsHexadecimal ? BinaryPrimitives.ReadUInt64LittleEndian(buffer).ToString("X16", CultureInfo.InvariantCulture) : (showAsSigned ? BinaryPrimitives.ReadInt64LittleEndian(buffer).ToString(CultureInfo.InvariantCulture) : BinaryPrimitives.ReadUInt64LittleEndian(buffer).ToString(CultureInfo.InvariantCulture)),
            VariableType.Single => showAsHexadecimal ? BinaryPrimitives.ReadUInt32LittleEndian(buffer).ToString("X8", CultureInfo.InvariantCulture) : BitConverter.ToSingle(buffer, 0).ToString(CultureInfo.InvariantCulture),
            VariableType.Double => showAsHexadecimal ? BinaryPrimitives.ReadUInt64LittleEndian(buffer).ToString("X16", CultureInfo.InvariantCulture) : BitConverter.ToDouble(buffer, 0).ToString(CultureInfo.InvariantCulture),
            VariableType.String => Encoding.Default.GetString(buffer, 0, byteSize).TrimEnd('\0'),
            VariableType.UnicodeString => Encoding.Unicode.GetString(buffer, 0, byteSize).TrimEnd('\0'),
            VariableType.ByteArray => FormatByteArray(buffer, byteSize, showAsHexadecimal, showAsSigned),
            VariableType.Custom when customType is not null => showAsHexadecimal && !customType.ScriptUsesFloat
                ? customType.ConvertDataToInteger(ToNativeInt(buffer)).ToString("X8", CultureInfo.InvariantCulture)
                : (customType.ScriptUsesFloat
                    ? customType.ConvertDataToFloat(ToNativeInt(buffer)).ToString(CultureInfo.InvariantCulture)
                    : customType.ConvertDataToInteger(ToNativeInt(buffer)).ToString(CultureInfo.InvariantCulture)),
            _ => "???",
        };
    }

    public static string ReadAndParseAddress(nuint address, VariableType variableType, CustomType? customType = null, bool showAsHexadecimal = false, bool showAsSigned = false, int byteSize = 1)
    {
        var size = variableType switch
        {
            VariableType.Byte => 1,
            VariableType.Word => 2,
            VariableType.Dword or VariableType.Single => 4,
            VariableType.Qword or VariableType.Double => 8,
            VariableType.String => byteSize,
            VariableType.UnicodeString => byteSize,
            VariableType.ByteArray => byteSize,
            VariableType.Custom when customType is not null => customType.ByteSize,
            _ => 0,
        };

        if (size <= 0)
        {
            return "???";
        }

        var buffer = new byte[size];
        if (!NewKernelHandler.ReadProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, buffer, out _))
        {
            return "???";
        }

        return ReadAndParsePointer(buffer, variableType, customType, showAsHexadecimal, showAsSigned, byteSize);
    }

    public static void ParseStringAndWriteToAddress(string value, nuint address, VariableType variableType, bool hexadecimal = false, CustomType? customType = null)
    {
        switch (variableType)
        {
            case VariableType.ByteArray:
            {
                var bytes = new List<int>();
                CeFuncProc.ConvertStringToBytes(value, hexadecimal, bytes);
                for (var index = 0; index < bytes.Count; index++)
                {
                    if (bytes[index] < 0)
                    {
                        continue;
                    }

                    Span<byte> oneByte = [(byte)bytes[index]];
                    NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address + (nuint)index, oneByte, out _);
                }

                return;
            }
            case VariableType.String:
                NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, Encoding.Default.GetBytes(value), out _);
                return;
            case VariableType.UnicodeString:
                NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, Encoding.Unicode.GetBytes(value), out _);
                return;
            case VariableType.Custom when customType is not null:
            {
                var buffer = new byte[customType.ByteSize];
                if (customType.ScriptUsesFloat)
                {
                    customType.ConvertFloatToData(float.Parse(value, CultureInfo.InvariantCulture), ToNativeInt(buffer));
                }
                else
                {
                    customType.ConvertIntegerToData((int)CeFuncProc.StrToQWordEx(hexadecimal ? "$" + value : value), ToNativeInt(buffer));
                }

                NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, buffer, out _);
                return;
            }
        }

        Span<byte> payload = variableType switch
        {
            VariableType.Byte => [(byte)CeFuncProc.StrToQWordEx(hexadecimal ? "$" + value : value)],
            VariableType.Word => BitConverter.GetBytes((ushort)CeFuncProc.StrToQWordEx(hexadecimal ? "$" + value : value)),
            VariableType.Dword => BitConverter.GetBytes((uint)CeFuncProc.StrToQWordEx(hexadecimal ? "$" + value : value)),
            VariableType.Qword => BitConverter.GetBytes(CeFuncProc.StrToQWordEx(hexadecimal ? "$" + value : value)),
            VariableType.Single => BitConverter.GetBytes(float.Parse(value, CultureInfo.InvariantCulture)),
            VariableType.Double => BitConverter.GetBytes(double.Parse(value, CultureInfo.InvariantCulture)),
            _ => Span<byte>.Empty,
        };

        if (!payload.IsEmpty)
        {
            NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, address, payload, out _);
        }
    }

    private static string FormatByteArray(byte[] buffer, int byteSize, bool showAsHexadecimal, bool showAsSigned)
    {
        var builder = new StringBuilder();
        for (var index = 0; index < byteSize; index++)
        {
            if (index > 0)
            {
                builder.Append(' ');
            }

            builder.Append(showAsHexadecimal
                ? buffer[index].ToString("X2", CultureInfo.InvariantCulture)
                : (showAsSigned ? unchecked((sbyte)buffer[index]).ToString(CultureInfo.InvariantCulture) : buffer[index].ToString(CultureInfo.InvariantCulture)));
        }

        return builder.ToString();
    }

    private static nint ToNativeInt(byte[] buffer)
    {
        unsafe
        {
            fixed (byte* pointer = buffer)
            {
                return (nint)pointer;
            }
        }
    }
}