using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace CheatEngine.Library;

internal sealed class AutoAssemblerStrictContext
{
    public Dictionary<string, string> Defines { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, nuint> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, nuint> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DeclaredLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> DefinedLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> ExplicitLabels { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> EphemeralAllocSymbols { get; } = new(StringComparer.OrdinalIgnoreCase);

    public HashSet<string> NewGlobalAllocations { get; } = new(StringComparer.OrdinalIgnoreCase);

    public List<string> PendingRegisterSymbols { get; } = [];

    public List<string> PendingUnregisterSymbols { get; } = [];

    public List<string> PendingDeallocations { get; } = [];

    public List<string> PendingCreateThreads { get; } = [];

    public List<AutoAssemblerFullAccessRequest> PendingFullAccess { get; } = [];

    public List<AutoAssemblerLoadBinaryRequest> PendingLoadBinary { get; } = [];
}

internal sealed record AutoAssemblerFullAccessRequest(string AddressExpression, int Size);

internal sealed record AutoAssemblerLoadBinaryRequest(string AddressExpression, string FilePath);

internal sealed record AutoAssemblerWrite(nuint Address, byte[] Bytes);

public static partial class AutoAssembler
{
    private const uint StrictPageReadWrite = 0x04;
    private const uint StrictPageExecuteReadWrite = 0x40;
    private const uint StrictMemCommit = 0x1000;
    private const uint StrictMemReserve = 0x2000;
    private const uint StrictMemRelease = 0x8000;
    private const uint StrictInfinite = 0xFFFFFFFF;
    private const uint StrictWaitTimeout = 0x00000102;
    private static readonly object StrictGlobalAllocSync = new();

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint GetModuleHandleW(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, SetLastError = true)]
    private static extern nint GetProcAddress(nint hModule, string procName);

    private static bool AutoAssembleStrict(IReadOnlyList<string> code, bool popupMessages, bool enable, bool syntaxCheckOnly, bool targetSelf, List<CeAlloc> ceAllocArray, List<string>? registeredSymbols)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(ceAllocArray);

        var effectiveRegisteredSymbols = registeredSymbols ?? new List<string>();
        var originalAllocCount = ceAllocArray.Count;
        var context = new AutoAssemblerStrictContext();
        var originalTargetSelf = SymbolHandler.Default.TargetSelf;

        try
        {
            SymbolHandler.Default.TargetSelf = targetSelf;

            var selectedLines = StrictSelectSection(code, enable);
            var preparedLines = StrictPrepareScriptLines(selectedLines);
            var expandedLines = StrictExpandIncludes(preparedLines, Directory.GetCurrentDirectory());
            var activeLines = StrictExpandStructures(expandedLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList());
            StrictCollectDeclaredLabels(activeLines, context);
            StrictPreprocessDirectives(activeLines, enable, syntaxCheckOnly, targetSelf, ceAllocArray, context);
            StrictResolveLabels(activeLines, context);
            StrictValidatePendingSymbolRegistrations(context);
            StrictExecuteLines(activeLines, syntaxCheckOnly, targetSelf, ceAllocArray, effectiveRegisteredSymbols, context);
            return true;
        }
        catch
        {
            StrictCleanupAfterFailure(targetSelf, ceAllocArray, originalAllocCount, context);
            if (popupMessages)
            {
                throw;
            }

            return false;
        }
        finally
        {
            SymbolHandler.Default.TargetSelf = originalTargetSelf;
        }
    }

    private static List<string> StrictExpandIncludes(IReadOnlyList<string> lines, string baseDirectory)
    {
        var result = new List<string>(lines.Count);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseDirective(line, "include", out var includeArguments))
            {
                var includePath = StrictResolveFilePath(StrictUnquote(includeArguments[0]), baseDirectory, ".cea");
                var nextBaseDirectory = Path.GetDirectoryName(includePath);
                var includeLines = StrictPrepareScriptLines(File.ReadAllLines(includePath));
                result.AddRange(StrictExpandIncludes(includeLines, string.IsNullOrWhiteSpace(nextBaseDirectory) ? baseDirectory : nextBaseDirectory));
                continue;
            }

            result.Add(line);
        }

        return result;
    }

    private static List<string> StrictPrepareScriptLines(IReadOnlyList<string> rawLines)
    {
        var cleaned = StrictRemoveComments(rawLines);
        StrictExpandUnlabeledLabels(cleaned);
        return cleaned;
    }

    private static List<string> StrictRemoveComments(IReadOnlyList<string> rawLines)
    {
        var result = new List<string>(rawLines.Count);
        var inComment = false;
        for (var lineIndex = 0; lineIndex < rawLines.Count; lineIndex++)
        {
            var line = rawLines[lineIndex] ?? string.Empty;
            var buffer = line.ToCharArray();
            var inSingleQuote = false;
            var inDoubleQuote = false;

            for (var index = 0; index < buffer.Length; index++)
            {
                if (inComment)
                {
                    if (buffer[index] == '}' || (buffer[index] == '*' && index < buffer.Length - 1 && buffer[index + 1] == '/'))
                    {
                        inComment = false;
                        if (buffer[index] == '*' && index < buffer.Length - 1 && buffer[index + 1] == '/')
                        {
                            buffer[index + 1] = ' ';
                        }
                    }

                    buffer[index] = ' ';
                    continue;
                }

                if (buffer[index] == '\'' && !inDoubleQuote)
                {
                    inSingleQuote = !inSingleQuote;
                }
                else if (buffer[index] == '"' && !inSingleQuote)
                {
                    inDoubleQuote = !inDoubleQuote;
                }

                if (buffer[index] == '\t')
                {
                    buffer[index] = ' ';
                }

                if (inSingleQuote || inDoubleQuote)
                {
                    continue;
                }

                if (buffer[index] == '/' && index < buffer.Length - 1 && buffer[index + 1] == '/')
                {
                    line = new string(buffer, 0, index);
                    buffer = line.ToCharArray();
                    break;
                }

                if (buffer[index] == '{' || (buffer[index] == '/' && index < buffer.Length - 1 && buffer[index + 1] == '*'))
                {
                    inComment = true;
                    buffer[index] = ' ';
                }
            }

            result.Add(new string(buffer).Trim());
        }

        return result;
    }

    private static void StrictExpandUnlabeledLabels(List<string> lines)
    {
        var generatedLabelDeclarations = new List<string>();
        var labels = new List<string>();

        for (var index = 0; index < lines.Count; index++)
        {
            var currentLine = lines[index];
            if (currentLine.Length <= 1)
            {
                continue;
            }

            if (string.Equals(currentLine, "@@:", StringComparison.Ordinal))
            {
                var generatedLabel = StrictGenerateLegacyLabel();
                lines[index] = generatedLabel + ':';
                generatedLabelDeclarations.Add($"label({generatedLabel})");
                labels.Add(generatedLabel);
                continue;
            }

            if (currentLine.EndsWith(':'))
            {
                labels.Add(currentLine[..^1]);
            }
        }

        if (labels.Count == 0 && generatedLabelDeclarations.Count == 0)
        {
            return;
        }

        var lastSeenLabel = -1;
        for (var index = 0; index < lines.Count; index++)
        {
            var currentLine = lines[index];
            if (currentLine.Length <= 1)
            {
                continue;
            }

            if (currentLine.EndsWith(':'))
            {
                var labelName = currentLine[..^1];
                for (var labelIndex = lastSeenLabel + 1; labelIndex < labels.Count; labelIndex++)
                {
                    if (!string.Equals(labelName, labels[labelIndex], StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    lastSeenLabel = labelIndex;
                    break;
                }

                continue;
            }

            if (currentLine.Contains("@f", StringComparison.OrdinalIgnoreCase))
            {
                if (lastSeenLabel + 1 >= labels.Count)
                {
                    throw new InvalidOperationException("Forward jump with no label defined");
                }

                currentLine = StrictReplaceToken(currentLine, "@f", labels[lastSeenLabel + 1]);
                currentLine = StrictReplaceToken(currentLine, "@F", labels[lastSeenLabel + 1]);
            }
            else if (currentLine.Contains("@b", StringComparison.OrdinalIgnoreCase))
            {
                if (lastSeenLabel < 0)
                {
                    throw new InvalidOperationException("There is code defined without specifying the address it belongs to");
                }

                currentLine = StrictReplaceToken(currentLine, "@b", labels[lastSeenLabel]);
                currentLine = StrictReplaceToken(currentLine, "@B", labels[lastSeenLabel]);
            }

            lines[index] = currentLine;
        }

        if (generatedLabelDeclarations.Count == 0)
        {
            return;
        }

        lines.InsertRange(0, generatedLabelDeclarations);
    }

    private static string StrictGenerateLegacyLabel()
    {
        return "RandomLabel" + Guid.NewGuid().ToString("N")[..9].ToUpperInvariant();
    }

    private static List<string> StrictSelectSection(IReadOnlyList<string> lines, bool enable)
    {
        if (!GetEnableAndDisablePos(lines, out var enablePos, out var disablePos))
        {
            return lines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        }

        if (enable && enablePos < 0)
        {
            throw new InvalidOperationException("You haven't specified an enable section");
        }

        if (!enable && disablePos < 0)
        {
            throw new InvalidOperationException("You haven't specified a disable section");
        }

        var start = enable ? enablePos + 1 : disablePos + 1;
        var end = enable ? (disablePos >= 0 ? disablePos : lines.Count) : lines.Count;
        return lines.Skip(start).Take(Math.Max(0, end - start)).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }

    private static List<string> StrictExpandStructures(IReadOnlyList<string> lines)
    {
        var expanded = new List<string>(lines.Count);
        for (var index = 0; index < lines.Count; index++)
        {
            var currentLine = lines[index].Trim();
            if (!currentLine.StartsWith("STRUCT ", StringComparison.OrdinalIgnoreCase))
            {
                expanded.Add(lines[index]);
                continue;
            }

            expanded.AddRange(StrictReplaceStructureWithDefines(lines, ref index));
        }

        return expanded;
    }

    private static IReadOnlyList<string> StrictReplaceStructureWithDefines(IReadOnlyList<string> lines, ref int structureIndex)
    {
        var structName = lines[structureIndex].Trim()[7..].Trim();
        var elements = new List<(string Name, int Offset)>();
        var currentOffset = 0;
        var endFound = false;
        var lastLine = structureIndex;

        for (var index = structureIndex + 1; index < lines.Count; index++)
        {
            lastLine = index;
            var tokens = StrictTokenizeStructureLine(lines[index]);
            var tokenIndex = 0;

            if (tokens.Count > 0)
            {
                if (tokens[0].EndsWith(':'))
                {
                    var elementName = tokens[0][..^1];
                    if (AssemblerUnit.GetOpcodesIndex(elementName) != -1)
                    {
                        throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1} :{elementName} is a reserved word");
                    }

                    elements.Add((elementName, currentOffset));
                    tokenIndex = 1;
                }

                if (string.Equals(tokens[0], "ENDSTRUCT", StringComparison.OrdinalIgnoreCase) || string.Equals(tokens[0], "ENDS", StringComparison.OrdinalIgnoreCase))
                {
                    endFound = true;
                    break;
                }
            }

            while (tokenIndex < tokens.Count)
            {
                var token = tokens[tokenIndex].ToUpperInvariant();
                if (token.Length == 4 && token.StartsWith("RES", StringComparison.Ordinal))
                {
                    var byteSize = token[3] switch
                    {
                        'B' => 1,
                        'W' => 2,
                        'D' => 4,
                        'Q' => 8,
                        _ => throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1}.")
                    };

                    tokenIndex++;
                    if (tokenIndex >= tokens.Count || !int.TryParse(tokens[tokenIndex], NumberStyles.Integer, CultureInfo.InvariantCulture, out var count))
                    {
                        throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1}.");
                    }

                    currentOffset += byteSize * count;
                    tokenIndex++;
                    continue;
                }

                if (token.Length == 2 && token[0] == 'D')
                {
                    var byteSize = token[1] switch
                    {
                        'B' => 1,
                        'W' => 2,
                        'D' => 4,
                        'Q' => 8,
                        _ => throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1}.")
                    };

                    tokenIndex++;
                    if (tokenIndex >= tokens.Count)
                    {
                        throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1}.");
                    }

                    currentOffset += byteSize;
                    while (tokenIndex < tokens.Count - 1 && tokens[tokenIndex + 1] == "?")
                    {
                        currentOffset += byteSize;
                        tokenIndex++;
                    }

                    tokenIndex++;
                    continue;
                }

                throw new InvalidOperationException($"Error in the structure definition of {structName} at line {index + 1} :No idea what {tokens[tokenIndex]} is");
            }
        }

        if (!endFound)
        {
            throw new InvalidOperationException($"Error in the structure definition of {structName} at line {lastLine + 1} :No end found");
        }

        structureIndex = lastLine;

        var defines = new List<string>();
        for (var index = elements.Count - 1; index >= 0; index--)
        {
            defines.Add($"define({structName}.{elements[index].Name},{elements[index].Offset:X})");
            defines.Add($"define({elements[index].Name},{elements[index].Offset:X})");
        }

        defines.Insert(0, $"define({structName}_size,{currentOffset:X})");
        return defines;
    }

    private static List<string> StrictTokenizeStructureLine(string line)
    {
        return line.Split([' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
    }

    private static void StrictCollectDeclaredLabels(IReadOnlyList<string> lines, AutoAssemblerStrictContext context)
    {
        foreach (var line in lines)
        {
            if (TryParseDirective(line, "label", out var labelArguments))
            {
                var labelName = labelArguments[0].Trim();
                StrictValidateIdentifier(labelName);
                context.ExplicitLabels.Add(labelName);
                context.DeclaredLabels.Add(labelName);
                continue;
            }

            if (StrictTryGetLabelDefinition(line, out var definedLabel))
            {
                context.DefinedLabels.Add(definedLabel);
            }
        }

        foreach (var explicitLabel in context.ExplicitLabels)
        {
            if (!context.DefinedLabels.Contains(explicitLabel))
            {
                throw new InvalidOperationException($"label {explicitLabel} is not defined in the script");
            }
        }
    }

    private static void StrictPreprocessDirectives(IReadOnlyList<string> lines, bool enable, bool syntaxCheckOnly, bool targetSelf, List<CeAlloc> ceAllocArray, AutoAssemblerStrictContext context)
    {
        foreach (var rawLine in lines)
        {
            if (TryParseDirective(rawLine, "registersymbol", out var registerArguments))
            {
                context.PendingRegisterSymbols.Add(registerArguments[0].Trim());
                continue;
            }

            if (TryParseDirective(rawLine, "unregistersymbol", out var unregisterArguments))
            {
                context.PendingUnregisterSymbols.Add(unregisterArguments[0].Trim());
                continue;
            }

            if (TryParseDirective(rawLine, "define", out var defineArguments))
            {
                var defineName = defineArguments[0].Trim();
                context.Defines[defineName] = StrictApplyDefines(defineArguments[1], context);
                continue;
            }

            var line = StrictApplyDefines(rawLine, context);

            if (TryParseDirective(line, "assert", out var assertArguments))
            {
                StrictValidateAssert(assertArguments, syntaxCheckOnly, targetSelf, context);
                continue;
            }

            if (TryParseDirective(line, "aobscanmodule", out var aobModuleArguments))
            {
                if (aobModuleArguments.Count != 3)
                {
                    throw new InvalidOperationException("Wrong syntax. AOBSCANMODULE(name,module,pattern)");
                }

                context.Symbols[aobModuleArguments[0].Trim()] = syntaxCheckOnly
                    ? StrictPlaceholderAddress(targetSelf)
                    : StrictAobScan(aobModuleArguments[2], aobModuleArguments[1], targetSelf);
                continue;
            }

            if (TryParseDirective(line, "aobscan", out var aobArguments))
            {
                if (aobArguments.Count != 2)
                {
                    throw new InvalidOperationException("Wrong syntax. AOBSCAN(name,pattern)");
                }

                context.Symbols[aobArguments[0].Trim()] = syntaxCheckOnly
                    ? StrictPlaceholderAddress(targetSelf)
                    : StrictAobScan(aobArguments[1], null, targetSelf);
                continue;
            }

            if (TryParseDirective(line, "alloc", out var allocArguments)
                || TryParseDirective(line, "kalloc", out allocArguments))
            {
                StrictHandleAllocDirective(line, allocArguments, enable, syntaxCheckOnly, targetSelf, ceAllocArray, context);
                continue;
            }

            if (TryParseDirective(line, "sharedalloc", out var sharedAllocArguments)
                || TryParseDirective(line, "globalalloc", out sharedAllocArguments))
            {
                StrictHandleGlobalAllocDirective(sharedAllocArguments, syntaxCheckOnly, context);
                continue;
            }

            if (TryParseDirective(line, "fullaccess", out var fullAccessArguments))
            {
                if (fullAccessArguments.Count != 2)
                {
                    throw new InvalidOperationException("Syntax error. FullAccess(address,size)");
                }

                var size = checked((int)StrictParseAddressWithContext(fullAccessArguments[1], context));
                context.PendingFullAccess.Add(new AutoAssemblerFullAccessRequest(fullAccessArguments[0], size));
                continue;
            }

            if (TryParseDirective(line, "loadbinary", out var loadBinaryArguments))
            {
                if (loadBinaryArguments.Count != 2)
                {
                    throw new InvalidOperationException("Wrong syntax. LoadBinary(address,filename)");
                }

                var filePath = StrictResolveFilePath(StrictUnquote(loadBinaryArguments[1]), Directory.GetCurrentDirectory(), null);
                context.PendingLoadBinary.Add(new AutoAssemblerLoadBinaryRequest(loadBinaryArguments[0], filePath));
                continue;
            }

            if (TryParseDirective(line, "createthread", out var createThreadArguments))
            {
                if (createThreadArguments.Count != 1)
                {
                    throw new InvalidOperationException("Wrong syntax. CreateThread(address)");
                }

                context.PendingCreateThreads.Add(createThreadArguments[0].Trim());
                continue;
            }

            if (TryParseDirective(line, "loadlibrary", out var loadLibraryArguments))
            {
                if (loadLibraryArguments.Count != 1)
                {
                    throw new InvalidOperationException("Wrong syntax. LoadLibrary(filename)");
                }

                if (!syntaxCheckOnly)
                {
                    StrictLoadLibraryIntoTarget(StrictUnquote(loadLibraryArguments[0]), targetSelf);
                }

                continue;
            }

            if (TryParseDirective(line, "dealloc", out var deallocArguments))
            {
                if (deallocArguments.Count != 1)
                {
                    throw new InvalidOperationException("Wrong syntax. DEALLOC(identifier)");
                }

                context.PendingDeallocations.Add(deallocArguments[0].Trim());
            }
        }
    }

    private static void StrictHandleAllocDirective(string sourceLine, IReadOnlyList<string> allocArguments, bool enable, bool syntaxCheckOnly, bool targetSelf, List<CeAlloc> ceAllocArray, AutoAssemblerStrictContext context)
    {
        if (allocArguments.Count < 2 || allocArguments.Count > 3)
        {
            throw new InvalidOperationException(sourceLine.StartsWith("kalloc(", StringComparison.OrdinalIgnoreCase)
                ? "Wrong syntax. KALLOC(identifier,sizeinbytes)"
                : "Wrong syntax. ALLOC(identifier,sizeinbytes)");
        }

        var symbolName = allocArguments[0].Trim();
        StrictValidateIdentifier(symbolName);

        if (!enable)
        {
            var existing = ceAllocArray.LastOrDefault(alloc => string.Equals(alloc.VarName, symbolName, StringComparison.OrdinalIgnoreCase));
            if (existing is not null && !string.IsNullOrWhiteSpace(existing.VarName))
            {
                context.Symbols[symbolName] = existing.Address;
            }
            else
            {
                context.Symbols[symbolName] = StrictPlaceholderAddress(targetSelf);
            }

            return;
        }

        if (syntaxCheckOnly)
        {
            context.Symbols[symbolName] = StrictPlaceholderAddress(targetSelf);
            return;
        }

        if (ceAllocArray.Any(alloc => string.Equals(alloc.VarName, symbolName, StringComparison.OrdinalIgnoreCase)))
        {
            throw new InvalidOperationException($"The identifier {symbolName} has already been declared");
        }

        var size = checked((uint)StrictParseAddressWithContext(allocArguments[1], context));
        var preferred = allocArguments.Count == 3 ? StrictParseAddressWithContext(allocArguments[2], context) : 0;
        var handle = StrictResolveTargetProcessHandle(targetSelf);
        var address = NewKernelHandler.VirtualAllocEx(handle, preferred, size, StrictMemCommit | StrictMemReserve, StrictPageExecuteReadWrite);
        if (address == 0 && preferred != 0)
        {
            address = NewKernelHandler.VirtualAllocEx(handle, 0, size, StrictMemCommit | StrictMemReserve, StrictPageExecuteReadWrite);
        }

        if (address == 0)
        {
            throw new InvalidOperationException("Failure to allocate memory");
        }

        ceAllocArray.Add(new CeAlloc(address, symbolName, size, preferred));
        context.Symbols[symbolName] = address;
        context.EphemeralAllocSymbols.Add(symbolName);
    }

    private static void StrictHandleGlobalAllocDirective(IReadOnlyList<string> globalAllocArguments, bool syntaxCheckOnly, AutoAssemblerStrictContext context)
    {
        if (globalAllocArguments.Count != 2)
        {
            throw new InvalidOperationException("Wrong syntax. GLOBALALLOC(name,size)");
        }

        var symbolName = globalAllocArguments[0].Trim();
        StrictValidateIdentifier(symbolName);
        if (syntaxCheckOnly)
        {
            context.Symbols[symbolName] = StrictPlaceholderAddress(false);
            return;
        }

        var size = checked((uint)StrictParseAddressWithContext(globalAllocArguments[1], context));
        nuint address;
        lock (StrictGlobalAllocSync)
        {
            if (!GlobalAllocations.TryGetValue(symbolName, out address))
            {
                SymbolHandler.Default.SetUserDefinedSymbolAllocSize(symbolName, size);
                address = SymbolHandler.Default.GetUserDefinedSymbolByName(symbolName);
                if (address == 0)
                {
                    throw new InvalidOperationException("Failure to allocate memory");
                }

                GlobalAllocations[symbolName] = address;
                context.NewGlobalAllocations.Add(symbolName);
            }
            else
            {
                SymbolHandler.Default.SetUserDefinedSymbolAllocSize(symbolName, size);
                address = SymbolHandler.Default.GetUserDefinedSymbolByName(symbolName);
                GlobalAllocations[symbolName] = address;
            }
        }

        context.Symbols[symbolName] = address;
    }

    private static void StrictValidateAssert(IReadOnlyList<string> assertArguments, bool syntaxCheckOnly, bool targetSelf, AutoAssemblerStrictContext context)
    {
        if (assertArguments.Count != 2)
        {
            throw new InvalidOperationException("Wrong syntax. ASSERT(address,11 22 33 ** 55 66)");
        }

        if (syntaxCheckOnly)
        {
            return;
        }

        var address = StrictParseAddressWithContext(assertArguments[0], context);
        var expected = StrictParseBytePattern(assertArguments[1]);
        if (expected.Length == 0)
        {
            throw new InvalidOperationException($"{assertArguments[1]} is not a valid bytestring");
        }

        var buffer = new byte[expected.Length];
        if (!NewKernelHandler.ReadProcessMemory(StrictResolveTargetProcessHandle(targetSelf), address, buffer, out var numberOfBytesRead) || numberOfBytesRead < (nuint)buffer.Length)
        {
            throw new InvalidOperationException($"The memory at {assertArguments[0]} can not be read");
        }

        for (var index = 0; index < expected.Length; index++)
        {
            if (expected[index] >= 0 && buffer[index] != expected[index])
            {
                throw new InvalidOperationException($"The bytes at {assertArguments[0]} are not what was expected");
            }
        }
    }

    private static void StrictResolveLabels(IReadOnlyList<string> lines, AutoAssemblerStrictContext context)
    {
        foreach (var declaredLabel in context.DeclaredLabels)
        {
            context.Labels.TryAdd(declaredLabel, 0);
        }

        for (var pass = 0; pass < 8; pass++)
        {
            var previousLabels = context.Labels.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase);
            var resolvedLabels = new Dictionary<string, nuint>(StringComparer.OrdinalIgnoreCase);
            nuint currentAddress = 0;

            foreach (var rawLine in lines)
            {
                if (StrictTryHandleResolutionDirective(rawLine, context, ref currentAddress))
                {
                    continue;
                }

                var line = StrictApplyDefines(rawLine, context);
                if (line.Length == 0)
                {
                    continue;
                }

                if (StrictTryHandleResolutionLabel(line, context, resolvedLabels, ref currentAddress))
                {
                    continue;
                }

                if (currentAddress == 0)
                {
                    throw new InvalidOperationException("There is code defined without specifying the address it belongs to");
                }

                var prepared = StrictApplySymbols(line, context);
                if (AssemblerUnit.Assemble(prepared, currentAddress, out var estimatedBytes, AssemblerPreference.X64, skipRangeCheck: true))
                {
                    currentAddress += (nuint)estimatedBytes.Length;
                }
                else
                {
                    currentAddress += (nuint)AssemblerUnit.GetEstimatedSize(prepared, AssemblerPreference.X64);
                }
            }

            context.Labels.Clear();
            foreach (var pair in resolvedLabels)
            {
                context.Labels[pair.Key] = pair.Value;
            }

            foreach (var declaredLabel in context.DeclaredLabels)
            {
                context.Labels.TryAdd(declaredLabel, 0);
            }

            if (StrictLabelsEqual(previousLabels, context.Labels))
            {
                return;
            }
        }
    }

    private static bool StrictTryHandleResolutionDirective(string rawLine, AutoAssemblerStrictContext context, ref nuint currentAddress)
    {
        if (TryParseDirective(rawLine, "registersymbol", out _)
            || TryParseDirective(rawLine, "unregistersymbol", out _)
            || TryParseDirective(rawLine, "label", out _)
            || TryParseDirective(rawLine, "define", out _))
        {
            return true;
        }

        var line = StrictApplyDefines(rawLine, context);
        if (TryParseDirective(line, "assert", out _)
            || TryParseDirective(line, "alloc", out _)
            || TryParseDirective(line, "kalloc", out _)
            || TryParseDirective(line, "globalalloc", out _)
            || TryParseDirective(line, "sharedalloc", out _)
            || TryParseDirective(line, "aobscan", out _)
            || TryParseDirective(line, "aobscanmodule", out _)
            || TryParseDirective(line, "fullaccess", out _)
            || TryParseDirective(line, "loadbinary", out _)
            || TryParseDirective(line, "createthread", out _)
            || TryParseDirective(line, "loadlibrary", out _)
            || TryParseDirective(line, "dealloc", out _))
        {
            return true;
        }

        if (TryParseDirective(line, "readmem", out var readMemArguments))
        {
            if (readMemArguments.Count != 2)
            {
                throw new InvalidOperationException("Wrong syntax. ReadMem(address,size)");
            }

            if (currentAddress == 0)
            {
                throw new InvalidOperationException("There is code defined without specifying the address it belongs to");
            }

            currentAddress += StrictParseAddressWithContext(readMemArguments[1], context);
            return true;
        }

        return false;
    }

    private static bool StrictTryHandleResolutionLabel(string line, AutoAssemblerStrictContext context, Dictionary<string, nuint> resolvedLabels, ref nuint currentAddress)
    {
        if (!line.EndsWith(':'))
        {
            return false;
        }

        var labelName = line[..^1].Trim();
        if (context.ExplicitLabels.Contains(labelName))
        {
            resolvedLabels[labelName] = currentAddress;
            return true;
        }

        if (!StrictTryResolveAddress(labelName, context, out var resolved))
        {
            throw new InvalidOperationException("This address specifier is not valid");
        }

        currentAddress = resolved;
        return true;
    }

    private static void StrictValidatePendingSymbolRegistrations(AutoAssemblerStrictContext context)
    {
        foreach (var symbolName in context.PendingRegisterSymbols)
        {
            if (context.Symbols.ContainsKey(symbolName) || context.Labels.ContainsKey(symbolName))
            {
                continue;
            }

            if (context.Defines.TryGetValue(symbolName, out var definitionValue))
            {
                if (!StrictTryResolveAddress(definitionValue, context, out _))
                {
                    throw new InvalidOperationException($"{symbolName} was supposed to be added to the symbollist, but it isn't declared");
                }

                continue;
            }

            throw new InvalidOperationException($"{symbolName} was supposed to be added to the symbollist, but it isn't declared");
        }
    }

    private static void StrictExecuteLines(IReadOnlyList<string> lines, bool syntaxCheckOnly, bool targetSelf, List<CeAlloc> ceAllocArray, List<string> registeredSymbols, AutoAssemblerStrictContext context)
    {
        var writes = new List<AutoAssemblerWrite>();
        nuint currentAddress = 0;

        foreach (var rawLine in lines)
        {
            if (TryParseDirective(rawLine, "registersymbol", out _)
                || TryParseDirective(rawLine, "unregistersymbol", out _)
                || TryParseDirective(rawLine, "label", out _)
                || TryParseDirective(rawLine, "define", out _))
            {
                continue;
            }

            var line = StrictApplyDefines(rawLine, context);
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseDirective(line, "assert", out _)
                || TryParseDirective(line, "alloc", out _)
                || TryParseDirective(line, "kalloc", out _)
                || TryParseDirective(line, "globalalloc", out _)
                || TryParseDirective(line, "sharedalloc", out _)
                || TryParseDirective(line, "aobscan", out _)
                || TryParseDirective(line, "aobscanmodule", out _)
                || TryParseDirective(line, "fullaccess", out _)
                || TryParseDirective(line, "loadbinary", out _)
                || TryParseDirective(line, "createthread", out _)
                || TryParseDirective(line, "loadlibrary", out _)
                || TryParseDirective(line, "dealloc", out _))
            {
                continue;
            }

            if (TryParseDirective(line, "readmem", out var readMemArguments))
            {
                if (readMemArguments.Count != 2)
                {
                    throw new InvalidOperationException("Wrong syntax. ReadMem(address,size)");
                }

                if (currentAddress == 0)
                {
                    throw new InvalidOperationException("There is code defined without specifying the address it belongs to");
                }

                var sourceAddress = StrictParseAddressWithContext(readMemArguments[0], context);
                var size = checked((int)StrictParseAddressWithContext(readMemArguments[1], context));
                if (!syntaxCheckOnly)
                {
                    var buffer = new byte[size];
                    if (!NewKernelHandler.ReadProcessMemory(StrictResolveTargetProcessHandle(targetSelf), sourceAddress, buffer, out var bytesRead) || bytesRead < (nuint)size)
                    {
                        throw new InvalidOperationException($"The memory at {readMemArguments[0]} could not be fully read");
                    }

                    writes.Add(new AutoAssemblerWrite(currentAddress, buffer));
                }

                currentAddress += (nuint)size;
                continue;
            }

            if (line.EndsWith(':'))
            {
                var labelName = line[..^1].Trim();
                if (context.ExplicitLabels.Contains(labelName))
                {
                    currentAddress = context.Labels.GetValueOrDefault(labelName);
                    continue;
                }

                if (!StrictTryResolveAddress(labelName, context, out var resolvedAnchor))
                {
                    throw new InvalidOperationException("This address specifier is not valid");
                }

                currentAddress = resolvedAnchor;
                continue;
            }

            if (currentAddress == 0)
            {
                throw new InvalidOperationException("There is code defined without specifying the address it belongs to");
            }

            var assembledLine = StrictApplySymbols(line, context);
            if (!AssemblerUnit.Assemble(assembledLine, currentAddress, out var bytes, AssemblerPreference.X64, skipRangeCheck: true))
            {
                throw new InvalidOperationException($"Failed to assemble line: {assembledLine}");
            }

            if (!syntaxCheckOnly)
            {
                writes.Add(new AutoAssemblerWrite(currentAddress, bytes));
            }

            currentAddress += (nuint)bytes.Length;
        }

        if (syntaxCheckOnly)
        {
            return;
        }

        StrictApplyFullAccess(targetSelf, context);
        StrictApplyLoadBinary(targetSelf, context);
        StrictApplyWrites(targetSelf, writes);
        StrictApplyPendingDeallocations(targetSelf, ceAllocArray, context);
        StrictApplyPendingSymbolRegistrations(registeredSymbols, ceAllocArray, context);
        StrictExecutePendingThreads(targetSelf, context);
    }

    private static void StrictApplyFullAccess(bool targetSelf, AutoAssemblerStrictContext context)
    {
        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        foreach (var request in context.PendingFullAccess)
        {
            var address = StrictParseAddressWithContext(request.AddressExpression, context);
            _ = NewKernelHandler.VirtualProtectEx(processHandle, address, (nuint)request.Size, StrictPageExecuteReadWrite, out _);
        }
    }

    private static void StrictApplyLoadBinary(bool targetSelf, AutoAssemblerStrictContext context)
    {
        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        foreach (var request in context.PendingLoadBinary)
        {
            var address = StrictParseAddressWithContext(request.AddressExpression, context);
            var bytes = File.ReadAllBytes(request.FilePath);
            StrictWriteBytes(processHandle, address, bytes);
        }
    }

    private static void StrictApplyWrites(bool targetSelf, IReadOnlyList<AutoAssemblerWrite> writes)
    {
        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        foreach (var write in writes)
        {
            StrictWriteBytes(processHandle, write.Address, write.Bytes);
        }
    }

    private static void StrictApplyPendingDeallocations(bool targetSelf, List<CeAlloc> ceAllocArray, AutoAssemblerStrictContext context)
    {
        if (context.PendingDeallocations.Count == 0)
        {
            return;
        }

        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        foreach (var symbolName in context.PendingDeallocations)
        {
            var index = ceAllocArray.FindLastIndex(allocation => string.Equals(allocation.VarName, symbolName, StringComparison.OrdinalIgnoreCase));
            if (index < 0)
            {
                continue;
            }

            _ = NewKernelHandler.VirtualFreeEx(processHandle, ceAllocArray[index].Address, 0, StrictMemRelease);
            ceAllocArray.RemoveAt(index);
            context.Symbols.Remove(symbolName);
            SymbolHandler.Default.DeleteUserDefinedSymbol(symbolName);
        }
    }

    private static void StrictApplyPendingSymbolRegistrations(List<string> registeredSymbols, IReadOnlyList<CeAlloc> ceAllocArray, AutoAssemblerStrictContext context)
    {
        foreach (var symbolName in context.PendingUnregisterSymbols)
        {
            SymbolHandler.Default.DeleteUserDefinedSymbol(symbolName);
            registeredSymbols.RemoveAll(item => string.Equals(item, symbolName, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var symbolName in context.PendingRegisterSymbols)
        {
            if (!StrictTryResolveRegistration(symbolName, ceAllocArray, context, out var addressString, out var doNotSave))
            {
                continue;
            }

            try
            {
                SymbolHandler.Default.DeleteUserDefinedSymbol(symbolName);
                SymbolHandler.Default.AddUserDefinedSymbol(addressString, symbolName, doNotSave);
            }
            catch (SymException)
            {
                continue;
            }

            if (!registeredSymbols.Contains(symbolName, StringComparer.OrdinalIgnoreCase))
            {
                registeredSymbols.Add(symbolName);
            }
        }
    }

    private static bool StrictTryResolveRegistration(string symbolName, IReadOnlyList<CeAlloc> ceAllocArray, AutoAssemblerStrictContext context, out string addressString, out bool doNotSave)
    {
        doNotSave = false;
        addressString = string.Empty;

        if (context.Symbols.TryGetValue(symbolName, out var symbolAddress))
        {
            addressString = "$" + symbolAddress.ToString("X", CultureInfo.InvariantCulture);
            doNotSave = context.EphemeralAllocSymbols.Contains(symbolName)
                || ceAllocArray.Any(allocation => string.Equals(allocation.VarName, symbolName, StringComparison.OrdinalIgnoreCase));
            return true;
        }

        if (context.Labels.TryGetValue(symbolName, out var labelAddress))
        {
            addressString = "$" + labelAddress.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        if (context.Defines.TryGetValue(symbolName, out var definitionValue) && StrictTryResolveAddress(definitionValue, context, out _))
        {
            addressString = definitionValue;
            return true;
        }

        return false;
    }

    private static void StrictExecutePendingThreads(bool targetSelf, AutoAssemblerStrictContext context)
    {
        if (context.PendingCreateThreads.Count == 0)
        {
            return;
        }

        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        foreach (var threadTarget in context.PendingCreateThreads)
        {
            if (!StrictTryResolveAddress(threadTarget, context, out var startAddress) || startAddress == 0)
            {
                throw new InvalidOperationException($"The address in createthread({threadTarget}) is not valid");
            }

            var threadHandle = NewKernelHandler.CreateRemoteThread(processHandle, startAddress, 0, 0, out _);
            if (threadHandle == 0)
            {
                throw new InvalidOperationException($"The address in createthread({threadTarget}) is not valid");
            }

            _ = NewKernelHandler.CloseHandle(threadHandle);
        }
    }

    private static void StrictLoadLibraryIntoTarget(string libraryPath, bool targetSelf)
    {
        var resolvedPath = StrictResolveFilePath(libraryPath, Directory.GetCurrentDirectory(), null);
        if (StrictIsCurrentProcessTarget(targetSelf))
        {
            if (!LoadedLibraries.ContainsKey(resolvedPath))
            {
                LoadedLibraries[resolvedPath] = NativeLibrary.Load(resolvedPath);
            }
        }
        else
        {
            StrictInjectLibraryIntoRemoteProcess(StrictResolveTargetProcessHandle(targetSelf), resolvedPath);
        }

        SymbolHandler.Default.Reinitialize(force: true);
        SymbolHandler.Default.WaitForSymbolsLoaded();
    }

    private static void StrictInjectLibraryIntoRemoteProcess(nint processHandle, string libraryPath)
    {
        var payload = Encoding.Unicode.GetBytes(Path.GetFullPath(libraryPath) + "\0");
        var remoteBuffer = NewKernelHandler.VirtualAllocEx(processHandle, 0, (nuint)payload.Length, StrictMemCommit | StrictMemReserve, StrictPageReadWrite);
        if (remoteBuffer == 0)
        {
            throw new InvalidOperationException($"{libraryPath} could not be injected");
        }

        try
        {
            if (!NewKernelHandler.WriteProcessMemory(processHandle, remoteBuffer, payload, out var bytesWritten) || bytesWritten < (nuint)payload.Length)
            {
                throw new InvalidOperationException($"{libraryPath} could not be injected");
            }

            var kernel32 = GetModuleHandleW("kernel32.dll");
            var loadLibrary = GetProcAddress(kernel32, "LoadLibraryW");
            if (kernel32 == 0 || loadLibrary == 0)
            {
                throw new InvalidOperationException($"{libraryPath} could not be injected");
            }

            var threadHandle = NewKernelHandler.CreateRemoteThread(processHandle, (nuint)loadLibrary, remoteBuffer, 0, out _);
            if (threadHandle == 0)
            {
                throw new InvalidOperationException($"{libraryPath} could not be injected");
            }

            try
            {
                var waitResult = NewKernelHandler.WaitForSingleObject(threadHandle, StrictInfinite);
                if (waitResult == StrictWaitTimeout)
                {
                    throw new InvalidOperationException($"{libraryPath} could not be injected");
                }
            }
            finally
            {
                _ = NewKernelHandler.CloseHandle(threadHandle);
            }
        }
        finally
        {
            _ = NewKernelHandler.VirtualFreeEx(processHandle, remoteBuffer, 0, StrictMemRelease);
        }
    }

    private static void StrictWriteBytes(nint processHandle, nuint address, byte[] bytes)
    {
        var changedProtection = NewKernelHandler.VirtualProtectEx(processHandle, address, (nuint)bytes.Length, StrictPageExecuteReadWrite, out var oldProtect);
        try
        {
            if (!NewKernelHandler.WriteProcessMemory(processHandle, address, bytes, out var bytesWritten) || bytesWritten < (nuint)bytes.Length)
            {
                throw new InvalidOperationException("Not all instructions could be injected");
            }
        }
        finally
        {
            if (changedProtection)
            {
                _ = NewKernelHandler.VirtualProtectEx(processHandle, address, (nuint)bytes.Length, oldProtect, out _);
            }
        }
    }

    private static void StrictCleanupAfterFailure(bool targetSelf, List<CeAlloc> ceAllocArray, int originalAllocCount, AutoAssemblerStrictContext context)
    {
        var processHandle = StrictResolveTargetProcessHandle(targetSelf);
        for (var index = ceAllocArray.Count - 1; index >= originalAllocCount; index--)
        {
            _ = NewKernelHandler.VirtualFreeEx(processHandle, ceAllocArray[index].Address, 0, StrictMemRelease);
            ceAllocArray.RemoveAt(index);
        }

        lock (StrictGlobalAllocSync)
        {
            foreach (var allocationName in context.NewGlobalAllocations)
            {
                if (!GlobalAllocations.Remove(allocationName, out var address) || address == 0)
                {
                    continue;
                }

                _ = NewKernelHandler.VirtualFreeEx(processHandle, address, 0, StrictMemRelease);
                SymbolHandler.Default.DeleteUserDefinedSymbol(allocationName);
            }
        }
    }

    private static nuint StrictAobScan(string pattern, string? moduleName, bool targetSelf)
    {
        using var process = StrictResolveTargetProcess(targetSelf);
        var searchPattern = StrictParseBytePattern(pattern);
        foreach (ProcessModule module in process.Modules)
        {
            if (moduleName is not null && !string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = unchecked((nuint)module.BaseAddress.ToInt64());
            var buffer = new byte[module.ModuleMemorySize];
            if (!NewKernelHandler.ReadProcessMemory(StrictResolveTargetProcessHandle(targetSelf), start, buffer, out _))
            {
                continue;
            }

            for (var offset = 0; offset + searchPattern.Length <= buffer.Length; offset++)
            {
                var matched = true;
                for (var index = 0; index < searchPattern.Length; index++)
                {
                    if (searchPattern[index] >= 0 && buffer[offset + index] != searchPattern[index])
                    {
                        matched = false;
                        break;
                    }
                }

                if (matched)
                {
                    return start + (nuint)offset;
                }
            }
        }

        throw new InvalidOperationException($"The array of byte '{pattern}' could not be found");
    }

    private static Process StrictResolveTargetProcess(bool targetSelf)
    {
        return targetSelf || CeFuncProc.ProcessHandler.ProcessId == 0
            ? Process.GetCurrentProcess()
            : Process.GetProcessById((int)CeFuncProc.ProcessHandler.ProcessId);
    }

    private static nint StrictResolveTargetProcessHandle(bool targetSelf)
    {
        return targetSelf || CeFuncProc.ProcessHandler.ProcessHandle == 0
            ? Process.GetCurrentProcess().Handle
            : CeFuncProc.ProcessHandler.ProcessHandle;
    }

    private static bool StrictIsCurrentProcessTarget(bool targetSelf)
    {
        return targetSelf || CeFuncProc.ProcessHandler.ProcessId == 0 || CeFuncProc.ProcessHandler.ProcessId == (uint)Process.GetCurrentProcess().Id;
    }

    private static nuint StrictPlaceholderAddress(bool targetSelf)
    {
        return StrictResolveTargetProcess(targetSelf).Handle == nint.Zero
            ? 0x1000u
            : (nuint)(CeFuncProc.ProcessHandler.Is64Bit || Environment.Is64BitProcess ? 0x100000000UL : 0x1000U);
    }

    private static bool StrictTryGetLabelDefinition(string line, out string labelName)
    {
        labelName = string.Empty;
        if (!line.EndsWith(':'))
        {
            return false;
        }

        var candidate = line[..^1].Trim();
        if (!StrictIsSimpleIdentifier(candidate))
        {
            return false;
        }

        labelName = candidate;
        return true;
    }

    private static void StrictValidateIdentifier(string identifier)
    {
        if (!StrictIsSimpleIdentifier(identifier))
        {
            throw new InvalidOperationException($"{identifier} is not a valid identifier");
        }
    }

    private static bool StrictIsSimpleIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || StrictLooksLikeNumericIdentifier(value))
        {
            return false;
        }

        foreach (var character in value)
        {
            if (!StrictIsTokenCharacter(character))
            {
                return false;
            }
        }

        return true;
    }

    private static bool StrictLooksLikeNumericIdentifier(string value)
    {
        var normalized = value.Trim();
        if (normalized.StartsWith('$'))
        {
            normalized = normalized[1..];
        }
        else if (normalized.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            normalized = normalized[2..];
        }

        return normalized.Length > 0 && normalized.All(StrictIsHexDigit);
    }

    private static bool StrictTryResolveAddress(string expression, AutoAssemblerStrictContext context, out nuint value)
    {
        try
        {
            value = StrictParseAddressWithContext(expression, context);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static nuint StrictParseAddressWithContext(string expression, AutoAssemblerStrictContext context)
    {
        var prepared = StrictApplySymbols(StrictApplyDefines(expression, context), context);
        return AddressParserGlobal.GetAddress(prepared);
    }

    private static string StrictApplyDefines(string line, AutoAssemblerStrictContext context)
    {
        var result = line;
        foreach (var pair in context.Defines.OrderByDescending(item => item.Key.Length))
        {
            result = StrictReplaceToken(result, pair.Key, pair.Value);
        }

        return result;
    }

    private static string StrictApplySymbols(string line, AutoAssemblerStrictContext context)
    {
        var result = line;
        foreach (var pair in context.Symbols.OrderByDescending(item => item.Key.Length))
        {
            result = StrictReplaceToken(result, pair.Key, "$" + pair.Value.ToString("X", CultureInfo.InvariantCulture));
        }

        foreach (var pair in context.Labels.OrderByDescending(item => item.Key.Length))
        {
            result = StrictReplaceToken(result, pair.Key, "$" + pair.Value.ToString("X", CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static string StrictReplaceToken(string text, string token, string replacement)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(token))
        {
            return text;
        }

        var tokens = StrictTokenize(text);
        if (tokens.Count == 0)
        {
            return text;
        }

        var builder = new StringBuilder(text);
        var delta = 0;
        foreach (var tokenInfo in tokens)
        {
            if (!string.Equals(tokenInfo.Token, token, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            builder.Remove(tokenInfo.Start + delta, tokenInfo.Length);
            builder.Insert(tokenInfo.Start + delta, replacement);
            delta += replacement.Length - tokenInfo.Length;
        }

        return builder.ToString();
    }

    private static List<(string Token, int Start, int Length)> StrictTokenize(string input)
    {
        var result = new List<(string Token, int Start, int Length)>();
        var start = -1;
        for (var index = 0; index < input.Length; index++)
        {
            if (StrictIsTokenCharacter(input[index]))
            {
                if (start < 0)
                {
                    start = index;
                }

                continue;
            }

            if (start >= 0)
            {
                result.Add((input[start..index], start, index - start));
                start = -1;
            }
        }

        if (start >= 0)
        {
            result.Add((input[start..], start, input.Length - start));
        }

        return result;
    }

    private static bool StrictIsTokenCharacter(char value)
    {
        return char.IsAsciiLetterOrDigit(value) || value is '.' or '_' or '#' or '@';
    }

    private static bool StrictIsHexDigit(char value)
    {
        return value is >= '0' and <= '9'
            || value is >= 'A' and <= 'F'
            || value is >= 'a' and <= 'f';
    }

    private static bool StrictLabelsEqual(IReadOnlyDictionary<string, nuint> left, IReadOnlyDictionary<string, nuint> right)
    {
        if (left.Count != right.Count)
        {
            return false;
        }

        foreach (var pair in left)
        {
            if (!right.TryGetValue(pair.Key, out var value) || value != pair.Value)
            {
                return false;
            }
        }

        return true;
    }

    private static string StrictResolveFilePath(string path, string baseDirectory, string? defaultExtension)
    {
        var trimmed = StrictUnquote(path).Trim();
        if (!string.IsNullOrEmpty(defaultExtension) && Path.GetExtension(trimmed).Length == 0)
        {
            trimmed += defaultExtension;
        }

        var candidates = new List<string>();
        if (Path.IsPathRooted(trimmed))
        {
            candidates.Add(trimmed);
        }
        else
        {
            candidates.Add(Path.Combine(baseDirectory, trimmed));
            candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), trimmed));
            candidates.Add(trimmed);
        }

        foreach (var candidate in candidates)
        {
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new FileNotFoundException($"The file {trimmed} does not exist", trimmed);
    }

    private static string StrictStripComment(string line)
    {
        var inDoubleQuote = false;
        var inSingleQuote = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"' && !inSingleQuote)
            {
                inDoubleQuote = !inDoubleQuote;
            }
            else if (line[index] == '\'' && !inDoubleQuote)
            {
                inSingleQuote = !inSingleQuote;
            }

            if (!inDoubleQuote && !inSingleQuote && line[index] == '/' && line[index + 1] == '/')
            {
                return line[..index];
            }
        }

        return line;
    }

    private static string StrictUnquote(string value)
    {
        var trimmed = value.Trim();
        if ((trimmed.StartsWith('"') && trimmed.EndsWith('"')) || (trimmed.StartsWith('\'') && trimmed.EndsWith('\'')))
        {
            return trimmed[1..^1];
        }

        return trimmed;
    }

    private static int[] StrictParseBytePattern(string pattern)
    {
        var bytes = new List<int>();
        CeFuncProc.ConvertStringToBytes(pattern, true, bytes);
        return bytes.ToArray();
    }
}
