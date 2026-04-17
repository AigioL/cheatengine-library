using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace CheatEngine.Library;

internal enum PascalTokenType
{
    InvalidToken,
    Register8Bit,
    Register16Bit,
    Register32Bit,
    Register64Bit,
    Register8BitWithPrefix,
    RegisterMM,
    RegisterXMM,
    RegisterST,
    RegisterSreg,
    RegisterCR,
    RegisterDR,
    MemoryLocation,
    MemoryLocation8,
    MemoryLocation16,
    MemoryLocation32,
    MemoryLocation64,
    MemoryLocation80,
    MemoryLocation128,
    Value,
}

internal enum PascalExtraOpcode
{
    None,
    Reg0,
    Reg1,
    Reg2,
    Reg3,
    Reg4,
    Reg5,
    Reg6,
    Reg7,
    Reg,
    Cb,
    Cw,
    Cd,
    Cp,
    Ib,
    Iw,
    Id,
    Prb,
    Prw,
    Prd,
    Pi,
}

internal enum PascalParamKind
{
    NoParam,
    One,
    Three,
    Al,
    Ax,
    Eax,
    Cl,
    Dx,
    Cs,
    Ds,
    Es,
    Ss,
    Fs,
    Gs,
    R8,
    R16,
    R32,
    R64,
    Mm,
    Xmm,
    St,
    St0,
    Sreg,
    Cr,
    Dr,
    M8,
    M16,
    M32,
    M64,
    M80,
    M128,
    Moffs8,
    Moffs16,
    Moffs32,
    Rm8,
    Rm16,
    Rm32,
    R32M16,
    MmM32,
    MmM64,
    XmmM32,
    XmmM64,
    XmmM128,
    Imm8,
    Imm16,
    Imm32,
    Rel8,
    Rel16,
    Rel32,
}

internal enum PascalRegisterCategory
{
    General,
    Mm,
    Xmm,
    St,
    Segment,
    Cr,
    Dr,
}

internal sealed record PascalRegisterInfo(
    string Name,
    int Code,
    PascalTokenType TokenType,
    PascalRegisterCategory Category,
    int SizeBits);

internal sealed record PascalOpcodeDefinition(
    string Mnemonic,
    PascalExtraOpcode Opcode1,
    PascalExtraOpcode Opcode2,
    PascalParamKind ParamType1,
    PascalParamKind ParamType2,
    PascalParamKind ParamType3,
    byte[] BaseBytes,
    bool Signed,
    bool NoRexW,
    bool InvalidIn64Bit,
    bool InvalidIn32Bit);

internal sealed record PascalOperand(
    string Raw,
    string Cleaned,
    PascalTokenType OriginalType,
    PascalTokenType EffectiveType,
    ulong UnsignedValue,
    long SignedValue,
    int ValueType,
    int SignedValueType);

internal sealed class PascalOpcodeCatalog
{
    public PascalOpcodeCatalog(List<PascalOpcodeDefinition> opcodes)
    {
        Opcodes = opcodes;

        var firstIndex = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var grouped = new Dictionary<string, List<PascalOpcodeDefinition>>(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; index < opcodes.Count; index++)
        {
            var mnemonic = opcodes[index].Mnemonic;
            if (!firstIndex.ContainsKey(mnemonic))
            {
                firstIndex[mnemonic] = index;
            }

            if (!grouped.TryGetValue(mnemonic, out var list))
            {
                list = new List<PascalOpcodeDefinition>();
                grouped[mnemonic] = list;
            }

            list.Add(opcodes[index]);
        }

        FirstIndex = firstIndex;
        Grouped = grouped;
    }

    public static PascalOpcodeCatalog Empty { get; } = new(new List<PascalOpcodeDefinition>());

    public List<PascalOpcodeDefinition> Opcodes { get; }

    public Dictionary<string, int> FirstIndex { get; }

    public Dictionary<string, List<PascalOpcodeDefinition>> Grouped { get; }

    public bool TryGetCandidates(string mnemonic, out List<PascalOpcodeDefinition>? candidates)
    {
        return Grouped.TryGetValue(mnemonic, out candidates);
    }
}

internal sealed class PascalAssemblerState
{
    public PascalAssemblerState(bool is64Bit, byte baseRexPrefix)
    {
        Is64Bit = is64Bit;
        RexPrefix = baseRexPrefix;
    }

    public bool Is64Bit { get; }

    public byte RexPrefix { get; set; }

    public int RexPrefixLocation { get; set; } = -1;

    public int RelativeAddressLocation { get; set; } = -1;

    public int RelativeAddressSize { get; set; }

    public ulong ActualDisplacement { get; set; }

    public bool RexW
    {
        get => (RexPrefix & 0x08) != 0;
        set => RexPrefix = value ? (byte)((RexPrefix & 0xF7) | 0x08) : (byte)(RexPrefix & 0xF7);
    }

    public bool RexR
    {
        get => (RexPrefix & 0x04) != 0;
        set => RexPrefix = value ? (byte)((RexPrefix & 0xFB) | 0x04) : (byte)(RexPrefix & 0xFB);
    }

    public bool RexX
    {
        get => (RexPrefix & 0x02) != 0;
        set => RexPrefix = value ? (byte)((RexPrefix & 0xFD) | 0x02) : (byte)(RexPrefix & 0xFD);
    }

    public bool RexB
    {
        get => (RexPrefix & 0x01) != 0;
        set => RexPrefix = value ? (byte)((RexPrefix & 0xFE) | 0x01) : (byte)(RexPrefix & 0xFE);
    }
}

internal static class PascalAssemblerEngine
{
    private static readonly string[] PrefixMnemonics = ["LOCK", "REP", "REPNE", "REPE"];
    private static readonly Lazy<PascalOpcodeCatalog> Catalog = new(LoadCatalog);
    private static readonly Dictionary<string, PascalRegisterInfo> Registers = BuildRegisters();

    public static int GetOpcodesIndex(string opcode)
    {
        if (string.IsNullOrWhiteSpace(opcode))
        {
            return -1;
        }

        var mnemonic = GetMnemonic(opcode);
        return Catalog.Value.FirstIndex.TryGetValue(mnemonic, out var index) ? index : -1;
    }

    public static bool TryAssemble(string opcode, nuint address, out byte[] bytes, bool skipRangeCheck)
    {
        if (Catalog.Value.Opcodes.Count == 0)
        {
            bytes = [];
            return false;
        }

        try
        {
            bytes = AssembleInternal(opcode, address, skipRangeCheck);
            return true;
        }
        catch
        {
            bytes = [];
            return false;
        }
    }

    private static byte[] AssembleInternal(string opcode, nuint address, bool skipRangeCheck)
    {
        ArgumentNullException.ThrowIfNull(opcode);

        var trimmed = opcode.Trim();
        if (trimmed.Length == 0)
        {
            return [];
        }

        var prefixBytes = new List<byte>();
        var prefixTokens = new List<string>();
        var remaining = trimmed;
        string mnemonic;
        while (true)
        {
            var word = ReadNextWord(ref remaining);
            if (word is null)
            {
                return [];
            }

            var upper = word.ToUpperInvariant();
            if (PrefixMnemonics.Contains(upper, StringComparer.OrdinalIgnoreCase))
            {
                prefixTokens.Add(upper);
                prefixBytes.Add(upper switch
                {
                    "LOCK" => (byte)0xF0,
                    "REPNE" => (byte)0xF2,
                    "REP" or "REPE" => (byte)0xF3,
                    _ => throw new InvalidOperationException(),
                });

                continue;
            }

            mnemonic = upper;
            break;
        }

        var operands = SplitOperands(remaining);
        if (TryAssembleDirective(mnemonic, operands, out var directiveBytes))
        {
            return directiveBytes;
        }

        var is64Bit = CeFuncProc.ProcessHandler.Is64Bit;
        var operandInfos = BuildOperands(operands, is64Bit, out var baseRexPrefix);
        AddSegmentPrefixes(prefixBytes, operandInfos);

        var overrideShort = operandInfos.Count > 0 && operandInfos[0].Raw.Contains("SHORT ", StringComparison.OrdinalIgnoreCase);
        var overrideLong = operandInfos.Count > 0 && operandInfos[0].Raw.Contains("LONG ", StringComparison.OrdinalIgnoreCase);
        var overrideFar = operandInfos.Count > 0 && operandInfos[0].Raw.Contains("FAR ", StringComparison.OrdinalIgnoreCase);
        if (!is64Bit)
        {
            overrideLong |= overrideFar;
            overrideFar = false;
        }

        if (is64Bit && operandInfos.Count == 1 && operandInfos[0].EffectiveType == PascalTokenType.Value && (mnemonic == "JMP" || mnemonic == "CALL"))
        {
            var farBranch = TryEncodeFarAbsoluteBranch(mnemonic, operandInfos[0], address, overrideShort, overrideLong, overrideFar);
            if (farBranch is not null)
            {
                return farBranch;
            }
        }

        if (!Catalog.Value.TryGetCandidates(mnemonic, out var candidates) || candidates is null)
        {
            throw new InvalidOperationException($"Unknown opcode {mnemonic}");
        }

        foreach (var candidate in candidates)
        {
            if (candidate.InvalidIn64Bit && is64Bit)
            {
                continue;
            }

            if (candidate.InvalidIn32Bit && !is64Bit)
            {
                continue;
            }

            if (TryEncodeCandidate(candidate, operandInfos, prefixBytes, address, skipRangeCheck, baseRexPrefix, overrideShort, overrideLong, out var bytes))
            {
                return bytes;
            }
        }

        throw new InvalidOperationException($"Unsupported instruction: {opcode}");
    }

    private static bool TryEncodeCandidate(
        PascalOpcodeDefinition candidate,
        IReadOnlyList<PascalOperand> operands,
        IReadOnlyCollection<byte> prefixBytes,
        nuint address,
        bool skipRangeCheck,
        byte baseRexPrefix,
        bool overrideShort,
        bool overrideLong,
        out byte[] bytes)
    {
        bytes = [];

        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        var paramCount = parameters.Count(parameter => parameter != PascalParamKind.NoParam);
        if (operands.Count != paramCount)
        {
            return false;
        }

        for (var index = 0; index < operands.Count; index++)
        {
            if (!MatchesParameter(parameters[index], operands[index]))
            {
                return false;
            }
        }

        var state = new PascalAssemblerState(CeFuncProc.ProcessHandler.Is64Bit, baseRexPrefix);
        var result = new List<byte>(prefixBytes);
        AddOpcode(result, state, candidate);

        if (HasMoffsParameter(candidate))
        {
            if (!TryEncodeMoffs(candidate, operands, result))
            {
                return false;
            }
        }

        if (RequiresOpcodePlusRegister(candidate))
        {
            if (!TryEncodeOpcodePlusRegister(candidate, operands, state, result))
            {
                return false;
            }
        }

        if (RequiresPiEncoding(candidate))
        {
            if (!TryEncodePi(candidate, operands, result))
            {
                return false;
            }
        }

        if (RequiresModRm(candidate))
        {
            if (!TryEncodeModRm(candidate, operands, state, result))
            {
                return false;
            }
        }

        if (!TryEncodeVariableData(candidate, operands, state, result, overrideShort, overrideLong))
        {
            return false;
        }

        if (!FinalizeEncoding(candidate, address, skipRangeCheck, state, result))
        {
            return false;
        }

        bytes = result.ToArray();
        return true;
    }

    private static bool TryEncodeVariableData(
        PascalOpcodeDefinition candidate,
        IReadOnlyList<PascalOperand> operands,
        PascalAssemblerState state,
        List<byte> bytes,
        bool overrideShort,
        bool overrideLong)
    {
        var immediates = GetImmediateOperandIndexes(candidate, operands.Count);
        var immediateCursor = 0;

        foreach (var extraOpcode in new[] { candidate.Opcode1, candidate.Opcode2 })
        {
            switch (extraOpcode)
            {
                case PascalExtraOpcode.Cb:
                case PascalExtraOpcode.Cw:
                case PascalExtraOpcode.Cd:
                    if (immediateCursor >= immediates.Count)
                    {
                        return false;
                    }

                    var relativeOperand = operands[immediates[immediateCursor++]];
                    var relativeSize = extraOpcode switch
                    {
                        PascalExtraOpcode.Cb => 1,
                        PascalExtraOpcode.Cw => 2,
                        PascalExtraOpcode.Cd => 4,
                        _ => throw new InvalidOperationException(),
                    };

                    if ((overrideShort && relativeSize != 1) || (overrideLong && relativeSize == 1))
                    {
                        return false;
                    }

                    state.RelativeAddressLocation = bytes.Count;
                    state.RelativeAddressSize = relativeSize;
                    state.ActualDisplacement = relativeOperand.UnsignedValue;
                    AddZeroBytes(bytes, relativeSize);
                    break;

                case PascalExtraOpcode.Ib:
                case PascalExtraOpcode.Iw:
                case PascalExtraOpcode.Id:
                    if (HasMoffsParameter(candidate))
                    {
                        break;
                    }

                    if (immediateCursor >= immediates.Count)
                    {
                        return false;
                    }

                    var immediateOperand = operands[immediates[immediateCursor++]];
                    if (!TryEncodeImmediate(candidate, immediateOperand, extraOpcode, state, bytes))
                    {
                        return false;
                    }

                    break;

                case PascalExtraOpcode.Cp:
                    return false;
            }
        }

        return true;
    }

    private static bool FinalizeEncoding(
        PascalOpcodeDefinition candidate,
        nuint address,
        bool skipRangeCheck,
        PascalAssemblerState state,
        List<byte> bytes)
    {
        if (!state.Is64Bit)
        {
            return PatchRelativeAddress(address, skipRangeCheck, state, bytes);
        }

        if (candidate.NoRexW)
        {
            state.RexW = false;
        }

        if (state.RexPrefix != 0)
        {
            if (state.RexPrefixLocation < 0 || state.RexPrefixLocation > bytes.Count)
            {
                return false;
            }

            state.RexPrefix |= 0x40;
            bytes.Insert(state.RexPrefixLocation, state.RexPrefix);
            if (state.RelativeAddressLocation != -1)
            {
                state.RelativeAddressLocation++;
            }
        }

        return PatchRelativeAddress(address, skipRangeCheck, state, bytes);
    }

    private static bool PatchRelativeAddress(nuint address, bool skipRangeCheck, PascalAssemblerState state, List<byte> bytes)
    {
        if (state.RelativeAddressLocation == -1)
        {
            return true;
        }

        var displacement = unchecked((long)state.ActualDisplacement - ((long)address + bytes.Count));
        return state.RelativeAddressSize switch
        {
            1 => PatchRelativeByte(displacement, skipRangeCheck, state, bytes),
            2 => PatchRelativeWord(displacement, skipRangeCheck, state, bytes),
            4 => PatchRelativeDword(displacement, skipRangeCheck, state, bytes),
            _ => false,
        };
    }

    private static bool PatchRelativeByte(long displacement, bool skipRangeCheck, PascalAssemblerState state, List<byte> bytes)
    {
        if (displacement < sbyte.MinValue || displacement > sbyte.MaxValue)
        {
            return skipRangeCheck;
        }

        bytes[state.RelativeAddressLocation] = unchecked((byte)(sbyte)displacement);
        return true;
    }

    private static bool PatchRelativeWord(long displacement, bool skipRangeCheck, PascalAssemblerState state, List<byte> bytes)
    {
        if (displacement < short.MinValue || displacement > short.MaxValue)
        {
            return skipRangeCheck;
        }

        var encoded = BitConverter.GetBytes((short)displacement);
        bytes[state.RelativeAddressLocation] = encoded[0];
        bytes[state.RelativeAddressLocation + 1] = encoded[1];
        return true;
    }

    private static bool PatchRelativeDword(long displacement, bool skipRangeCheck, PascalAssemblerState state, List<byte> bytes)
    {
        if (displacement < int.MinValue || displacement > int.MaxValue)
        {
            return skipRangeCheck;
        }

        var encoded = BitConverter.GetBytes((int)displacement);
        for (var index = 0; index < 4; index++)
        {
            bytes[state.RelativeAddressLocation + index] = encoded[index];
        }

        return true;
    }

    private static bool TryEncodeImmediate(
        PascalOpcodeDefinition candidate,
        PascalOperand operand,
        PascalExtraOpcode extraOpcode,
        PascalAssemblerState state,
        List<byte> bytes)
    {
        switch (extraOpcode)
        {
            case PascalExtraOpcode.Ib:
                if (candidate.Signed)
                {
                    if (operand.SignedValue < sbyte.MinValue || operand.SignedValue > sbyte.MaxValue)
                    {
                        return false;
                    }

                    bytes.Add(unchecked((byte)(sbyte)operand.SignedValue));
                    return true;
                }

                if (operand.ValueType > 8 && operand.UnsignedValue > byte.MaxValue)
                {
                    return false;
                }

                bytes.Add((byte)operand.UnsignedValue);
                return true;

            case PascalExtraOpcode.Iw:
                if (operand.UnsignedValue > ushort.MaxValue && operand.SignedValueType > 16)
                {
                    return false;
                }

                bytes.AddRange(BitConverter.GetBytes((ushort)operand.UnsignedValue));
                return true;

            case PascalExtraOpcode.Id:
                if (candidate.Opcode1 == PascalExtraOpcode.Prd && candidate.ParamType1 == PascalParamKind.R32 && state.RexW)
                {
                    bytes.AddRange(BitConverter.GetBytes(operand.UnsignedValue));
                    return true;
                }

                if (operand.UnsignedValue > uint.MaxValue && operand.SignedValueType > 32)
                {
                    return false;
                }

                bytes.AddRange(BitConverter.GetBytes((uint)operand.UnsignedValue));
                return true;

            default:
                return false;
        }
    }

    private static bool TryEncodeMoffs(PascalOpcodeDefinition candidate, IReadOnlyList<PascalOperand> operands, List<byte> bytes)
    {
        var moffsIndex = GetMoffsOperandIndex(candidate, operands.Count);
        if (moffsIndex == -1)
        {
            return false;
        }

        if (!TryParseMoffsAddress(operands[moffsIndex].Raw, out var absoluteAddress))
        {
            return false;
        }

        if (CeFuncProc.ProcessHandler.Is64Bit)
        {
            bytes.AddRange(BitConverter.GetBytes(absoluteAddress));
        }
        else
        {
            if (absoluteAddress > uint.MaxValue)
            {
                return false;
            }

            bytes.AddRange(BitConverter.GetBytes((uint)absoluteAddress));
        }

        return true;
    }

    private static bool TryEncodeOpcodePlusRegister(PascalOpcodeDefinition candidate, IReadOnlyList<PascalOperand> operands, PascalAssemblerState state, List<byte> bytes)
    {
        var registerIndex = GetOpcodePlusRegisterOperandIndex(candidate, operands.Count);
        if (registerIndex == -1)
        {
            return false;
        }

        if (!TryGetRegisterCode(operands[registerIndex].Cleaned, out var registerCode))
        {
            return false;
        }

        if (registerCode > 7)
        {
            state.RexB = true;
            registerCode &= 7;
        }

        bytes[^1] = unchecked((byte)(bytes[^1] + registerCode));
        return true;
    }

    private static bool TryEncodePi(PascalOpcodeDefinition candidate, IReadOnlyList<PascalOperand> operands, List<byte> bytes)
    {
        var stIndex = GetPiOperandIndex(candidate, operands.Count);
        if (stIndex == -1)
        {
            return false;
        }

        if (!TryGetRegisterCode(operands[stIndex].Cleaned, out var registerCode))
        {
            return false;
        }

        bytes[^1] = unchecked((byte)(bytes[^1] + (registerCode & 7)));
        return true;
    }

    private static bool TryEncodeModRm(PascalOpcodeDefinition candidate, IReadOnlyList<PascalOperand> operands, PascalAssemblerState state, List<byte> bytes)
    {
        if (!TryResolveModRm(candidate, operands, out var registerCode, out var rmOperandIndex))
        {
            return false;
        }

        return CreateModRm(bytes, state, registerCode, operands[rmOperandIndex].Cleaned);
    }

    private static bool TryResolveModRm(
        PascalOpcodeDefinition candidate,
        IReadOnlyList<PascalOperand> operands,
        out int registerCode,
        out int rmOperandIndex)
    {
        registerCode = -1;
        rmOperandIndex = -1;

        if (candidate.Opcode1 is PascalExtraOpcode.Reg0 or PascalExtraOpcode.Reg1 or PascalExtraOpcode.Reg2 or PascalExtraOpcode.Reg3 or PascalExtraOpcode.Reg4 or PascalExtraOpcode.Reg5 or PascalExtraOpcode.Reg6 or PascalExtraOpcode.Reg7)
        {
            registerCode = ExtraOpcodeToReg(candidate.Opcode1);
            rmOperandIndex = FindRmOperandIndex(candidate, operands.Count);
            return rmOperandIndex != -1;
        }

        if (candidate.Opcode1 != PascalExtraOpcode.Reg && candidate.Opcode2 != PascalExtraOpcode.Reg)
        {
            return false;
        }

        var paramKinds = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        var registerOnlyIndexes = new List<int>();
        var rmLikeIndexes = new List<int>();
        for (var index = 0; index < operands.Count; index++)
        {
            if (IsRegisterOnlyParam(paramKinds[index]))
            {
                registerOnlyIndexes.Add(index);
            }

            if (CanOccupyRmField(paramKinds[index]))
            {
                rmLikeIndexes.Add(index);
            }
        }

        if (registerOnlyIndexes.Count == 0 || rmLikeIndexes.Count == 0)
        {
            return false;
        }

        if (registerOnlyIndexes.Count == 1)
        {
            var registerIndex = registerOnlyIndexes[0];
            rmOperandIndex = rmLikeIndexes.First(index => index != registerIndex || rmLikeIndexes.Count == 1);
            if (!TryGetRegisterCode(operands[registerIndex].Cleaned, out registerCode))
            {
                return false;
            }

            return true;
        }

        if (paramKinds[0] == PascalParamKind.R32 && paramKinds[1] is PascalParamKind.Xmm or PascalParamKind.Mm or PascalParamKind.Cr or PascalParamKind.Dr)
        {
            rmOperandIndex = 0;
            return TryGetRegisterCode(operands[1].Cleaned, out registerCode);
        }

        rmOperandIndex = 1;
        return TryGetRegisterCode(operands[0].Cleaned, out registerCode);
    }

    private static bool CreateModRm(List<byte> bytes, PascalAssemblerState state, int registerCode, string operand)
    {
        var modRm = new List<byte> { 0 };
        var normalizedOperand = NormalizeOperand(operand);
        var openBracket = normalizedOperand.IndexOf('[');
        var closeBracket = normalizedOperand.LastIndexOf(']');
        var address = openBracket >= 0 && closeBracket > openBracket
            ? normalizedOperand[(openBracket + 1)..closeBracket]
            : string.Empty;

        if (address.Length == 0)
        {
            SetMod(modRm, 0, 3);
            if (!TrySetRegisterRm(modRm, state, normalizedOperand))
            {
                return false;
            }
        }
        else if (!SetMemoryAddress(modRm, state, address, bytes.Count))
        {
            return false;
        }

        if (registerCode > 7)
        {
            if (!state.Is64Bit)
            {
                return false;
            }

            state.RexR = true;
        }

        if (registerCode < 0)
        {
            registerCode = 0;
        }

        modRm[0] = unchecked((byte)(modRm[0] + ((registerCode & 7) << 3)));
        bytes.AddRange(modRm);
        return true;
    }

    private static bool TrySetRegisterRm(List<byte> modRm, PascalAssemblerState state, string operand)
    {
        if (!TryGetRegisterCode(operand, out var code))
        {
            return false;
        }

        SetRm(modRm, state, 0, code);
        return true;
    }

    private static bool SetMemoryAddress(List<byte> modRm, PascalAssemblerState state, string address, int offset)
    {
        var parts = SplitAddressTerms(address);
        ulong displacement = 0;
        var registerTerms = new List<string>();
        foreach (var part in parts)
        {
            var term = part.Term.Trim();
            if (term.Length == 0)
            {
                return false;
            }

            if (ContainsRegister(term))
            {
                registerTerms.Add((part.Positive ? string.Empty : "-") + term);
                continue;
            }

            var value = ParseUnsignedValue(term);
            if (part.Positive)
            {
                displacement += value;
            }
            else
            {
                displacement -= value;
            }
        }

        var regs = string.Join("+", registerTerms.Where(term => term.Length > 0));
        var plusCount = regs.Count(character => character == '+');
        var starCount = regs.Count(character => character == '*');
        if (plusCount > 1 || starCount > 1)
        {
            return false;
        }

        var signedDisplacement = unchecked((int)displacement);
        if (displacement == 0)
        {
            SetMod(modRm, 0, 0);
        }
        else if (signedDisplacement >= sbyte.MinValue && signedDisplacement <= sbyte.MaxValue)
        {
            SetMod(modRm, 0, 1);
        }
        else
        {
            SetMod(modRm, 0, 2);
        }

        string reg1;
        string reg2;
        if (regs.Contains('+'))
        {
            var index = regs.IndexOf('+');
            reg1 = regs[..index];
            reg2 = regs[(index + 1)..];
        }
        else
        {
            reg1 = regs;
            reg2 = string.Empty;
        }

        var reg = new Dictionary<int, string>
        {
            [-1] = reg1,
            [1] = reg2,
        };

        if (reg1.Length > 0 && reg2.Length == 0 && reg1.Contains('*'))
        {
            SetMod(modRm, 0, 0);
            SetRm(modRm, state, 0, 4);
            modRm.Add(0);
            SetSibBase(modRm, state, 1, 5);
            CreateSibScaleIndex(modRm, state, 1, reg[-1]);
            modRm.AddRange(BitConverter.GetBytes((uint)displacement));
            return true;
        }

        if (reg[1].Length == 0 && reg[-1].Length == 0)
        {
            SetRm(modRm, state, 0, 5);
            SetMod(modRm, 0, 0);

            if (state.Is64Bit)
            {
                if (displacement <= uint.MaxValue)
                {
                    modRm.Add(0);
                    SetRm(modRm, state, 0, 4);
                    SetSibBase(modRm, state, 1, 5);
                    SetSibIndex(modRm, state, 1, 4);
                    SetSibScale(modRm, 1, 0);
                }
                else
                {
                    state.ActualDisplacement = displacement;
                    state.RelativeAddressLocation = offset + 1;
                    state.RelativeAddressSize = 4;
                }
            }

            modRm.AddRange(BitConverter.GetBytes((uint)displacement));
            return true;
        }

        var k = 1;
        if (TryHandleBaseRegister(modRm, state, reg, ref k, "ESP", "RSP", 4)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "EAX", "RAX", 0)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "ECX", "RCX", 1)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "EDX", "RDX", 2)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "EBX", "RBX", 3)
            || TryHandleBaseRegisterWithDisp(modRm, state, reg, ref k, "EBP", "RBP", 5, displacement)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "ESI", "RSI", 6)
            || TryHandleBaseRegister(modRm, state, reg, ref k, "EDI", "RDI", 7)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R8", 8, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R9", 9, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R10", 10, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R11", 11, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R12", 12, displacement, forceSib: true)
            || TryHandleExtendedBaseRegisterWithDisp(modRm, state, reg, ref k, "R13", 13, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R14", 14, displacement)
            || TryHandleExtendedBaseRegister(modRm, state, reg, ref k, "R15", 15, displacement))
        {
            var mode = GetMod(modRm, 0);
            if (mode == 1)
            {
                modRm.Add(unchecked((byte)(sbyte)signedDisplacement));
            }
            else if (mode == 2)
            {
                modRm.AddRange(BitConverter.GetBytes((uint)displacement));
            }

            return true;
        }

        return false;
    }

    private static bool TryHandleBaseRegister(List<byte> modRm, PascalAssemblerState state, Dictionary<int, string> reg, ref int k, string x86Name, string x64Name, int code)
    {
        if (!MatchesRegister(reg[k], x86Name, x64Name) && !MatchesRegister(reg[-k], x86Name, x64Name))
        {
            return false;
        }

        if (MatchesRegister(reg[-k], x86Name, x64Name))
        {
            k = -k;
        }

        if (reg[-k].Length > 0)
        {
            SetRm(modRm, state, 0, 4);
            modRm.Add(0);
            SetSibBase(modRm, state, 1, code);
            CreateSibScaleIndex(modRm, state, 1, reg[-k]);
        }
        else
        {
            SetRm(modRm, state, 0, code);
        }

        return true;
    }

    private static bool TryHandleBaseRegisterWithDisp(List<byte> modRm, PascalAssemblerState state, Dictionary<int, string> reg, ref int k, string x86Name, string x64Name, int code, ulong displacement)
    {
        if (!MatchesRegister(reg[k], x86Name, x64Name) && !MatchesRegister(reg[-k], x86Name, x64Name))
        {
            return false;
        }

        if (MatchesRegister(reg[-k], x86Name, x64Name))
        {
            k = -k;
        }

        if (displacement == 0)
        {
            SetMod(modRm, 0, 1);
        }

        if (reg[-k].Length > 0)
        {
            SetRm(modRm, state, 0, 4);
            modRm.Add(0);
            SetSibBase(modRm, state, 1, code);
            CreateSibScaleIndex(modRm, state, 1, reg[-k]);
        }
        else
        {
            SetRm(modRm, state, 0, code);
        }

        return true;
    }

    private static bool TryHandleExtendedBaseRegister(List<byte> modRm, PascalAssemblerState state, Dictionary<int, string> reg, ref int k, string name, int code, ulong displacement, bool forceSib = false)
    {
        if (!state.Is64Bit)
        {
            return false;
        }

        if (!MatchesRegister(reg[k], name, name) && !MatchesRegister(reg[-k], name, name))
        {
            return false;
        }

        if (MatchesRegister(reg[-k], name, name))
        {
            k = -k;
        }

        if (forceSib || reg[-k].Length > 0)
        {
            SetRm(modRm, state, 0, 4);
            modRm.Add(0);
            SetSibBase(modRm, state, 1, code);
            CreateSibScaleIndex(modRm, state, 1, reg[-k]);
        }
        else
        {
            SetRm(modRm, state, 0, code);
        }

        return true;
    }

    private static bool TryHandleExtendedBaseRegisterWithDisp(List<byte> modRm, PascalAssemblerState state, Dictionary<int, string> reg, ref int k, string name, int code, ulong displacement)
    {
        if (!state.Is64Bit)
        {
            return false;
        }

        if (!MatchesRegister(reg[k], name, name) && !MatchesRegister(reg[-k], name, name))
        {
            return false;
        }

        if (MatchesRegister(reg[-k], name, name))
        {
            k = -k;
        }

        if (displacement == 0)
        {
            SetMod(modRm, 0, 1);
        }

        if (reg[-k].Length > 0)
        {
            SetRm(modRm, state, 0, 4);
            modRm.Add(0);
            SetSibBase(modRm, state, 1, code);
            CreateSibScaleIndex(modRm, state, 1, reg[-k]);
        }
        else
        {
            SetRm(modRm, state, 0, code);
        }

        return true;
    }

    private static void CreateSibScaleIndex(List<byte> modRm, PascalAssemblerState state, int sibIndex, string registerExpression)
    {
        if (registerExpression.Contains("*2", StringComparison.OrdinalIgnoreCase))
        {
            SetSibScale(modRm, sibIndex, 1);
        }
        else if (registerExpression.Contains("*4", StringComparison.OrdinalIgnoreCase))
        {
            SetSibScale(modRm, sibIndex, 2);
        }
        else if (registerExpression.Contains("*8", StringComparison.OrdinalIgnoreCase))
        {
            SetSibScale(modRm, sibIndex, 3);
        }
        else
        {
            SetSibScale(modRm, sibIndex, 0);
        }

        var normalized = registerExpression.ToUpperInvariant();
        foreach (var candidate in new[] { "RAX", "RCX", "RDX", "RBX", "RSP", "RBP", "RSI", "RDI", "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15", "EAX", "ECX", "EDX", "EBX", "ESP", "EBP", "ESI", "EDI" })
        {
            if (!normalized.Contains(candidate, StringComparison.Ordinal))
            {
                continue;
            }

            if (!TryGetRegisterCode(candidate, out var code))
            {
                break;
            }

            SetSibIndex(modRm, state, sibIndex, code);
            return;
        }

        SetSibIndex(modRm, state, sibIndex, 4);
    }

    private static bool ShouldEncodeFarAbsoluteBranch(string mnemonic, PascalOperand operand, nuint address, bool overrideShort, bool overrideLong, bool overrideFar)
    {
        if (overrideShort || overrideLong)
        {
            return false;
        }

        var target = operand.UnsignedValue;
        var distance = target > address ? target - address : address - target;
        if (distance <= int.MaxValue && !overrideFar)
        {
            return false;
        }

        return true;
    }

    private static byte[]? TryEncodeFarAbsoluteBranch(string mnemonic, PascalOperand operand, nuint address, bool overrideShort, bool overrideLong, bool overrideFar)
    {
        if (!ShouldEncodeFarAbsoluteBranch(mnemonic, operand, address, overrideShort, overrideLong, overrideFar))
        {
            return null;
        }

        var bytes = new List<byte> { 0xFF };
        if (mnemonic == "JMP")
        {
            bytes.Add(0x25);
            bytes.AddRange([0, 0, 0, 0]);
        }
        else
        {
            bytes.Add(0x15);
            bytes.AddRange(BitConverter.GetBytes(2));
            bytes.AddRange([0xEB, 0x08]);
        }

        bytes.AddRange(BitConverter.GetBytes(operand.UnsignedValue));
        return bytes.ToArray();
    }

    private static void AddOpcode(List<byte> bytes, PascalAssemblerState state, PascalOpcodeDefinition candidate)
    {
        state.RexPrefixLocation = bytes.Count;
        if (candidate.BaseBytes.Length > 0 && candidate.BaseBytes[0] is 0x66 or 0xF2 or 0xF3)
        {
            state.RexPrefixLocation++;
        }

        bytes.AddRange(candidate.BaseBytes);
    }

    private static bool MatchesParameter(PascalParamKind paramKind, PascalOperand operand)
    {
        return paramKind switch
        {
            PascalParamKind.NoParam => false,
            PascalParamKind.One => operand.EffectiveType == PascalTokenType.Value && operand.UnsignedValue == 1,
            PascalParamKind.Three => operand.EffectiveType == PascalTokenType.Value && operand.UnsignedValue == 3,
            PascalParamKind.Al => operand.Cleaned == "AL",
            PascalParamKind.Ax => operand.Cleaned == "AX",
            PascalParamKind.Eax => operand.Cleaned is "EAX" or "RAX",
            PascalParamKind.Cl => operand.Cleaned == "CL",
            PascalParamKind.Dx => operand.Cleaned == "DX",
            PascalParamKind.Cs => operand.Cleaned == "CS",
            PascalParamKind.Ds => operand.Cleaned == "DS",
            PascalParamKind.Es => operand.Cleaned == "ES",
            PascalParamKind.Ss => operand.Cleaned == "SS",
            PascalParamKind.Fs => operand.Cleaned == "FS",
            PascalParamKind.Gs => operand.Cleaned == "GS",
            PascalParamKind.R8 => operand.EffectiveType == PascalTokenType.Register8Bit,
            PascalParamKind.R16 => operand.EffectiveType == PascalTokenType.Register16Bit,
            PascalParamKind.R32 => operand.EffectiveType == PascalTokenType.Register32Bit,
            PascalParamKind.R64 => operand.OriginalType == PascalTokenType.Register64Bit,
            PascalParamKind.Mm => operand.OriginalType == PascalTokenType.RegisterMM,
            PascalParamKind.Xmm => operand.OriginalType == PascalTokenType.RegisterXMM,
            PascalParamKind.St => operand.OriginalType == PascalTokenType.RegisterST,
            PascalParamKind.St0 => operand.Cleaned is "ST" or "ST(0)",
            PascalParamKind.Sreg => operand.OriginalType == PascalTokenType.RegisterSreg,
            PascalParamKind.Cr => operand.OriginalType == PascalTokenType.RegisterCR,
            PascalParamKind.Dr => operand.OriginalType == PascalTokenType.RegisterDR,
            PascalParamKind.M8 => operand.OriginalType == PascalTokenType.MemoryLocation8,
            PascalParamKind.M16 => operand.OriginalType == PascalTokenType.MemoryLocation16,
            PascalParamKind.M32 => operand.OriginalType == PascalTokenType.MemoryLocation32 || operand.EffectiveType == PascalTokenType.MemoryLocation32,
            PascalParamKind.M64 => operand.OriginalType == PascalTokenType.MemoryLocation64,
            PascalParamKind.M80 => operand.OriginalType == PascalTokenType.MemoryLocation80,
            PascalParamKind.M128 => operand.OriginalType == PascalTokenType.MemoryLocation128,
            PascalParamKind.Moffs8 => IsAbsoluteMemoryOperand(operand.Raw) && operand.OriginalType is PascalTokenType.MemoryLocation8 or PascalTokenType.MemoryLocation,
            PascalParamKind.Moffs16 => IsAbsoluteMemoryOperand(operand.Raw) && operand.OriginalType is PascalTokenType.MemoryLocation16 or PascalTokenType.MemoryLocation,
            PascalParamKind.Moffs32 => IsAbsoluteMemoryOperand(operand.Raw) && operand.OriginalType is PascalTokenType.MemoryLocation32 or PascalTokenType.MemoryLocation64 or PascalTokenType.MemoryLocation,
            PascalParamKind.Rm8 => IsRm8(operand),
            PascalParamKind.Rm16 => IsRm16(operand),
            PascalParamKind.Rm32 => IsRm32(operand),
            PascalParamKind.R32M16 => operand.EffectiveType == PascalTokenType.Register32Bit || operand.OriginalType == PascalTokenType.MemoryLocation16,
            PascalParamKind.MmM32 => operand.OriginalType == PascalTokenType.RegisterMM || operand.OriginalType == PascalTokenType.MemoryLocation32,
            PascalParamKind.MmM64 => operand.OriginalType == PascalTokenType.RegisterMM || operand.OriginalType == PascalTokenType.MemoryLocation64 || (operand.EffectiveType == PascalTokenType.MemoryLocation32 && IsMemoryLocationDefault(operand.Cleaned)),
            PascalParamKind.XmmM32 => operand.OriginalType == PascalTokenType.RegisterXMM || operand.OriginalType == PascalTokenType.MemoryLocation32,
            PascalParamKind.XmmM64 => operand.OriginalType == PascalTokenType.RegisterXMM || operand.OriginalType == PascalTokenType.MemoryLocation64 || (operand.EffectiveType == PascalTokenType.MemoryLocation32 && IsMemoryLocationDefault(operand.Cleaned)),
            PascalParamKind.XmmM128 => operand.OriginalType == PascalTokenType.RegisterXMM || operand.OriginalType == PascalTokenType.MemoryLocation128 || (operand.EffectiveType == PascalTokenType.MemoryLocation32 && IsMemoryLocationDefault(operand.Cleaned)),
            PascalParamKind.Imm8 => operand.EffectiveType == PascalTokenType.Value,
            PascalParamKind.Imm16 => operand.EffectiveType == PascalTokenType.Value,
            PascalParamKind.Imm32 => operand.EffectiveType == PascalTokenType.Value,
            PascalParamKind.Rel8 => operand.EffectiveType == PascalTokenType.Value,
            PascalParamKind.Rel16 => operand.EffectiveType == PascalTokenType.Value,
            PascalParamKind.Rel32 => operand.EffectiveType == PascalTokenType.Value,
            _ => false,
        };
    }

    private static bool IsRm8(PascalOperand operand)
    {
        return operand.EffectiveType is PascalTokenType.Register8Bit or PascalTokenType.MemoryLocation8;
    }

    private static bool IsRm16(PascalOperand operand)
    {
        return operand.EffectiveType is PascalTokenType.Register16Bit or PascalTokenType.MemoryLocation16;
    }

    private static bool IsRm32(PascalOperand operand)
    {
        return operand.EffectiveType is PascalTokenType.Register32Bit or PascalTokenType.MemoryLocation32;
    }

    private static bool IsAbsoluteMemoryOperand(string operand)
    {
        var normalized = NormalizeOperand(operand);
        var openBracket = normalized.IndexOf('[');
        var closeBracket = normalized.LastIndexOf(']');
        if (openBracket < 0 || closeBracket <= openBracket)
        {
            return false;
        }

        var inner = normalized[(openBracket + 1)..closeBracket];
        return !ContainsRegister(inner);
    }

    private static bool TryParseMoffsAddress(string operand, out ulong address)
    {
        address = 0;
        if (!IsAbsoluteMemoryOperand(operand))
        {
            return false;
        }

        var normalized = NormalizeOperand(operand);
        var inner = normalized[(normalized.IndexOf('[') + 1)..normalized.LastIndexOf(']')];
        address = ParseUnsignedValue(inner);
        return true;
    }

    private static bool HasMoffsParameter(PascalOpcodeDefinition candidate)
    {
        return candidate.ParamType1 is PascalParamKind.Moffs8 or PascalParamKind.Moffs16 or PascalParamKind.Moffs32
            || candidate.ParamType2 is PascalParamKind.Moffs8 or PascalParamKind.Moffs16 or PascalParamKind.Moffs32
            || candidate.ParamType3 is PascalParamKind.Moffs8 or PascalParamKind.Moffs16 or PascalParamKind.Moffs32;
    }

    private static int GetMoffsOperandIndex(PascalOpcodeDefinition candidate, int operandCount)
    {
        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        for (var index = 0; index < operandCount; index++)
        {
            if (parameters[index] is PascalParamKind.Moffs8 or PascalParamKind.Moffs16 or PascalParamKind.Moffs32)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool RequiresOpcodePlusRegister(PascalOpcodeDefinition candidate)
    {
        return candidate.Opcode1 is PascalExtraOpcode.Prb or PascalExtraOpcode.Prw or PascalExtraOpcode.Prd
            || candidate.Opcode2 is PascalExtraOpcode.Prb or PascalExtraOpcode.Prw or PascalExtraOpcode.Prd;
    }

    private static int GetOpcodePlusRegisterOperandIndex(PascalOpcodeDefinition candidate, int operandCount)
    {
        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        for (var index = 0; index < operandCount; index++)
        {
            if (parameters[index] is PascalParamKind.R8 or PascalParamKind.R16 or PascalParamKind.R32 or PascalParamKind.R64)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool RequiresPiEncoding(PascalOpcodeDefinition candidate)
    {
        return candidate.Opcode1 == PascalExtraOpcode.Pi || candidate.Opcode2 == PascalExtraOpcode.Pi;
    }

    private static int GetPiOperandIndex(PascalOpcodeDefinition candidate, int operandCount)
    {
        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        for (var index = 0; index < operandCount; index++)
        {
            if (parameters[index] == PascalParamKind.St)
            {
                return index;
            }
        }

        return -1;
    }

    private static bool RequiresModRm(PascalOpcodeDefinition candidate)
    {
        return candidate.Opcode1 is PascalExtraOpcode.Reg or PascalExtraOpcode.Reg0 or PascalExtraOpcode.Reg1 or PascalExtraOpcode.Reg2 or PascalExtraOpcode.Reg3 or PascalExtraOpcode.Reg4 or PascalExtraOpcode.Reg5 or PascalExtraOpcode.Reg6 or PascalExtraOpcode.Reg7
            || candidate.Opcode2 is PascalExtraOpcode.Reg;
    }

    private static int FindRmOperandIndex(PascalOpcodeDefinition candidate, int operandCount)
    {
        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        for (var index = 0; index < operandCount; index++)
        {
            if (CanOccupyRmField(parameters[index]))
            {
                return index;
            }
        }

        return -1;
    }

    private static bool IsRegisterOnlyParam(PascalParamKind paramKind)
    {
        return paramKind is PascalParamKind.R8 or PascalParamKind.R16 or PascalParamKind.R32 or PascalParamKind.R64 or PascalParamKind.Mm or PascalParamKind.Xmm or PascalParamKind.St or PascalParamKind.Sreg or PascalParamKind.Cr or PascalParamKind.Dr;
    }

    private static bool CanOccupyRmField(PascalParamKind paramKind)
    {
        return paramKind is PascalParamKind.R8 or PascalParamKind.R16 or PascalParamKind.R32 or PascalParamKind.R64
            or PascalParamKind.Mm or PascalParamKind.Xmm or PascalParamKind.Cr or PascalParamKind.Dr
            or PascalParamKind.M8 or PascalParamKind.M16 or PascalParamKind.M32 or PascalParamKind.M64 or PascalParamKind.M80 or PascalParamKind.M128
            or PascalParamKind.Rm8 or PascalParamKind.Rm16 or PascalParamKind.Rm32
            or PascalParamKind.R32M16 or PascalParamKind.MmM32 or PascalParamKind.MmM64 or PascalParamKind.XmmM32 or PascalParamKind.XmmM64 or PascalParamKind.XmmM128;
    }

    private static List<int> GetImmediateOperandIndexes(PascalOpcodeDefinition candidate, int operandCount)
    {
        var parameters = new[] { candidate.ParamType1, candidate.ParamType2, candidate.ParamType3 };
        var indexes = new List<int>();
        for (var index = 0; index < operandCount; index++)
        {
            if (parameters[index] is PascalParamKind.Imm8 or PascalParamKind.Imm16 or PascalParamKind.Imm32 or PascalParamKind.Rel8 or PascalParamKind.Rel16 or PascalParamKind.Rel32)
            {
                indexes.Add(index);
            }
        }

        return indexes;
    }

    private static int ExtraOpcodeToReg(PascalExtraOpcode extraOpcode)
    {
        return extraOpcode switch
        {
            PascalExtraOpcode.Reg0 => 0,
            PascalExtraOpcode.Reg1 => 1,
            PascalExtraOpcode.Reg2 => 2,
            PascalExtraOpcode.Reg3 => 3,
            PascalExtraOpcode.Reg4 => 4,
            PascalExtraOpcode.Reg5 => 5,
            PascalExtraOpcode.Reg6 => 6,
            PascalExtraOpcode.Reg7 => 7,
            _ => -1,
        };
    }

    private static List<PascalOperand> BuildOperands(IReadOnlyList<string> operands, bool is64Bit, out byte baseRexPrefix)
    {
        var result = new List<PascalOperand>(operands.Count);
        baseRexPrefix = 0;
        for (var index = 0; index < operands.Count; index++)
        {
            var raw = operands[index].Trim();
            var cleaned = raw.ToUpperInvariant();
            var peer = index switch
            {
                0 when operands.Count > 1 => operands[1],
                1 when operands.Count > 0 => operands[0],
                _ => string.Empty,
            };

            var originalType = GetTokenType(ref cleaned, peer);
            var effectiveType = originalType;
            if (is64Bit)
            {
                if (originalType == PascalTokenType.Register8BitWithPrefix)
                {
                    baseRexPrefix |= 0x40;
                    effectiveType = PascalTokenType.Register8Bit;
                }

                if (originalType == PascalTokenType.Register64Bit)
                {
                    baseRexPrefix |= 0x08;
                    effectiveType = PascalTokenType.Register32Bit;
                }

                if (originalType == PascalTokenType.MemoryLocation64)
                {
                    baseRexPrefix |= 0x08;
                    effectiveType = PascalTokenType.MemoryLocation32;
                }
            }

            ulong unsignedValue = 0;
            long signedValue = 0;
            var valueType = 0;
            var signedValueType = 0;
            if (effectiveType == PascalTokenType.Value)
            {
                unsignedValue = ParseUnsignedValue(cleaned);
                signedValue = unchecked((long)unsignedValue);
                valueType = StringValueToType(cleaned);
                signedValueType = SignedValueToType(signedValue);
            }

            result.Add(new PascalOperand(raw, cleaned, originalType, effectiveType, unsignedValue, signedValue, valueType, signedValueType));
        }

        return result;
    }

    private static PascalTokenType GetTokenType(ref string token, string otherOperand)
    {
        var normalized = NormalizeOperand(token);
        normalized = normalized.Replace("LONG ", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("SHORT ", string.Empty, StringComparison.OrdinalIgnoreCase);
        normalized = normalized.Replace("FAR ", string.Empty, StringComparison.OrdinalIgnoreCase);
        token = normalized;

        var registerType = TokenToRegisterType(normalized);
        if (registerType != PascalTokenType.InvalidToken)
        {
            return registerType;
        }

        if (TryParseValue(normalized, out _))
        {
            return PascalTokenType.Value;
        }

        if (normalized.Contains('[', StringComparison.Ordinal))
        {
            if (normalized.Contains("DQWORD ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation128;
            }

            if (normalized.Contains("TBYTE ", StringComparison.OrdinalIgnoreCase) || normalized.Contains("TWORD ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation80;
            }

            if (normalized.Contains("QWORD ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation64;
            }

            if (normalized.Contains("DWORD ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation32;
            }

            if (normalized.Contains("WORD ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation16;
            }

            if (normalized.Contains("BYTE ", StringComparison.OrdinalIgnoreCase))
            {
                return PascalTokenType.MemoryLocation8;
            }

            if (string.IsNullOrWhiteSpace(otherOperand))
            {
                return PascalTokenType.MemoryLocation32;
            }

            return TokenToRegisterType(NormalizeOperand(otherOperand).ToUpperInvariant()) switch
            {
                PascalTokenType.Register8Bit or PascalTokenType.Register8BitWithPrefix => PascalTokenType.MemoryLocation8,
                PascalTokenType.Register16Bit or PascalTokenType.RegisterSreg => PascalTokenType.MemoryLocation16,
                PascalTokenType.Register64Bit => PascalTokenType.MemoryLocation64,
                _ => PascalTokenType.MemoryLocation32,
            };
        }

        return PascalTokenType.InvalidToken;
    }

    private static PascalTokenType TokenToRegisterType(string token)
    {
        return Registers.TryGetValue(NormalizeOperand(token), out var register)
            ? register.TokenType
            : PascalTokenType.InvalidToken;
    }

    private static void AddSegmentPrefixes(List<byte> prefixBytes, IReadOnlyList<PascalOperand> operands)
    {
        foreach (var operand in operands)
        {
            if (operand.OriginalType is < PascalTokenType.MemoryLocation or > PascalTokenType.MemoryLocation128)
            {
                continue;
            }

            var raw = operand.Raw.ToUpperInvariant();
            if (raw.Contains("ES:", StringComparison.Ordinal))
            {
                prefixBytes.Add(0x26);
            }

            if (raw.Contains("CS:", StringComparison.Ordinal))
            {
                prefixBytes.Add(0x2E);
            }

            if (raw.Contains("SS:", StringComparison.Ordinal))
            {
                prefixBytes.Add(0x36);
            }

            if (raw.Contains("FS:", StringComparison.Ordinal))
            {
                prefixBytes.Add(0x64);
            }

            if (raw.Contains("GS:", StringComparison.Ordinal))
            {
                prefixBytes.Add(0x65);
            }
        }
    }

    private static bool TryAssembleDirective(string mnemonic, IReadOnlyList<string> operands, out byte[] bytes)
    {
        bytes = [];
        switch (mnemonic)
        {
            case "DB":
                bytes = AssembleDataDirective(operands, 1);
                return true;
            case "DW":
                bytes = AssembleDataDirective(operands, 2);
                return true;
            case "DD":
                bytes = AssembleDataDirective(operands, 4);
                return true;
            case "DQ":
                bytes = AssembleDataDirective(operands, 8);
                return true;
            case "RESB":
            case "RESW":
            case "RESD":
            case "RESQ":
                if (operands.Count != 1)
                {
                    return false;
                }

                var count = checked((int)ParseUnsignedValue(operands[0]));
                var multiplier = mnemonic[3] switch
                {
                    'B' => 1,
                    'W' => 2,
                    'D' => 4,
                    'Q' => 8,
                    _ => throw new InvalidOperationException(),
                };
                bytes = new byte[count * multiplier];
                return true;
            default:
                return false;
        }
    }

    private static byte[] AssembleDataDirective(IReadOnlyList<string> operands, int itemSize)
    {
        var bytes = new List<byte>();
        foreach (var operand in ExpandDirectiveOperands(operands))
        {
            var trimmed = operand.Trim();
            if (itemSize == 1 && trimmed.Length >= 2 && ((trimmed[0] == '\'' && trimmed[^1] == '\'') || (trimmed[0] == '"' && trimmed[^1] == '"')))
            {
                bytes.AddRange(Encoding.Default.GetBytes(trimmed[1..^1]));
                continue;
            }

            var value = ParseUnsignedValue(trimmed);
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

            if ((trimmed.StartsWith("\"") && trimmed.EndsWith("\"")) || (trimmed.StartsWith("'") && trimmed.EndsWith("'")))
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

    private static string GetMnemonic(string opcode)
    {
        var remaining = opcode.Trim();
        while (true)
        {
            var word = ReadNextWord(ref remaining);
            if (word is null)
            {
                return string.Empty;
            }

            var upper = word.ToUpperInvariant();
            if (PrefixMnemonics.Contains(upper, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }

            return upper;
        }
    }

    private static string? ReadNextWord(ref string remaining)
    {
        remaining = remaining.TrimStart();
        if (remaining.Length == 0)
        {
            return null;
        }

        var end = remaining.IndexOfAny([' ', '\t']);
        if (end < 0)
        {
            var result = remaining;
            remaining = string.Empty;
            return result;
        }

        var word = remaining[..end];
        remaining = remaining[end..];
        return word;
    }

    private static List<string> SplitOperands(string text)
    {
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(text))
        {
            return result;
        }

        var builder = new StringBuilder();
        var bracketDepth = 0;
        var inSingleQuote = false;
        var inDoubleQuote = false;
        foreach (var character in text)
        {
            switch (character)
            {
                case '\'' when !inDoubleQuote:
                    inSingleQuote = !inSingleQuote;
                    break;
                case '"' when !inSingleQuote:
                    inDoubleQuote = !inDoubleQuote;
                    break;
                case '[' when !inSingleQuote && !inDoubleQuote:
                    bracketDepth++;
                    break;
                case ']' when !inSingleQuote && !inDoubleQuote:
                    bracketDepth--;
                    break;
                case ',' when !inSingleQuote && !inDoubleQuote && bracketDepth == 0:
                    result.Add(builder.ToString().Trim());
                    builder.Clear();
                    continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString().Trim());
        }

        return result.Where(item => item.Length > 0).ToList();
    }

    private static ulong ParseUnsignedValue(string text)
    {
        var trimmed = NormalizeOperand(text);
        if (TryParseValue(trimmed, out var value))
        {
            return value;
        }

        return AddressParserGlobal.GetAddress(trimmed);
    }

    private static bool TryParseValue(string text, out ulong value)
    {
        try
        {
            value = CeFuncProc.StrToQWordEx(text);
            return true;
        }
        catch
        {
            try
            {
                value = AddressParserGlobal.GetAddress(text);
                return true;
            }
            catch
            {
                value = 0;
                return false;
            }
        }
    }

    private static int SignedValueToType(long value)
    {
        if (value < sbyte.MinValue || value > sbyte.MaxValue)
        {
            if (value < short.MinValue || value > short.MaxValue)
            {
                if (value < int.MinValue || value > int.MaxValue)
                {
                    return 64;
                }

                return 32;
            }

            return 16;
        }

        return 8;
    }

    private static int ValueToType(ulong value)
    {
        if (value > uint.MaxValue)
        {
            return 64;
        }

        var dword = (uint)value;
        var result = 32;
        if (dword <= ushort.MaxValue)
        {
            result = 16;
            if (dword >= 0x8000)
            {
                result = 32;
            }
        }

        if (dword <= byte.MaxValue)
        {
            result = 8;
            if (dword >= 0x80)
            {
                result = 16;
            }
        }

        if (result == 32)
        {
            var signed = unchecked((int)dword);
            if (signed < 0)
            {
                if (signed >= -128)
                {
                    result = 8;
                }
                else if (signed >= -32768)
                {
                    result = 16;
                }
            }
        }

        return result;
    }

    private static int StringValueToType(string value)
    {
        var trimmed = NormalizeOperand(value);
        if (trimmed.StartsWith("+", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.StartsWith("-", StringComparison.Ordinal))
        {
            trimmed = trimmed[1..];
        }

        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            var hexDigits = trimmed[1..];
            return hexDigits.Length switch
            {
                16 => 64,
                8 => 32,
                4 => 16,
                2 => 8,
                _ => ValueToType(ulong.Parse(hexDigits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
            };
        }

        if (trimmed.StartsWith("0X", StringComparison.OrdinalIgnoreCase))
        {
            var hexDigits = trimmed[2..];
            return hexDigits.Length switch
            {
                16 => 64,
                8 => 32,
                4 => 16,
                2 => 8,
                _ => ValueToType(ulong.Parse(hexDigits, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture)),
            };
        }

        return ValueToType(CeFuncProc.StrToQWordEx(value));
    }

    private static bool ContainsRegister(string expression)
    {
        var upper = NormalizeOperand(expression);
        foreach (var register in Registers.Keys)
        {
            if (upper.Contains(register, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRegisterCode(string registerName, out int registerCode)
    {
        if (Registers.TryGetValue(NormalizeOperand(registerName), out var register))
        {
            registerCode = register.Code;
            return true;
        }

        registerCode = -1;
        return false;
    }

    private static string NormalizeOperand(string operand)
    {
        return operand.Trim().ToUpperInvariant();
    }

    private static bool IsMemoryLocationDefault(string operand)
    {
        var normalized = NormalizeOperand(operand);
        return normalized.StartsWith("[", StringComparison.Ordinal) && normalized.EndsWith("]", StringComparison.Ordinal);
    }

    private static List<(bool Positive, string Term)> SplitAddressTerms(string address)
    {
        var result = new List<(bool Positive, string Term)>();
        var builder = new StringBuilder();
        var positive = true;
        for (var index = 0; index < address.Length; index++)
        {
            var character = address[index];
            if ((character == '+' || character == '-') && builder.Length > 0)
            {
                result.Add((positive, builder.ToString()));
                builder.Clear();
                positive = character == '+';
                continue;
            }

            if ((character == '+' || character == '-') && builder.Length == 0)
            {
                positive = character != '-';
                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            result.Add((positive, builder.ToString()));
        }

        return result;
    }

    private static bool MatchesRegister(string candidate, string x86Name, string x64Name)
    {
        var normalized = NormalizeOperand(candidate);
        return normalized == x86Name || normalized == x64Name;
    }

    private static void SetMod(List<byte> bytes, int index, int value)
    {
        bytes[index] = unchecked((byte)((bytes[index] & 0x3F) | (value << 6)));
    }

    private static int GetMod(List<byte> bytes, int index)
    {
        return bytes[index] >> 6;
    }

    private static void SetRm(List<byte> bytes, PascalAssemblerState state, int index, int value)
    {
        bytes[index] = unchecked((byte)((bytes[index] & 0xF8) | (value & 7)));
        if (value > 7)
        {
            state.RexB = true;
        }
    }

    private static void SetSibBase(List<byte> bytes, PascalAssemblerState state, int index, int value)
    {
        bytes[index] = unchecked((byte)((bytes[index] & 0xF8) | (value & 7)));
        if (value > 7)
        {
            state.RexB = true;
        }
    }

    private static void SetSibIndex(List<byte> bytes, PascalAssemblerState state, int index, int value)
    {
        bytes[index] = unchecked((byte)((bytes[index] & 0xC7) | ((value & 7) << 3)));
        if (value > 7)
        {
            state.RexX = true;
        }
    }

    private static void SetSibScale(List<byte> bytes, int index, int value)
    {
        bytes[index] = unchecked((byte)((bytes[index] & 0x3F) | (value << 6)));
    }

    private static void AddZeroBytes(List<byte> bytes, int count)
    {
        for (var index = 0; index < count; index++)
        {
            bytes.Add(0);
        }
    }

    private static PascalOpcodeCatalog LoadCatalog()
    {
        try
        {
            var path = FindPascalAssemblerSource();
            if (path is null)
            {
                return PascalOpcodeCatalog.Empty;
            }

            var definitions = ParsePascalOpcodeTable(path);
            return definitions.Count == 0 ? PascalOpcodeCatalog.Empty : new PascalOpcodeCatalog(definitions);
        }
        catch
        {
            return PascalOpcodeCatalog.Empty;
        }
    }

    private static string? FindPascalAssemblerSource()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "library", "Assemblerunit.pas");
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        return null;
    }

    private static List<PascalOpcodeDefinition> ParsePascalOpcodeTable(string path)
    {
        var definitions = new List<PascalOpcodeDefinition>();
        var inTable = false;
        foreach (var rawLine in File.ReadLines(path))
        {
            if (!inTable)
            {
                if (rawLine.Contains("const opcodes:", StringComparison.OrdinalIgnoreCase))
                {
                    inTable = true;
                }

                continue;
            }

            var line = StripPascalComment(rawLine).Trim();
            if (line.StartsWith(");", StringComparison.Ordinal))
            {
                break;
            }

            var start = line.IndexOf("(mnemonic:'", StringComparison.OrdinalIgnoreCase);
            if (start < 0)
            {
                continue;
            }

            var entry = line[start..].Trim();
            if (entry.EndsWith(",", StringComparison.Ordinal))
            {
                entry = entry[..^1];
            }

            if (!entry.StartsWith("(", StringComparison.Ordinal) || !entry.EndsWith(")", StringComparison.Ordinal))
            {
                continue;
            }

            entry = entry[1..^1];
            var fields = SplitPascalFields(entry);

            var mnemonic = string.Empty;
            var opcode1 = PascalExtraOpcode.None;
            var opcode2 = PascalExtraOpcode.None;
            var param1 = PascalParamKind.NoParam;
            var param2 = PascalParamKind.NoParam;
            var param3 = PascalParamKind.NoParam;
            var bytesCount = 0;
            byte bt1 = 0;
            byte bt2 = 0;
            byte bt3 = 0;
            var signed = false;
            var noRexW = false;
            var invalidIn64Bit = false;
            var invalidIn32Bit = false;

            foreach (var field in fields)
            {
                var separator = field.IndexOf(':');
                if (separator < 0)
                {
                    continue;
                }

                var key = field[..separator].Trim().ToLowerInvariant();
                var value = field[(separator + 1)..].Trim();
                switch (key)
                {
                    case "mnemonic":
                        mnemonic = value.Trim('\'');
                        break;
                    case "opcode1":
                        opcode1 = ParseExtraOpcode(value);
                        break;
                    case "opcode2":
                        opcode2 = ParseExtraOpcode(value);
                        break;
                    case "paramtype1":
                        param1 = ParseParamKind(value);
                        break;
                    case "paramtype2":
                        param2 = ParseParamKind(value);
                        break;
                    case "paramtype3":
                        param3 = ParseParamKind(value);
                        break;
                    case "bytes":
                        bytesCount = int.Parse(value, CultureInfo.InvariantCulture);
                        break;
                    case "bt1":
                        bt1 = ParseHexByte(value);
                        break;
                    case "bt2":
                        bt2 = ParseHexByte(value);
                        break;
                    case "bt3":
                        bt3 = ParseHexByte(value);
                        break;
                    case "signed":
                        signed = bool.Parse(value);
                        break;
                    case "norexw":
                        noRexW = bool.Parse(value);
                        break;
                    case "invalidin64bit":
                        invalidIn64Bit = bool.Parse(value);
                        break;
                    case "invalidin32bit":
                        invalidIn32Bit = bool.Parse(value);
                        break;
                }
            }

            if (mnemonic.Length == 0 || bytesCount == 0)
            {
                continue;
            }

            byte[] baseBytes = bytesCount switch
            {
                1 => [bt1],
                2 => [bt1, bt2],
                _ => [bt1, bt2, bt3],
            };

            definitions.Add(new PascalOpcodeDefinition(
                mnemonic.ToUpperInvariant(),
                opcode1,
                opcode2,
                param1,
                param2,
                param3,
                baseBytes,
                signed,
                noRexW,
                invalidIn64Bit,
                invalidIn32Bit));
        }

        return definitions;
    }

    private static List<string> SplitPascalFields(string entry)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var inString = false;
        foreach (var character in entry)
        {
            if (character == '\'')
            {
                inString = !inString;
            }

            if (!inString && character == ';')
            {
                result.Add(builder.ToString());
                builder.Clear();
                continue;
            }

            builder.Append(character);
        }

        if (builder.Length > 0)
        {
            result.Add(builder.ToString());
        }

        return result;
    }

    private static string StripPascalComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '\'')
            {
                inString = !inString;
            }

            if (!inString && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static PascalExtraOpcode ParseExtraOpcode(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "eo_none" => PascalExtraOpcode.None,
            "eo_reg0" => PascalExtraOpcode.Reg0,
            "eo_reg1" => PascalExtraOpcode.Reg1,
            "eo_reg2" => PascalExtraOpcode.Reg2,
            "eo_reg3" => PascalExtraOpcode.Reg3,
            "eo_reg4" => PascalExtraOpcode.Reg4,
            "eo_reg5" => PascalExtraOpcode.Reg5,
            "eo_reg6" => PascalExtraOpcode.Reg6,
            "eo_reg7" => PascalExtraOpcode.Reg7,
            "eo_reg" => PascalExtraOpcode.Reg,
            "eo_cb" => PascalExtraOpcode.Cb,
            "eo_cw" => PascalExtraOpcode.Cw,
            "eo_cd" => PascalExtraOpcode.Cd,
            "eo_cp" => PascalExtraOpcode.Cp,
            "eo_ib" => PascalExtraOpcode.Ib,
            "eo_iw" => PascalExtraOpcode.Iw,
            "eo_id" => PascalExtraOpcode.Id,
            "eo_prb" => PascalExtraOpcode.Prb,
            "eo_prw" => PascalExtraOpcode.Prw,
            "eo_prd" => PascalExtraOpcode.Prd,
            "eo_pi" => PascalExtraOpcode.Pi,
            _ => PascalExtraOpcode.None,
        };
    }

    private static PascalParamKind ParseParamKind(string value)
    {
        return value.Trim().ToLowerInvariant() switch
        {
            "par_noparam" => PascalParamKind.NoParam,
            "par_1" => PascalParamKind.One,
            "par_3" => PascalParamKind.Three,
            "par_al" => PascalParamKind.Al,
            "par_ax" => PascalParamKind.Ax,
            "par_eax" => PascalParamKind.Eax,
            "par_cl" => PascalParamKind.Cl,
            "par_dx" => PascalParamKind.Dx,
            "par_cs" => PascalParamKind.Cs,
            "par_ds" => PascalParamKind.Ds,
            "par_es" => PascalParamKind.Es,
            "par_ss" => PascalParamKind.Ss,
            "par_fs" => PascalParamKind.Fs,
            "par_gs" => PascalParamKind.Gs,
            "par_r8" => PascalParamKind.R8,
            "par_r16" => PascalParamKind.R16,
            "par_r32" => PascalParamKind.R32,
            "par_r64" => PascalParamKind.R64,
            "par_mm" => PascalParamKind.Mm,
            "par_xmm" => PascalParamKind.Xmm,
            "par_st" => PascalParamKind.St,
            "par_st0" => PascalParamKind.St0,
            "par_sreg" => PascalParamKind.Sreg,
            "par_cr" => PascalParamKind.Cr,
            "par_dr" => PascalParamKind.Dr,
            "par_m8" => PascalParamKind.M8,
            "par_m16" => PascalParamKind.M16,
            "par_m32" => PascalParamKind.M32,
            "par_m64" => PascalParamKind.M64,
            "par_m80" => PascalParamKind.M80,
            "par_m128" => PascalParamKind.M128,
            "par_moffs8" => PascalParamKind.Moffs8,
            "par_moffs16" => PascalParamKind.Moffs16,
            "par_moffs32" => PascalParamKind.Moffs32,
            "par_rm8" => PascalParamKind.Rm8,
            "par_rm16" => PascalParamKind.Rm16,
            "par_rm32" => PascalParamKind.Rm32,
            "par_r32_m16" => PascalParamKind.R32M16,
            "par_mm_m32" => PascalParamKind.MmM32,
            "par_mm_m64" => PascalParamKind.MmM64,
            "par_xmm_m32" => PascalParamKind.XmmM32,
            "par_xmm_m64" => PascalParamKind.XmmM64,
            "par_xmm_m128" => PascalParamKind.XmmM128,
            "par_imm8" => PascalParamKind.Imm8,
            "par_imm16" => PascalParamKind.Imm16,
            "par_imm32" => PascalParamKind.Imm32,
            "par_rel8" => PascalParamKind.Rel8,
            "par_rel16" => PascalParamKind.Rel16,
            "par_rel32" => PascalParamKind.Rel32,
            _ => PascalParamKind.NoParam,
        };
    }

    private static byte ParseHexByte(string value)
    {
        var trimmed = value.Trim();
        if (trimmed.StartsWith("$", StringComparison.Ordinal))
        {
            return byte.Parse(trimmed[1..], NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture);
        }

        return byte.Parse(trimmed, CultureInfo.InvariantCulture);
    }

    private static Dictionary<string, PascalRegisterInfo> BuildRegisters()
    {
        var registers = new Dictionary<string, PascalRegisterInfo>(StringComparer.OrdinalIgnoreCase);

        AddRegister(registers, "AL", 0, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "CL", 1, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "DL", 2, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "BL", 3, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "AH", 4, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "CH", 5, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "DH", 6, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "BH", 7, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);

        AddRegisterSet(registers, PascalTokenType.Register16Bit, PascalRegisterCategory.General, 16, ["AX", "CX", "DX", "BX", "SP", "BP", "SI", "DI"]);
        AddRegisterSet(registers, PascalTokenType.Register32Bit, PascalRegisterCategory.General, 32, ["EAX", "ECX", "EDX", "EBX", "ESP", "EBP", "ESI", "EDI"]);
        AddRegisterSet(registers, PascalTokenType.Register64Bit, PascalRegisterCategory.General, 64, ["RAX", "RCX", "RDX", "RBX", "RSP", "RBP", "RSI", "RDI", "R8", "R9", "R10", "R11", "R12", "R13", "R14", "R15"]);

        AddRegister(registers, "SPL", 4, PascalTokenType.Register8BitWithPrefix, PascalRegisterCategory.General, 8);
        AddRegister(registers, "BPL", 5, PascalTokenType.Register8BitWithPrefix, PascalRegisterCategory.General, 8);
        AddRegister(registers, "SIL", 6, PascalTokenType.Register8BitWithPrefix, PascalRegisterCategory.General, 8);
        AddRegister(registers, "DIL", 7, PascalTokenType.Register8BitWithPrefix, PascalRegisterCategory.General, 8);

        AddRegister(registers, "R8L", 8, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R9L", 9, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R10L", 10, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R11L", 11, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R12L", 12, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R13L", 13, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R14L", 14, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R15L", 15, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R8B", 8, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R9B", 9, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R10B", 10, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R11B", 11, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R12B", 12, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R13B", 13, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R14B", 14, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegister(registers, "R15B", 15, PascalTokenType.Register8Bit, PascalRegisterCategory.General, 8);
        AddRegisterSet(registers, PascalTokenType.Register16Bit, PascalRegisterCategory.General, 16, ["R8W", "R9W", "R10W", "R11W", "R12W", "R13W", "R14W", "R15W"], 8);
        AddRegisterSet(registers, PascalTokenType.Register32Bit, PascalRegisterCategory.General, 32, ["R8D", "R9D", "R10D", "R11D", "R12D", "R13D", "R14D", "R15D"], 8);

        for (var index = 0; index <= 15; index++)
        {
            AddRegister(registers, $"MM{index}", index, PascalTokenType.RegisterMM, PascalRegisterCategory.Mm, 64);
            AddRegister(registers, $"XMM{index}", index, PascalTokenType.RegisterXMM, PascalRegisterCategory.Xmm, 128);
            AddRegister(registers, $"ST({index})", index, PascalTokenType.RegisterST, PascalRegisterCategory.St, 80);
            AddRegister(registers, $"CR{index}", index, PascalTokenType.RegisterCR, PascalRegisterCategory.Cr, 32);
            AddRegister(registers, $"DR{index}", index, PascalTokenType.RegisterDR, PascalRegisterCategory.Dr, 32);
        }

        AddRegister(registers, "ST", 0, PascalTokenType.RegisterST, PascalRegisterCategory.St, 80);
        AddRegisterSet(registers, PascalTokenType.RegisterSreg, PascalRegisterCategory.Segment, 16, ["ES", "CS", "SS", "DS", "FS", "GS", "HS", "IS", "JS", "KS", "LS", "MS", "NS", "OS", "PS"]);

        return registers;
    }

    private static void AddRegisterSet(
        Dictionary<string, PascalRegisterInfo> registers,
        PascalTokenType tokenType,
        PascalRegisterCategory category,
        int sizeBits,
        IReadOnlyList<string> names,
        int codeOffset = 0)
    {
        for (var index = 0; index < names.Count; index++)
        {
            AddRegister(registers, names[index], codeOffset + index, tokenType, category, sizeBits);
        }
    }

    private static void AddRegister(
        Dictionary<string, PascalRegisterInfo> registers,
        string name,
        int code,
        PascalTokenType tokenType,
        PascalRegisterCategory category,
        int sizeBits)
    {
        registers[name] = new PascalRegisterInfo(name, code, tokenType, category, sizeBits);
    }
}