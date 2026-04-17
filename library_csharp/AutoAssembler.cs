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

internal sealed class AutoAssemblerContext
{
    public Dictionary<string, string> Defines { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, nuint> Symbols { get; } = new(StringComparer.OrdinalIgnoreCase);

    public Dictionary<string, nuint> Labels { get; } = new(StringComparer.OrdinalIgnoreCase);
}

public static partial class AutoAssembler
{
    private static readonly Dictionary<string, nuint> GlobalAllocations = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, nint> LoadedLibraries = new(StringComparer.OrdinalIgnoreCase);

    public static bool GetEnableAndDisablePos(IReadOnlyList<string> code, out int enablePos, out int disablePos)
    {
        enablePos = -1;
        disablePos = -1;
        for (var index = 0; index < code.Count; index++)
        {
            var line = code[index];
            var commentIndex = line.IndexOf("//", StringComparison.Ordinal);
            if (commentIndex >= 0)
            {
                line = line[..commentIndex];
            }

            var trimmed = line.Trim();
            if (string.Equals(trimmed, "[ENABLE]", StringComparison.OrdinalIgnoreCase))
            {
                enablePos = index;
            }
            else if (string.Equals(trimmed, "[DISABLE]", StringComparison.OrdinalIgnoreCase))
            {
                disablePos = index;
            }
        }

        return enablePos >= 0 || disablePos >= 0;
    }

    public static bool AutoAssemble(string code, bool enable = true, List<CeAlloc>? ceAllocArray = null, List<string>? registeredSymbols = null, bool popupMessages = false, bool syntaxCheckOnly = false, bool targetSelf = false)
    {
        return AutoAssemble(SplitLines(code), popupMessages, enable, syntaxCheckOnly, targetSelf, ceAllocArray ?? new List<CeAlloc>(), registeredSymbols ?? new List<string>());
    }

    public static bool AutoAssemble(IReadOnlyList<string> code, bool popupMessages)
    {
        return AutoAssemble(code, popupMessages, true, false, false, new List<CeAlloc>(), new List<string>());
    }

    public static bool AutoAssemble(IReadOnlyList<string> code, bool popupMessages, bool enable, bool syntaxCheckOnly, bool targetSelf)
    {
        return AutoAssemble(code, popupMessages, enable, syntaxCheckOnly, targetSelf, new List<CeAlloc>(), new List<string>());
    }

    public static bool AutoAssemble(IReadOnlyList<string> code, bool popupMessages, bool enable, bool syntaxCheckOnly, bool targetSelf, List<CeAlloc> ceAllocArray, List<string>? registeredSymbols = null)
    {
        return AutoAssembleStrict(code, popupMessages, enable, syntaxCheckOnly, targetSelf, ceAllocArray, registeredSymbols);
    }

    private static List<string> ExpandIncludes(IReadOnlyList<string> lines)
    {
        var result = new List<string>(lines.Count);
        foreach (var rawLine in lines)
        {
            var line = StripComment(rawLine).Trim();
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseDirective(line, "include", out var includeArguments))
            {
                var path = Unquote(includeArguments[0]);
                result.AddRange(ExpandIncludes(File.ReadAllLines(path)));
                continue;
            }

            result.Add(rawLine);
        }

        return result;
    }

    private static List<string> SelectSection(IReadOnlyList<string> lines, bool enable)
    {
        if (!GetEnableAndDisablePos(lines, out var enablePos, out var disablePos))
        {
            return lines.Select(StripComment).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
        }

        var start = enable ? enablePos + 1 : disablePos + 1;
        var end = enable ? (disablePos >= 0 ? disablePos : lines.Count) : lines.Count;
        return lines.Skip(start).Take(end - start).Select(StripComment).Where(line => !string.IsNullOrWhiteSpace(line)).ToList();
    }

    private static void PreprocessDirectives(IReadOnlyList<string> lines, bool enable, List<CeAlloc> ceAllocArray, AutoAssemblerContext context)
    {
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (TryParseDirective(line, "define", out var defineArguments))
            {
                context.Defines[defineArguments[0]] = defineArguments[1];
                continue;
            }

            if (TryParseDirective(line, "aobscanmodule", out var aobModuleArguments))
            {
                context.Symbols[aobModuleArguments[0]] = AobScan(aobModuleArguments[2], aobModuleArguments[1]);
                continue;
            }

            if (TryParseDirective(line, "aobscan", out var aobArguments))
            {
                context.Symbols[aobArguments[0]] = AobScan(aobArguments[1], null);
                continue;
            }

            if (TryParseDirective(line, "alloc", out var allocArguments)
                || TryParseDirective(line, "kalloc", out allocArguments))
            {
                if (!enable)
                {
                    if (!context.Symbols.ContainsKey(allocArguments[0]))
                    {
                        var existing = ceAllocArray.LastOrDefault(alloc => string.Equals(alloc.VarName, allocArguments[0], StringComparison.OrdinalIgnoreCase));
                        if (existing is not null && !string.IsNullOrEmpty(existing.VarName))
                        {
                            context.Symbols[existing.VarName!] = existing.Address;
                        }
                    }

                    continue;
                }

                var symbolName = allocArguments[0];
                var size = checked((uint)ParseAddressWithContext(ApplyDefines(allocArguments[1], context), context));
                var preferred = allocArguments.Count >= 3 ? ParseAddressWithContext(ApplyDefines(allocArguments[2], context), context) : 0;
                var memory = unchecked((nuint)Marshal.AllocHGlobal((int)size));
                ceAllocArray.Add(new CeAlloc(memory, symbolName, size, preferred));
                context.Symbols[symbolName] = memory;
                continue;
            }

            if (TryParseDirective(line, "sharedalloc", out var sharedArguments)
                || TryParseDirective(line, "globalalloc", out sharedArguments))
            {
                var symbolName = sharedArguments[0];
                if (!GlobalAllocations.TryGetValue(symbolName, out var address))
                {
                    var size = checked((uint)ParseAddressWithContext(ApplyDefines(sharedArguments[1], context), context));
                    address = unchecked((nuint)Marshal.AllocHGlobal((int)size));
                    GlobalAllocations[symbolName] = address;
                }

                context.Symbols[symbolName] = address;
            }
        }
    }

    private static void ResolveLabels(IReadOnlyList<string> lines, AutoAssemblerContext context)
    {
        nuint currentAddress = 0;
        foreach (var rawLine in lines)
        {
            var line = ApplyDefines(rawLine.Trim(), context);
            if (IsDirective(line))
            {
                continue;
            }

            if (line.EndsWith(':'))
            {
                var labelName = line[..^1].Trim();
                if (context.Symbols.TryGetValue(labelName, out var symbolAddress))
                {
                    currentAddress = symbolAddress;
                }
                else if (TryResolveAddress(labelName, context, out var resolved))
                {
                    currentAddress = resolved;
                }
                else
                {
                    context.Labels[labelName] = currentAddress;
                }

                continue;
            }

            if (currentAddress == 0)
            {
                continue;
            }

            var prepared = ApplySymbols(line, context);
            currentAddress += (nuint)AssemblerUnit.GetEstimatedSize(prepared, AssemblerPreference.X64);
        }
    }

    private static void ExecuteLines(IReadOnlyList<string> lines, bool enable, bool syntaxCheckOnly, List<CeAlloc> ceAllocArray, List<string> registeredSymbols, AutoAssemblerContext context)
    {
        nuint currentAddress = 0;
        foreach (var rawLine in lines)
        {
            var line = ApplyDefines(rawLine.Trim(), context);
            if (line.Length == 0)
            {
                continue;
            }

            if (TryParseDirective(line, "registersymbol", out var registerArguments))
            {
                var symbolName = registerArguments[0];
                var address = ParseAddressWithContext(symbolName, context);
                if (enable)
                {
                    SymbolHandler.Default.AddUserDefinedSymbol(address.ToString(CultureInfo.InvariantCulture), symbolName, doNotSave: true);
                    if (!registeredSymbols.Contains(symbolName, StringComparer.OrdinalIgnoreCase))
                    {
                        registeredSymbols.Add(symbolName);
                    }
                }

                continue;
            }

            if (TryParseDirective(line, "unregistersymbol", out var unregisterArguments))
            {
                var symbolName = unregisterArguments[0];
                SymbolHandler.Default.DeleteUserDefinedSymbol(symbolName);
                registeredSymbols.RemoveAll(item => string.Equals(item, symbolName, StringComparison.OrdinalIgnoreCase));
                continue;
            }

            if (TryParseDirective(line, "dealloc", out var deallocArguments))
            {
                if (enable)
                {
                    continue;
                }

                var symbolName = deallocArguments[0];
                var index = ceAllocArray.FindLastIndex(allocation => string.Equals(allocation.VarName, symbolName, StringComparison.OrdinalIgnoreCase));
                if (index >= 0)
                {
                    Marshal.FreeHGlobal((nint)ceAllocArray[index].Address);
                    ceAllocArray.RemoveAt(index);
                }

                context.Symbols.Remove(symbolName);
                SymbolHandler.Default.DeleteUserDefinedSymbol(symbolName);
                continue;
            }

            if (TryParseDirective(line, "loadlibrary", out var loadLibraryArguments))
            {
                if (enable)
                {
                    var path = Unquote(loadLibraryArguments[0]);
                    if (!LoadedLibraries.ContainsKey(path))
                    {
                        LoadedLibraries[path] = NativeLibrary.Load(path);
                    }
                }

                continue;
            }

            if (TryParseDirective(line, "label", out _)
                || TryParseDirective(line, "alloc", out _)
                || TryParseDirective(line, "kalloc", out _)
                || TryParseDirective(line, "globalalloc", out _)
                || TryParseDirective(line, "sharedalloc", out _)
                || TryParseDirective(line, "define", out _)
                || TryParseDirective(line, "aobscan", out _)
                || TryParseDirective(line, "aobscanmodule", out _))
            {
                continue;
            }

            if (TryParseDirective(line, "readmem", out var readMemArguments))
            {
                if (currentAddress == 0)
                {
                    continue;
                }

                var sourceAddress = ParseAddressWithContext(readMemArguments[0], context);
                var size = checked((int)ParseAddressWithContext(readMemArguments[1], context));
                var buffer = new byte[size];
                if (!NewKernelHandler.ReadProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, sourceAddress, buffer, out _))
                {
                    throw new InvalidOperationException("READMEM failed");
                }

                if (!syntaxCheckOnly)
                {
                    NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, currentAddress, buffer, out _);
                }

                currentAddress += (nuint)size;
                continue;
            }

            if (line.EndsWith(':'))
            {
                var labelName = line[..^1].Trim();
                if (context.Symbols.TryGetValue(labelName, out var symbolAddress))
                {
                    currentAddress = symbolAddress;
                }
                else if (context.Labels.TryGetValue(labelName, out var labelAddress))
                {
                    currentAddress = labelAddress;
                }
                else
                {
                    currentAddress = ParseAddressWithContext(labelName, context);
                }

                continue;
            }

            if (currentAddress == 0)
            {
                continue;
            }

            var assembledLine = ApplySymbols(line, context);
            if (!AssemblerUnit.Assemble(assembledLine, currentAddress, out var bytes, AssemblerPreference.X64, skipRangeCheck: true))
            {
                throw new InvalidOperationException($"Failed to assemble line: {assembledLine}");
            }

            if (!syntaxCheckOnly)
            {
                NewKernelHandler.WriteProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, currentAddress, bytes, out _);
            }

            currentAddress += (nuint)bytes.Length;
        }
    }

    private static nuint AobScan(string pattern, string? moduleName)
    {
        var process = CeFuncProc.ProcessHandler.ProcessId != 0 ? Process.GetProcessById((int)CeFuncProc.ProcessHandler.ProcessId) : Process.GetCurrentProcess();
        var searchPattern = ParseBytePattern(pattern);
        foreach (ProcessModule module in process.Modules)
        {
            if (moduleName is not null && !string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var start = unchecked((nuint)module.BaseAddress.ToInt64());
            var buffer = new byte[module.ModuleMemorySize];
            if (!NewKernelHandler.ReadProcessMemory(CeFuncProc.ProcessHandler.ProcessHandle, start, buffer, out _))
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

        throw new InvalidOperationException("AOB pattern not found");
    }

    private static bool TryParseDirective(string line, string directive, out List<string> arguments)
    {
        arguments = new List<string>();
        if (!line.StartsWith(directive + "(", StringComparison.OrdinalIgnoreCase) || !line.EndsWith(')'))
        {
            return false;
        }

        arguments = SplitArguments(line[(directive.Length + 1)..^1]);
        return true;
    }

    private static bool IsDirective(string line)
    {
        return Regex.IsMatch(line, "^[A-Za-z]+\\(.*\\)$");
    }

    private static string ApplyDefines(string line, AutoAssemblerContext context)
    {
        var result = line;
        foreach (var pair in context.Defines.OrderByDescending(item => item.Key.Length))
        {
            result = ReplaceToken(result, pair.Key, pair.Value);
        }

        return result;
    }

    private static string ApplySymbols(string line, AutoAssemblerContext context)
    {
        var result = line;
        foreach (var pair in context.Symbols.OrderByDescending(item => item.Key.Length))
        {
            result = ReplaceToken(result, pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
        }

        foreach (var pair in context.Labels.OrderByDescending(item => item.Key.Length))
        {
            result = ReplaceToken(result, pair.Key, pair.Value.ToString(CultureInfo.InvariantCulture));
        }

        return result;
    }

    private static string ReplaceToken(string text, string token, string replacement)
    {
        return Regex.Replace(text, $@"\b{Regex.Escape(token)}\b", replacement, RegexOptions.IgnoreCase);
    }

    private static bool TryResolveAddress(string expression, AutoAssemblerContext context, out nuint value)
    {
        try
        {
            value = ParseAddressWithContext(expression, context);
            return true;
        }
        catch
        {
            value = 0;
            return false;
        }
    }

    private static nuint ParseAddressWithContext(string expression, AutoAssemblerContext context)
    {
        var prepared = ApplySymbols(ApplyDefines(expression, context), context);
        return AddressParserGlobal.GetAddress(prepared);
    }

    private static List<string> SplitArguments(string arguments)
    {
        var result = new List<string>();
        var builder = new StringBuilder();
        var depth = 0;
        var inString = false;
        foreach (var character in arguments)
        {
            if (character == '"')
            {
                inString = !inString;
            }

            if (!inString)
            {
                if (character == '(')
                {
                    depth++;
                }
                else if (character == ')')
                {
                    depth--;
                }
                else if (character == ',' && depth == 0)
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

    private static string StripComment(string line)
    {
        var inString = false;
        for (var index = 0; index < line.Length - 1; index++)
        {
            if (line[index] == '"')
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

    private static string Unquote(string value)
    {
        var trimmed = value.Trim();
        return trimmed.StartsWith('"') && trimmed.EndsWith('"') ? trimmed[1..^1] : trimmed;
    }

    private static int[] ParseBytePattern(string pattern)
    {
        var bytes = new List<int>();
        CeFuncProc.ConvertStringToBytes(pattern, true, bytes);
        return bytes.ToArray();
    }

    private static IReadOnlyList<string> SplitLines(string code)
    {
        return code.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n', StringSplitOptions.None);
    }
}