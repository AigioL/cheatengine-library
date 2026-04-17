using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Linq;
using System.Text;

namespace CheatEngine.Library;

public enum AssemblerPreference
{
    None,
    X86,
    X64,
}

internal sealed record RegisterOperand(string Name, int Code, int Size, bool Extended);

internal sealed record MemoryOperand(RegisterOperand BaseRegister, int Displacement);

public static class AssemblerUnit
{
    private static readonly Dictionary<string, RegisterOperand> Registers = BuildRegisters();
    private static readonly string[] KnownMnemonics = ["db", "dw", "dd", "dq", "nop", "ret", "int3", "push", "pop", "jmp", "call", "mov", "add", "sub", "cmp", "test", "lea"];

    public static int GetOpcodesIndex(string opcode)
    {
        if (string.IsNullOrWhiteSpace(opcode))
        {
            return -1;
        }

        var tableDrivenIndex = PascalAssemblerEngine.GetOpcodesIndex(opcode);
        if (tableDrivenIndex >= 0)
        {
            return tableDrivenIndex;
        }

        var mnemonic = GetMnemonic(opcode);
        return Array.FindIndex(KnownMnemonics, item => string.Equals(item, mnemonic, StringComparison.OrdinalIgnoreCase));
    }

    public static bool Assemble(string opcode, nuint address, out byte[] bytes, AssemblerPreference assemblerPreference = AssemblerPreference.None, bool skipRangeCheck = false)
    {
        if (PascalAssemblerEngine.TryAssemble(opcode, address, out bytes, skipRangeCheck))
        {
            return true;
        }

        try
        {
            bytes = AssembleInternal(opcode, address, assemblerPreference);
            return true;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    internal static int GetEstimatedSize(string opcode, AssemblerPreference assemblerPreference = AssemblerPreference.None)
    {
        if (Assemble(opcode, 0, out var bytes, assemblerPreference, skipRangeCheck: true))
        {
            return bytes.Length;
        }

        var mnemonic = GetMnemonic(opcode);
        return mnemonic.ToLowerInvariant() switch
        {
            "db" => SplitArguments(GetOperands(opcode)).Count,
            "dw" => SplitArguments(GetOperands(opcode)).Count * 2,
            "dd" => SplitArguments(GetOperands(opcode)).Count * 4,
            "dq" => SplitArguments(GetOperands(opcode)).Count * 8,
            "jmp" or "call" => 5,
            "push" or "pop" => 1,
            "nop" or "ret" or "int3" => 1,
            _ => 8,
        };
    }

    private static byte[] AssembleInternal(string opcode, nuint address, AssemblerPreference assemblerPreference)
    {
        ArgumentNullException.ThrowIfNull(opcode);

        var trimmed = opcode.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var mnemonic = GetMnemonic(trimmed).ToLowerInvariant();
        var operands = SplitArguments(GetOperands(trimmed));
        return mnemonic switch
        {
            "db" => AssembleDataDirective(operands, 1),
            "dw" => AssembleDataDirective(operands, 2),
            "dd" => AssembleDataDirective(operands, 4),
            "dq" => AssembleDataDirective(operands, 8),
            "nop" => [0x90],
            "ret" => [0xC3],
            "int3" => [0xCC],
            "push" => AssemblePushPop(operands, push: true),
            "pop" => AssemblePushPop(operands, push: false),
            "jmp" => AssembleBranch(operands, address, call: false),
            "call" => AssembleBranch(operands, address, call: true),
            "mov" => AssembleMov(operands, address),
            "add" => AssembleArithmetic(operands, 0),
            "sub" => AssembleArithmetic(operands, 5),
            "cmp" => AssembleArithmetic(operands, 7),
            "test" => AssembleTest(operands),
            "lea" => AssembleLea(operands),
            _ => throw new InvalidOperationException($"Unsupported instruction: {opcode}"),
        };
    }

    private static byte[] AssembleDataDirective(IReadOnlyList<string> operands, int itemSize)
    {
        var bytes = new List<byte>();
        foreach (var operand in ExpandDirectiveOperands(operands))
        {
            var trimmed = operand.Trim();
            if (trimmed.StartsWith('"') && trimmed.EndsWith('"') && itemSize == 1)
            {
                bytes.AddRange(Encoding.Default.GetBytes(trimmed[1..^1]));
                continue;
            }

            var value = ParseImmediate(trimmed);
            switch (itemSize)
            {
                case 1:
                    bytes.Add((byte)value);
                    break;
                case 2:
                    bytes.AddRange(BitConverter.GetBytes((ushort)value));
                    break;
                case 4:
                    bytes.AddRange(BitConverter.GetBytes((uint)value));
                    break;
                case 8:
                    bytes.AddRange(BitConverter.GetBytes(value));
                    break;
            }
        }

        return bytes.ToArray();
    }

    private static IEnumerable<string> ExpandDirectiveOperands(IReadOnlyList<string> operands)
    {
        foreach (var operand in operands)
        {
            var trimmed = operand.Trim();
            if (trimmed.Length == 0)
            {
                continue;
            }

            if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
            {
                yield return trimmed;
                continue;
            }

            foreach (var part in trimmed.Split([' ', '\t'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                yield return part;
            }
        }
    }

    private static byte[] AssemblePushPop(IReadOnlyList<string> operands, bool push)
    {
        if (operands.Count != 1)
        {
            throw new InvalidOperationException("push/pop require one operand");
        }

        var register = ParseRegister(operands[0]);
        var bytes = new List<byte>();
        EmitRex(bytes, register.Size == 64, 0, 0, register.Code);
        bytes.Add((byte)((push ? 0x50 : 0x58) + (register.Code & 7)));
        return bytes.ToArray();
    }

    private static byte[] AssembleBranch(IReadOnlyList<string> operands, nuint address, bool call)
    {
        if (operands.Count != 1)
        {
            throw new InvalidOperationException("jmp/call require one operand");
        }

        var operand = operands[0].Trim();
        if (TryParseRegister(operand, out var register))
        {
            var bytes = new List<byte>();
            EmitRex(bytes, register.Size == 64, 0, 0, register.Code);
            bytes.Add(0xFF);
            bytes.Add((byte)(0xC0 | ((call ? 2 : 4) << 3) | (register.Code & 7)));
            return bytes.ToArray();
        }

        var target = ParseImmediate(operand);
        var displacement = unchecked((long)target - ((long)address + 5));
        if (displacement >= int.MinValue && displacement <= int.MaxValue)
        {
            var bytes = new List<byte> { call ? (byte)0xE8 : (byte)0xE9 };
            bytes.AddRange(BitConverter.GetBytes((int)displacement));
            return bytes.ToArray();
        }

        if (!call)
        {
            var bytes = new List<byte> { 0xFF, 0x25, 0x00, 0x00, 0x00, 0x00 };
            bytes.AddRange(BitConverter.GetBytes(target));
            return bytes.ToArray();
        }

        throw new InvalidOperationException("Call target is out of range");
    }

    private static byte[] AssembleMov(IReadOnlyList<string> operands, nuint address)
    {
        if (operands.Count != 2)
        {
            throw new InvalidOperationException("mov requires two operands");
        }

        var left = operands[0].Trim();
        var right = operands[1].Trim();
        if (TryParseRegister(left, out var destination) && !right.StartsWith('['))
        {
            if (TryParseRegister(right, out var sourceRegister))
            {
                return AssembleRegisterToRegister(0x8B, destination, sourceRegister);
            }

            var bytes = new List<byte>();
            EmitRex(bytes, destination.Size == 64, 0, 0, destination.Code);
            bytes.Add((byte)(0xB8 + (destination.Code & 7)));
            if (destination.Size == 64)
            {
                bytes.AddRange(BitConverter.GetBytes(ParseImmediate(right)));
            }
            else
            {
                bytes.AddRange(BitConverter.GetBytes((uint)ParseImmediate(right)));
            }

            return bytes.ToArray();
        }

        if (TryParseRegister(left, out destination) && TryParseMemory(right, out var sourceMemory))
        {
            return AssembleRegisterMemoryTransfer(0x8B, destination, sourceMemory);
        }

        if (TryParseMemory(left, out var destinationMemory) && TryParseRegister(right, out var source))
        {
            return AssembleRegisterMemoryTransfer(0x89, source, destinationMemory);
        }

        throw new InvalidOperationException("Unsupported mov operands");
    }

    private static byte[] AssembleArithmetic(IReadOnlyList<string> operands, int groupCode)
    {
        if (operands.Count != 2)
        {
            throw new InvalidOperationException("Instruction requires two operands");
        }

        var register = ParseRegister(operands[0]);
        var immediate = unchecked((long)ParseImmediate(operands[1]));
        var bytes = new List<byte>();
        EmitRex(bytes, register.Size == 64, groupCode, 0, register.Code);
        if (immediate >= sbyte.MinValue && immediate <= sbyte.MaxValue)
        {
            bytes.Add(0x83);
            bytes.Add((byte)(0xC0 | (groupCode << 3) | (register.Code & 7)));
            bytes.Add((byte)(sbyte)immediate);
        }
        else
        {
            bytes.Add(0x81);
            bytes.Add((byte)(0xC0 | (groupCode << 3) | (register.Code & 7)));
            bytes.AddRange(BitConverter.GetBytes((int)immediate));
        }

        return bytes.ToArray();
    }

    private static byte[] AssembleTest(IReadOnlyList<string> operands)
    {
        if (operands.Count != 2)
        {
            throw new InvalidOperationException("test requires two operands");
        }

        var left = ParseRegister(operands[0]);
        var right = ParseRegister(operands[1]);
        return AssembleRegisterToRegister(0x85, left, right);
    }

    private static byte[] AssembleLea(IReadOnlyList<string> operands)
    {
        if (operands.Count != 2)
        {
            throw new InvalidOperationException("lea requires two operands");
        }

        var destination = ParseRegister(operands[0]);
        var memory = ParseMemory(operands[1]);
        return AssembleRegisterMemoryTransfer(0x8D, destination, memory);
    }

    private static byte[] AssembleRegisterToRegister(byte opcode, RegisterOperand destination, RegisterOperand source)
    {
        var bytes = new List<byte>();
        EmitRex(bytes, destination.Size == 64 || source.Size == 64, destination.Code, 0, source.Code);
        bytes.Add(opcode);
        bytes.Add((byte)(0xC0 | ((destination.Code & 7) << 3) | (source.Code & 7)));
        return bytes.ToArray();
    }

    private static byte[] AssembleRegisterMemoryTransfer(byte opcode, RegisterOperand register, MemoryOperand memory)
    {
        var bytes = new List<byte>();
        EmitRex(bytes, register.Size == 64 || memory.BaseRegister.Size == 64, register.Code, 0, memory.BaseRegister.Code);
        bytes.Add(opcode);
        EmitMemoryOperand(bytes, register.Code, memory);
        return bytes.ToArray();
    }

    private static void EmitMemoryOperand(List<byte> bytes, int registerCode, MemoryOperand memory)
    {
        var rm = memory.BaseRegister.Code & 7;
        var displacement = memory.Displacement;
        var needsSib = rm == 4;
        if (displacement == 0 && rm != 5)
        {
            bytes.Add((byte)(((0) << 6) | ((registerCode & 7) << 3) | rm));
            if (needsSib)
            {
                bytes.Add((byte)((4 << 3) | rm));
            }

            return;
        }

        if (displacement >= sbyte.MinValue && displacement <= sbyte.MaxValue)
        {
            bytes.Add((byte)(((1) << 6) | ((registerCode & 7) << 3) | rm));
            if (needsSib)
            {
                bytes.Add((byte)((4 << 3) | rm));
            }

            bytes.Add((byte)(sbyte)displacement);
            return;
        }

        bytes.Add((byte)(((2) << 6) | ((registerCode & 7) << 3) | rm));
        if (needsSib)
        {
            bytes.Add((byte)((4 << 3) | rm));
        }

        bytes.AddRange(BitConverter.GetBytes(displacement));
    }

    private static void EmitRex(List<byte> bytes, bool wide, int reg, int index, int rm)
    {
        byte rex = 0x40;
        if (wide)
        {
            rex |= 0x08;
        }

        if ((reg & 8) != 0)
        {
            rex |= 0x04;
        }

        if ((index & 8) != 0)
        {
            rex |= 0x02;
        }

        if ((rm & 8) != 0)
        {
            rex |= 0x01;
        }

        if (rex != 0x40)
        {
            bytes.Add(rex);
        }
    }

    private static RegisterOperand ParseRegister(string value)
    {
        if (!TryParseRegister(value, out var register))
        {
            throw new InvalidOperationException($"Unknown register {value}");
        }

        return register;
    }

    private static bool TryParseRegister(string value, [NotNullWhen(true)] out RegisterOperand? register)
    {
        return Registers.TryGetValue(value.Trim().ToLowerInvariant(), out register);
    }

    private static MemoryOperand ParseMemory(string value)
    {
        if (!TryParseMemory(value, out var memory))
        {
            throw new InvalidOperationException($"Unsupported memory operand {value}");
        }

        return memory;
    }

    private static bool TryParseMemory(string value, [NotNullWhen(true)] out MemoryOperand? memory)
    {
        var trimmed = value.Trim();
        if (!trimmed.StartsWith('[') || !trimmed.EndsWith(']'))
        {
            memory = null;
            return false;
        }

        var inner = trimmed[1..^1].Replace(" ", string.Empty);
        var plusIndex = inner.IndexOf('+');
        var minusIndex = inner.LastIndexOf('-');
        var splitIndex = plusIndex >= 0 ? plusIndex : minusIndex;
        var registerName = splitIndex >= 0 ? inner[..splitIndex] : inner;
        if (!TryParseRegister(registerName, out var baseRegister))
        {
            memory = null;
            return false;
        }

        var displacement = 0;
        if (splitIndex >= 0)
        {
            displacement = ParseSignedImmediate(inner[splitIndex..]);
        }

        memory = new MemoryOperand(baseRegister, displacement);
        return true;
    }

    private static ulong ParseImmediate(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith('+'))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.StartsWith('-'))
        {
            return unchecked((ulong)long.Parse(trimmed, NumberStyles.Integer, CultureInfo.InvariantCulture));
        }

        return CeFuncProc.StrToQWordEx(trimmed);
    }

    private static int ParseSignedImmediate(string value)
    {
        return value.StartsWith('-')
            ? int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture)
            : checked((int)ParseImmediate(value.TrimStart('+')));
    }

    private static string GetMnemonic(string opcode)
    {
        var index = opcode.IndexOf(' ');
        return index < 0 ? opcode.Trim() : opcode[..index].Trim();
    }

    private static string GetOperands(string opcode)
    {
        var index = opcode.IndexOf(' ');
        return index < 0 ? string.Empty : opcode[(index + 1)..].Trim();
    }

    private static List<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(arguments))
        {
            return result;
        }

        var builder = new StringBuilder();
        var bracketDepth = 0;
        var inString = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inString = !inString;
            }

            if (!inString)
            {
                if (character == '[')
                {
                    bracketDepth++;
                }
                else if (character == ']')
                {
                    bracketDepth--;
                }
                else if (character == ',' && bracketDepth == 0)
                {
                    result.Add(builder.ToString().Trim());
                    builder.Clear();
                    continue;
                }
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString().Trim());
        }

        return result;
    }

    private static Dictionary<string, RegisterOperand> BuildRegisters()
    {
        var registers = new Dictionary<string, RegisterOperand>(StringComparer.OrdinalIgnoreCase);
        AddRegisterSet(registers, 64, ["rax", "rcx", "rdx", "rbx", "rsp", "rbp", "rsi", "rdi", "r8", "r9", "r10", "r11", "r12", "r13", "r14", "r15"]);
        AddRegisterSet(registers, 32, ["eax", "ecx", "edx", "ebx", "esp", "ebp", "esi", "edi", "r8d", "r9d", "r10d", "r11d", "r12d", "r13d", "r14d", "r15d"]);
        AddRegisterSet(registers, 16, ["ax", "cx", "dx", "bx", "sp", "bp", "si", "di", "r8w", "r9w", "r10w", "r11w", "r12w", "r13w", "r14w", "r15w"]);
        AddRegisterSet(registers, 8, ["al", "cl", "dl", "bl", "spl", "bpl", "sil", "dil", "r8b", "r9b", "r10b", "r11b", "r12b", "r13b", "r14b", "r15b"]);
        return registers;
    }

    private static void AddRegisterSet(Dictionary<string, RegisterOperand> registers, int size, IReadOnlyList<string> names)
    {
        for (var index = 0; index < names.Count; index++)
        {
            registers[names[index]] = new RegisterOperand(names[index], index, size, index >= 8);
        }
    }
}