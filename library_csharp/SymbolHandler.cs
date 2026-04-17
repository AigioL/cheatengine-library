using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;

namespace CheatEngine.Library;

public sealed class SymException : Exception
{
    public SymException(string message) : base(message)
    {
    }
}

public sealed class UserDefinedSymbol
{
    public string SymbolName { get; set; } = string.Empty;

    public nuint Address { get; set; }

    public string AddressString { get; set; } = string.Empty;

    public uint AllocSize { get; set; }

    public uint ProcessId { get; set; }

    public bool DoNotSave { get; set; }
}

public sealed class ModuleInfo
{
    public string ModuleName { get; set; } = string.Empty;

    public string ModulePath { get; set; } = string.Empty;

    public bool IsSystemModule { get; set; }

    public nuint BaseAddress { get; set; }

    public uint BaseSize { get; set; }

    public bool Is64BitModule { get; set; }

    public bool SymbolsLoaded { get; set; }
}

public sealed class SymbolHandler
{
    private readonly ReaderWriterLockSlim _lock = new();
    private readonly object _reinitializeSync = new();
    private readonly List<ModuleInfo> _moduleList = new();
    private readonly List<UserDefinedSymbol> _userDefinedSymbols = new();
    private readonly List<Action> _symbolsLoadedNotifications = new();
    private readonly SymbolListHandler _symbolList = new();
    private readonly AddressParser _addressParser = new();
    private readonly ManualResetEventSlim _symbolsLoadedEvent = new(initialState: true);
    private nuint _globalAllocCursor;
    private int _globalAllocSizeLeft;
    private uint _globalAllocProcessId;
    private bool _showModules = true;
    private bool _showSymbols = true;
    private bool _locked;
    private uint _loadedProcessId;
    private volatile bool _isLoaded = true;
    private volatile bool _hasError;
    private string _searchPath = string.Empty;

    public static SymbolHandler Default { get; } = new();

    public SymbolHandler()
    {
        ExceptionOnLuaLookup = true;
    }

    public bool KernelSymbols { get; set; }

    public bool DllSymbols { get; set; }

    public bool TargetSelf { get; set; }

    public bool ExceptionOnLuaLookup { get; set; }

    public bool ShowModules
    {
        get => _showModules;
        set
        {
            if (_locked)
            {
                throw new SymException("You can't change this setting at the moment");
            }

            _showModules = value;
        }
    }

    public bool ShowSymbols
    {
        get => _showSymbols;
        set
        {
            if (_locked)
            {
                throw new SymException("You can't change this setting at the moment");
            }

            _showSymbols = value;
        }
    }

    public nint UsedProcessHandle => CeFuncProc.ProcessHandler.ProcessHandle;

    public uint UsedProcessId => CeFuncProc.ProcessHandler.ProcessId;

    public bool IsLoaded => _isLoaded;

    public bool HasError => _hasError;

    public void WaitForSymbolsLoaded(bool apiSymbolsOnly = false, string specificModule = "")
    {
        _symbolsLoadedEvent.Wait();

        if (!apiSymbolsOnly && DllSymbols && !string.IsNullOrWhiteSpace(specificModule) && !AreSymbolsLoadedForModule(specificModule))
        {
            Reinitialize(force: false);
            _symbolsLoadedEvent.Wait();
        }
    }

    public void Reinitialize(bool force = false)
    {
        lock (_reinitializeSync)
        {
            var refreshStarted = false;
            var previousLocked = _locked;
            _locked = true;

            try
            {
                var refreshRequired = force || LoadModuleList();
                if (!refreshRequired)
                {
                    ReinitializeUserDefinedSymbolList();
                    _isLoaded = true;
                    _hasError = false;
                    return;
                }

                refreshStarted = true;
                _symbolsLoadedEvent.Reset();
                _isLoaded = false;
                _hasError = false;

                _symbolList.Clear();
                RebuildModuleSymbols();
                if (DllSymbols)
                {
                    LoadDllSymbols();
                }

                ReinitializeUserDefinedSymbolList();
                _isLoaded = true;
            }
            catch
            {
                _hasError = true;
                throw;
            }
            finally
            {
                _locked = previousLocked;

                if (refreshStarted)
                {
                    _symbolsLoadedEvent.Set();
                    NotifyFinishedLoadingSymbols();
                }
            }
        }
    }

    public bool LoadModuleList()
    {
        using var process = ResolveSymbolTargetProcess();
        var targetProcessIs64Bit = TargetSelf || CeFuncProc.ProcessHandler.ProcessId == 0
            ? Environment.Is64BitProcess
            : CeFuncProc.ProcessHandler.Is64Bit;
        var systemDirectory = Environment.SystemDirectory;
        var seenBaseAddresses = new HashSet<nuint>();
        var modules = new List<ModuleInfo>();
        foreach (ProcessModule module in process.Modules)
        {
            var baseAddress = unchecked((nuint)module.BaseAddress.ToInt64());
            if (!seenBaseAddresses.Add(baseAddress))
            {
                continue;
            }

            var path = module.FileName ?? string.Empty;
            modules.Add(new ModuleInfo
            {
                ModuleName = module.ModuleName,
                ModulePath = path,
                BaseAddress = baseAddress,
                BaseSize = unchecked((uint)module.ModuleMemorySize),
                Is64BitModule = targetProcessIs64Bit,
                IsSystemModule = path.StartsWith(systemDirectory, StringComparison.OrdinalIgnoreCase)
                    && !string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase),
                SymbolsLoaded = !DllSymbols,
            });
        }

        modules.Sort(static (left, right) => left.BaseAddress.CompareTo(right.BaseAddress));

        _lock.EnterWriteLock();
        try
        {
            var targetProcessId = ResolveCurrentSymbolProcessId();
            var changed = targetProcessId != _loadedProcessId
                || modules.Count != _moduleList.Count
                || modules.Zip(_moduleList, static (left, right) =>
                    left.BaseAddress != right.BaseAddress
                    || left.BaseSize != right.BaseSize
                    || !string.Equals(left.ModuleName, right.ModuleName, StringComparison.OrdinalIgnoreCase)
                    || !string.Equals(left.ModulePath, right.ModulePath, StringComparison.OrdinalIgnoreCase)).Any(static value => value);
            _moduleList.Clear();
            _moduleList.AddRange(modules);
            _loadedProcessId = targetProcessId;
            return changed;
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    private void RebuildModuleSymbols()
    {
        List<ModuleInfo> modules;
        _lock.EnterReadLock();
        try
        {
            modules = _moduleList.Select(CloneModuleInfo).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var module in modules)
        {
            var size = module.BaseSize > int.MaxValue ? int.MaxValue : (int)module.BaseSize;
            _symbolList.AddSymbol(module.ModuleName, module.ModuleName, (ulong)module.BaseAddress, size);
        }
    }

    private void LoadDllSymbols()
    {
        List<ModuleInfo> modules;
        _lock.EnterReadLock();
        try
        {
            modules = _moduleList.Select(module => new ModuleInfo
            {
                ModuleName = module.ModuleName,
                ModulePath = module.ModulePath,
                IsSystemModule = module.IsSystemModule,
                BaseAddress = module.BaseAddress,
                BaseSize = module.BaseSize,
                Is64BitModule = module.Is64BitModule,
                SymbolsLoaded = module.SymbolsLoaded,
            }).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var module in modules)
        {
            if (string.IsNullOrWhiteSpace(module.ModulePath) || !File.Exists(module.ModulePath))
            {
                MarkModuleAsLoaded(module.ModuleName);
                continue;
            }

            try
            {
                var imageInfo = PeInfoFunctions.ReadImageInfo(module.ModulePath);
                foreach (var exportEntry in imageInfo.Exports)
                {
                    var relativeAddress = exportEntry.Address >= imageInfo.ImageBase
                        ? exportEntry.Address - imageInfo.ImageBase
                        : exportEntry.Address;
                    var runtimeAddress = unchecked((ulong)module.BaseAddress + relativeAddress);
                    _symbolList.AddSymbol(module.ModuleName, module.ModuleName + "." + exportEntry.Name, runtimeAddress, 0);
                    _symbolList.AddSymbol(module.ModuleName, exportEntry.Name, runtimeAddress, 0, skipAddressToStringLookup: true);
                }
            }
            catch (IOException)
            {
            }
            catch (InvalidDataException)
            {
            }

            MarkModuleAsLoaded(module.ModuleName);
        }
    }

    private void MarkModuleAsLoaded(string moduleName)
    {
        _lock.EnterWriteLock();
        try
        {
            var module = _moduleList.FirstOrDefault(item => string.Equals(item.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase));
            if (module is not null)
            {
                module.SymbolsLoaded = true;
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void ReinitializeUserDefinedSymbolList()
    {
        _lock.EnterWriteLock();
        try
        {
            foreach (var symbol in _userDefinedSymbols)
            {
                if (nuint.TryParse(symbol.AddressString, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var parsed))
                {
                    symbol.Address = parsed;
                    continue;
                }

                var value = GetAddressFromName(symbol.AddressString, false, out var hasError);
                if (!hasError)
                {
                    symbol.Address = value;
                }
            }
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void FillMemoryRegionsWithModuleData(List<MemoryRegion> regions, nuint startAddress, uint size)
    {
        _lock.EnterReadLock();
        try
        {
            var currentAddress = startAddress;
            ulong sizeLeft = size;
            while (sizeLeft > 0)
            {
                ModuleInfo? currentModule = null;
                foreach (var module in _moduleList)
                {
                    if (currentAddress >= module.BaseAddress && currentAddress < module.BaseAddress + module.BaseSize)
                    {
                        currentModule = module;
                        break;
                    }
                }

                if (currentModule is not null)
                {
                    var chunkSize = Math.Min(sizeLeft, (ulong)currentModule.BaseSize - (currentAddress - currentModule.BaseAddress));
                    regions.Add(new MemoryRegion(currentAddress, chunkSize, false, 0));
                    currentAddress += (nuint)chunkSize;
                    sizeLeft -= chunkSize;
                }
                else
                {
                    ModuleInfo? closestModule = null;
                    foreach (var module in _moduleList)
                    {
                        if (module.BaseAddress <= currentAddress)
                        {
                            continue;
                        }

                        if (closestModule is null || module.BaseAddress < closestModule.BaseAddress)
                        {
                            closestModule = module;
                        }
                    }

                    if (closestModule is null)
                    {
                        break;
                    }

                    sizeLeft += (ulong)(closestModule.BaseAddress - currentAddress);
                    currentAddress = closestModule.BaseAddress;
                }
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void GetModuleList(ICollection<string> list)
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var module in _moduleList)
            {
                list.Add(module.ModuleName);
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void GetSymbolList(nuint address, ICollection<string> list)
    {
        var symbol = _symbolList.FindAddress(address);
        if (symbol is not null)
        {
            list.Add(symbol.OriginalString);
        }
    }

    public bool GetModuleByAddress(nuint address, out ModuleInfo moduleInfo)
    {
        _lock.EnterReadLock();
        try
        {
            moduleInfo = _moduleList.FirstOrDefault(module => address >= module.BaseAddress && address < module.BaseAddress + module.BaseSize) ?? new ModuleInfo();
            return moduleInfo.BaseAddress != 0 || _moduleList.Any(module => module.BaseAddress == 0 && address < module.BaseSize);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool GetModuleByName(string moduleName, out ModuleInfo moduleInfo)
    {
        _lock.EnterReadLock();
        try
        {
            moduleInfo = _moduleList.FirstOrDefault(module => string.Equals(module.ModuleName, moduleName, StringComparison.OrdinalIgnoreCase)) ?? new ModuleInfo();
            return !string.IsNullOrEmpty(moduleInfo.ModuleName);
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool InModule(nuint address)
    {
        return GetModuleByAddress(address, out _);
    }

    public bool InSystemModule(nuint address)
    {
        return GetModuleByAddress(address, out var module) && module.IsSystemModule;
    }

    public string GetNameFromAddress(nuint address)
    {
        return GetNameFromAddress(address, out _);
    }

    public string GetNameFromAddress(nuint address, out bool found, int hexCharSize = 8)
    {
        return GetNameFromAddress(address, true, true, out found, null, hexCharSize);
    }

    public string GetNameFromAddress(nuint address, bool symbols, bool modules, out bool found, ulong? baseAddress = null, int hexCharSize = 8)
    {
        return GetNameFromAddressCore(address, symbols, modules, out found, hexCharSize, allowRefresh: true);
    }

    private string GetNameFromAddressCore(nuint address, bool symbols, bool modules, out bool found, int hexCharSize, bool allowRefresh)
    {
        found = false;
        var userDefinedName = GetUserDefinedSymbolByAddress(address);
        if (!string.IsNullOrEmpty(userDefinedName))
        {
            found = true;
            return userDefinedName;
        }

        if (symbols)
        {
            var symbol = _symbolList.FindAddress(address);
            if (symbol is not null)
            {
                found = true;
                var offset = (ulong)address - symbol.Address;
                return offset == 0 ? symbol.OriginalString : $"{symbol.OriginalString}+{offset:X}";
            }
        }

        if (modules && GetModuleByAddress(address, out var module))
        {
            found = true;
            var offset = address - module.BaseAddress;
            return offset == 0 ? module.ModuleName : $"{module.ModuleName}+{offset:X}";
        }

        if (allowRefresh && TryRefreshSymbolsAfterLookupMiss())
        {
            return GetNameFromAddressCore(address, symbols, modules, out found, hexCharSize, allowRefresh: false);
        }

        return address.ToString($"X{hexCharSize}", CultureInfo.InvariantCulture);
    }

    public ExtraSymbolData? GetExtraDataFromSymbolAtAddress(nuint address)
    {
        return _symbolList.FindAddress(address)?.Extra;
    }

    public nuint GetAddressFromNameL(string name)
    {
        try
        {
            return GetAddressFromName(name);
        }
        catch when (!ExceptionOnLuaLookup)
        {
            return 0;
        }
    }

    public nuint GetAddressFromName(string name)
    {
        return GetAddressFromName(name, true);
    }

    public nuint GetAddressFromName(string name, bool waitForSymbols)
    {
        var address = GetAddressFromName(name, waitForSymbols, out var hasError);
        if (hasError)
        {
            throw new SymException($"Failure determining what {name} means");
        }

        return address;
    }

    public nuint GetAddressFromName(string name, bool waitForSymbols, out bool hasError)
    {
        return GetAddressFromName(name, waitForSymbols, out hasError, null);
    }

    public nuint GetAddressFromName(string name, bool waitForSymbols, out bool hasError, CpuContext? context)
    {
        return GetAddressFromNameCore(name, waitForSymbols, out hasError, context, allowRefresh: true);
    }

    private nuint GetAddressFromNameCore(string name, bool waitForSymbols, out bool hasError, CpuContext? context, bool allowRefresh)
    {
        hasError = false;
        if (string.IsNullOrWhiteSpace(name))
        {
            hasError = true;
            return 0;
        }

        if (waitForSymbols)
        {
            WaitForSymbolsLoaded();
        }

        if (TryResolveAddressExpression(name.Trim(), context, out var result))
        {
            return result;
        }

        if (allowRefresh && TryRefreshSymbolsAfterLookupMiss())
        {
            return GetAddressFromNameCore(name, waitForSymbols, out hasError, context, allowRefresh: false);
        }

        hasError = true;
        return 0;
    }

    public string GetSearchPath()
    {
        return _searchPath;
    }

    public void SetSearchPath(string path)
    {
        _searchPath = path ?? string.Empty;
    }

    public bool DeleteUserDefinedSymbol(string symbolName)
    {
        var removed = false;
        _lock.EnterWriteLock();
        try
        {
            removed = _userDefinedSymbols.RemoveAll(symbol => string.Equals(symbol.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase)) > 0;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (removed)
        {
            NotifyFinishedLoadingSymbols();
        }

        return removed;
    }

    public nuint GetUserDefinedSymbolByName(string symbolName)
    {
        _lock.EnterReadLock();
        try
        {
            return _userDefinedSymbols.FirstOrDefault(symbol => string.Equals(symbol.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase))?.Address ?? 0;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool SetUserDefinedSymbolAllocSize(string symbolName, uint size)
    {
        if (size == 0)
        {
            throw new ArgumentException("Please provide a bigger size", nameof(size));
        }

        var shouldNotify = false;
        _lock.EnterWriteLock();
        try
        {
            var symbol = _userDefinedSymbols.FirstOrDefault(item => string.Equals(item.SymbolName, symbolName, StringComparison.OrdinalIgnoreCase));
            if (symbol is null)
            {
                var address = AllocateUserDefinedSymbolAddress(size);
                symbol = new UserDefinedSymbol
                {
                    SymbolName = symbolName,
                    Address = address,
                    AddressString = address.ToString("X", CultureInfo.InvariantCulture),
                    AllocSize = size,
                    ProcessId = ResolveUserDefinedSymbolProcessId(),
                };
                _userDefinedSymbols.Add(symbol);
            }
            else
            {
                if (symbol.AllocSize > 0 && symbol.ProcessId == ResolveUserDefinedSymbolProcessId())
                {
                    if (symbol.AllocSize != size)
                    {
                        throw new SymException($"The symbol named {symbol.SymbolName} was previously declared with size {symbol.AllocSize} and is now requested with size {size}");
                    }

                    return true;
                }

                var address = AllocateUserDefinedSymbolAddress(size);
                symbol.Address = address;
                symbol.AddressString = address.ToString("X", CultureInfo.InvariantCulture);
                symbol.AllocSize = size;
                symbol.ProcessId = ResolveUserDefinedSymbolProcessId();
            }

            shouldNotify = true;
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        if (shouldNotify)
        {
            NotifyFinishedLoadingSymbols();
        }

        return true;
    }

    public string GetUserDefinedSymbolByAddress(nuint address)
    {
        _lock.EnterReadLock();
        try
        {
            return _userDefinedSymbols.FirstOrDefault(symbol => symbol.Address == address)?.SymbolName ?? string.Empty;
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void AddUserDefinedSymbol(string addressString, string symbolName, bool doNotSave = false)
    {
        if (GetUserDefinedSymbolByName(symbolName) > 0)
        {
            throw new SymException(symbolName + " already exists");
        }

        var address = GetAddressFromName(addressString, false, out var hasError);
        if (address == 0 || hasError)
        {
            throw new SymException("You can't add a symbol with address 0");
        }

        _lock.EnterWriteLock();
        try
        {
            _userDefinedSymbols.Add(new UserDefinedSymbol
            {
                Address = address,
                AddressString = addressString,
                SymbolName = symbolName,
                DoNotSave = doNotSave,
                ProcessId = ResolveUserDefinedSymbolProcessId(),
            });
        }
        finally
        {
            _lock.ExitWriteLock();
        }

        NotifyFinishedLoadingSymbols();
    }

    private nuint AllocateUserDefinedSymbolAddress(uint size)
    {
        var alignedSize = AlignUserDefinedAllocationSize(size);
        var processId = ResolveUserDefinedSymbolProcessId();
        var processHandle = ResolveUserDefinedSymbolProcessHandle();
        if (_globalAllocProcessId != processId || _globalAllocCursor == 0 || _globalAllocSizeLeft < alignedSize)
        {
            var blockSize = (nuint)Math.Max(65536, alignedSize);
            var blockAddress = NewKernelHandler.VirtualAllocEx(processHandle, 0, blockSize, 0x3000, 0x40);
            if (blockAddress == 0)
            {
                throw new SymException("Error allocating memory");
            }

            _globalAllocProcessId = processId;
            _globalAllocCursor = blockAddress;
            _globalAllocSizeLeft = (int)blockSize;
        }

        var result = _globalAllocCursor;
        _globalAllocCursor += (nuint)alignedSize;
        _globalAllocSizeLeft -= alignedSize;
        return result;
    }

    private static int AlignUserDefinedAllocationSize(uint size)
    {
        return checked((int)((size + 15U) & ~15U));
    }

    private uint ResolveUserDefinedSymbolProcessId()
    {
        return TargetSelf || CeFuncProc.ProcessHandler.ProcessId == 0
            ? (uint)Process.GetCurrentProcess().Id
            : CeFuncProc.ProcessHandler.ProcessId;
    }

    private nint ResolveUserDefinedSymbolProcessHandle()
    {
        return TargetSelf || CeFuncProc.ProcessHandler.ProcessHandle == 0
            ? Process.GetCurrentProcess().Handle
            : CeFuncProc.ProcessHandler.ProcessHandle;
    }

    public void EnumerateUserDefinedSymbols(ICollection<UserDefinedSymbol> list)
    {
        _lock.EnterReadLock();
        try
        {
            foreach (var symbol in _userDefinedSymbols)
            {
                list.Add(new UserDefinedSymbol
                {
                    SymbolName = symbol.SymbolName,
                    Address = symbol.Address,
                    AddressString = symbol.AddressString,
                    AllocSize = symbol.AllocSize,
                    ProcessId = symbol.ProcessId,
                    DoNotSave = symbol.DoNotSave,
                });
            }
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public bool ParseAsPointer(string text, ICollection<string> tokens)
    {
        var currentLevel = 0;
        var prolog = true;
        var current = string.Empty;
        var isPointer = false;

        foreach (var character in text)
        {
            if (character == '[')
            {
                if (prolog)
                {
                    currentLevel++;
                    isPointer = true;
                }
                else
                {
                    return false;
                }

                continue;
            }

            if (prolog && character is not (' ' or '\t' or '\b'))
            {
                prolog = false;
            }

            if (prolog)
            {
                continue;
            }

            if (character == ']')
            {
                currentLevel--;
                tokens.Add(current.Length == 0 ? "+0" : current);
                current = string.Empty;
                if (currentLevel < 0)
                {
                    return false;
                }

                continue;
            }

            current += character;
        }

        if (current.Length == 0)
        {
            current = "+0";
        }

        if (isPointer && current.Length > 0)
        {
            tokens.Add(current);
        }

        return isPointer && currentLevel == 0;
    }

    public nuint GetAddressFromPointer(string text, out bool error)
    {
        error = true;
        var tokens = new List<string>();
        if (!ParseAsPointer(text, tokens) || tokens.Count == 0)
        {
            return 0;
        }

        var baseAddress = GetAddressFromName(tokens[0], true, out var hasBaseError);
        if (hasBaseError)
        {
            return 0;
        }

        var currentAddress = baseAddress;
        var processHandle = ResolveUserDefinedSymbolProcessHandle();
        var pointerSize = ResolvePointerSize();
        var buffer = new byte[Math.Max(pointerSize, sizeof(ulong))];

        for (var index = 1; index < tokens.Count; index++)
        {
            if (!TryParsePointerOffset(tokens[index], out var offset, out var subtract))
            {
                return 0;
            }

            if (!NewKernelHandler.ReadProcessMemory(processHandle, currentAddress, buffer.AsSpan(0, pointerSize), out var bytesRead) || bytesRead < (nuint)pointerSize)
            {
                return 0;
            }

            var dereferencedAddress = pointerSize == sizeof(ulong)
                ? BinaryPrimitives.ReadUInt64LittleEndian(buffer)
                : BinaryPrimitives.ReadUInt32LittleEndian(buffer);

            currentAddress = subtract
                ? unchecked((nuint)(dereferencedAddress - offset))
                : unchecked((nuint)(dereferencedAddress + offset));
        }

        error = false;
        return currentAddress;
    }

    public void LoadCommonModuleList()
    {
        LoadModuleList();
    }

    public List<string> GetCommonModuleList()
    {
        _lock.EnterReadLock();
        try
        {
            return _moduleList.Select(module => module.ModuleName).ToList();
        }
        finally
        {
            _lock.ExitReadLock();
        }
    }

    public void RemoveFinishedLoadingSymbolsNotification(Action notification)
    {
        _lock.EnterWriteLock();
        try
        {
            _symbolsLoadedNotifications.Remove(notification);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void AddFinishedLoadingSymbolsNotification(Action notification)
    {
        _lock.EnterWriteLock();
        try
        {
            _symbolsLoadedNotifications.Add(notification);
        }
        finally
        {
            _lock.ExitWriteLock();
        }
    }

    public void NotifyFinishedLoadingSymbols()
    {
        List<Action> notifications;
        _lock.EnterReadLock();
        try
        {
            notifications = [.. _symbolsLoadedNotifications];
        }
        finally
        {
            _lock.ExitReadLock();
        }

        foreach (var notification in notifications)
        {
            notification();
        }
    }

    private bool AreSymbolsLoadedForModule(string moduleName)
    {
        return GetModuleByName(moduleName, out var module) && module.SymbolsLoaded;
    }

    private bool TryResolveAddressExpression(string expression, CpuContext? context, out nuint result)
    {
        if (TryParseAddressLiteral(expression, out result))
        {
            return true;
        }

        var tokens = Tokenize(expression).ToList();
        if (tokens.Count == 0)
        {
            result = 0;
            return false;
        }

        if (tokens[0].Length > 0 && tokens[0][0] == '*')
        {
            result = 0;
            return false;
        }

        var lastToken = tokens[^1];
        if (lastToken.Length > 0 && lastToken[0] is '*' or '+' or '-')
        {
            result = 0;
            return false;
        }

        var hasMultiplication = false;
        var hasPointer = false;
        for (var index = 0; index < tokens.Count; index++)
        {
            var token = tokens[index];
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            if (IsSeparatorToken(token))
            {
                if (token == "*")
                {
                    hasMultiplication = true;
                }

                if (token is "[" or "]")
                {
                    hasPointer = true;
                }

                continue;
            }

            if (TryResolveToken(token, context, out var resolvedToken))
            {
                tokens[index] = resolvedToken;
                continue;
            }

            if (index < tokens.Count - 1)
            {
                tokens[index + 1] = token + tokens[index + 1];
                tokens[index] = string.Empty;
                continue;
            }

            result = 0;
            return false;
        }

        if (hasPointer)
        {
            result = GetAddressFromPointer(string.Concat(tokens), out var pointerError);
            return !pointerError;
        }

        if (hasMultiplication)
        {
            CollapseMultiplications(tokens);
        }

        return TryEvaluateTokens(tokens, out result);
    }

    private bool TryResolveToken(string token, CpuContext? context, out string resolvedToken)
    {
        if (TryParseAddressLiteral(token, out var literal))
        {
            resolvedToken = literal.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        if (GetModuleByName(token, out var module))
        {
            resolvedToken = module.BaseAddress.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        if (TryResolveRegisterValue(token, context, out var registerValue))
        {
            resolvedToken = registerValue.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        var userDefinedAddress = GetUserDefinedSymbolByName(token);
        if (userDefinedAddress != 0)
        {
            resolvedToken = userDefinedAddress.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        var symbol = FindSymbolByName(token);
        if (symbol is not null)
        {
            resolvedToken = symbol.Address.ToString("X", CultureInfo.InvariantCulture);
            return true;
        }

        resolvedToken = string.Empty;
        return false;
    }

    private CeSymbolInfo? FindSymbolByName(string symbolName)
    {
        return _symbolList.FindSymbol(symbolName.Replace('!', '.'));
    }

    private static bool TryParseAddressLiteral(string text, out nuint value)
    {
        var trimmed = text.Trim();
        if (trimmed.Length == 0)
        {
            value = 0;
            return false;
        }

        if (trimmed.StartsWith('$'))
        {
            return nuint.TryParse(trimmed[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        if (trimmed.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return nuint.TryParse(trimmed[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
        }

        return nuint.TryParse(trimmed, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out value);
    }

    private static bool IsSeparatorToken(string token)
    {
        return token.Length == 1 && token[0] is '[' or ']' or '+' or '-' or '*';
    }

    private static void CollapseMultiplications(IList<string> tokens)
    {
        for (var index = 0; index < tokens.Count; index++)
        {
            if (tokens[index] != "*")
            {
                continue;
            }

            var left = ulong.Parse(tokens[index - 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            var right = ulong.Parse(tokens[index + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
            tokens[index - 1] = unchecked(left * right).ToString("X", CultureInfo.InvariantCulture);
            tokens[index] = string.Empty;
            tokens[index + 1] = string.Empty;
        }
    }

    private static bool TryEvaluateTokens(IReadOnlyList<string> tokens, out nuint result)
    {
        ulong current = 0;
        var subtract = false;

        foreach (var token in tokens)
        {
            if (string.IsNullOrEmpty(token))
            {
                continue;
            }

            switch (token)
            {
                case "+":
                    subtract = false;
                    break;
                case "-":
                    subtract = !subtract;
                    break;
                default:
                    if (!ulong.TryParse(token, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var value))
                    {
                        result = 0;
                        return false;
                    }

                    current = subtract ? unchecked(current - value) : unchecked(current + value);
                    break;
            }
        }

        result = unchecked((nuint)current);
        return true;
    }

    private static bool TryResolveRegisterValue(string token, CpuContext? context, out nuint value)
    {
        value = 0;
        if (context is null)
        {
            return false;
        }

        switch (token.ToUpperInvariant())
        {
            case "EAX":
            case "RAX":
                value = (nuint)context.Rax;
                return true;
            case "EBX":
            case "RBX":
                value = (nuint)context.Rbx;
                return true;
            case "ECX":
            case "RCX":
                value = (nuint)context.Rcx;
                return true;
            case "EDX":
            case "RDX":
                value = (nuint)context.Rdx;
                return true;
            case "ESI":
            case "RSI":
                value = (nuint)context.Rsi;
                return true;
            case "EDI":
            case "RDI":
                value = (nuint)context.Rdi;
                return true;
            case "EBP":
            case "RBP":
                value = (nuint)context.Rbp;
                return true;
            case "ESP":
            case "RSP":
                value = (nuint)context.Rsp;
                return true;
            case "EIP":
            case "RIP":
                value = (nuint)context.Rip;
                return true;
            case "R8":
                value = (nuint)context.R8;
                return true;
            case "R9":
                value = (nuint)context.R9;
                return true;
            case "R10":
                value = (nuint)context.R10;
                return true;
            case "R11":
                value = (nuint)context.R11;
                return true;
            case "R12":
                value = (nuint)context.R12;
                return true;
            case "R13":
                value = (nuint)context.R13;
                return true;
            case "R14":
                value = (nuint)context.R14;
                return true;
            case "R15":
                value = (nuint)context.R15;
                return true;
            default:
                return false;
        }
    }

    private static bool TryParsePointerOffset(string token, out ulong offset, out bool subtract)
    {
        subtract = token.Length > 0 && token[0] == '-';
        var offsetText = token.Length > 0 && token[0] is '+' or '-' ? token[1..] : token;
        if (!TryParseAddressLiteral(offsetText, out var parsedOffset))
        {
            offset = 0;
            return false;
        }

        offset = parsedOffset;
        return true;
    }

    private int ResolvePointerSize()
    {
        if (TargetSelf || CeFuncProc.ProcessHandler.ProcessId == 0)
        {
            return IntPtr.Size;
        }

        return CeFuncProc.ProcessHandler.PointerSize > 0
            ? CeFuncProc.ProcessHandler.PointerSize
            : (CeFuncProc.ProcessHandler.Is64Bit ? sizeof(ulong) : sizeof(uint));
    }

    private uint ResolveCurrentSymbolProcessId()
    {
        return TargetSelf || CeFuncProc.ProcessHandler.ProcessId == 0
            ? (uint)Process.GetCurrentProcess().Id
            : CeFuncProc.ProcessHandler.ProcessId;
    }

    private Process ResolveSymbolTargetProcess()
    {
        return TargetSelf || CeFuncProc.ProcessHandler.ProcessId == 0
            ? Process.GetCurrentProcess()
            : Process.GetProcessById((int)CeFuncProc.ProcessHandler.ProcessId);
    }

    private bool TryRefreshSymbolsAfterLookupMiss()
    {
        if (_locked)
        {
            return false;
        }

        try
        {
            Reinitialize(force: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static ModuleInfo CloneModuleInfo(ModuleInfo module)
    {
        return new ModuleInfo
        {
            ModuleName = module.ModuleName,
            ModulePath = module.ModulePath,
            IsSystemModule = module.IsSystemModule,
            BaseAddress = module.BaseAddress,
            BaseSize = module.BaseSize,
            Is64BitModule = module.Is64BitModule,
            SymbolsLoaded = module.SymbolsLoaded,
        };
    }

    private static IEnumerable<string> Tokenize(string text)
    {
        var tokens = new List<string>();
        var last = 0;
        var inQuote = false;

        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] is '"' or '[' or ']' or '+' or '-' or '*')
            {
                if (text[index] == '"')
                {
                    if (!inQuote)
                    {
                        last = index + 1;
                    }

                    inQuote = !inQuote;
                }

                if (!inQuote)
                {
                    var token = text[last..index].Trim();
                    if (!string.IsNullOrEmpty(token))
                    {
                        tokens.Add(token);
                    }

                    if (text[index] != '"')
                    {
                        tokens.Add(text[index].ToString());
                    }

                    last = index + 1;
                }
            }
        }

        var trailing = text[last..].Trim();
        if (!string.IsNullOrEmpty(trailing))
        {
            tokens.Add(trailing);
        }

        return tokens;
    }
}