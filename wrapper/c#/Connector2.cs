using System.Runtime.InteropServices;

namespace CheatEngine;

public enum TScanOption
{
    soUnknownValue = 0,
    soExactValue = 1,
    soValueBetween = 2,
    soBiggerThan = 3,
    soSmallerThan = 4,
    soIncreasedValue = 5,
    soIncreasedValueBy = 6,
    soDecreasedValue = 7,
    soDecreasedValueBy = 8,
    soChanged = 9,
    soUnchanged = 10,
    soCustom,
};

public enum TScanType
{
    stNewScan,
    stFirstScan,
    stNextScan,
};

public enum TRoundingType
{
    rtRounded = 0,
    rtExtremerounded = 1,
    rtTruncated = 2,
};

public enum TVariableType
{
    vtByte = 0,
    vtWord = 1,
    vtDword = 2,
    vtQword = 3,
    vtSingle = 4,
    vtDouble = 5,
    vtString = 6,
    vtUnicodeString = 7,
    vtByteArray = 8,
    vtBinary = 9,
    vtAll = 10,
    vtAutoAssembler = 11,
    vtPointer = 12,
    vtCustom = 13,
    vtGrouped = 14,
    vtByteArrays = 15,
}; // all ,grouped and MultiByteArray are special types

public enum TCustomScanType
{
    cstNone,
    cstAutoAssembler,
    cstCPP,
    cstDLLFunction,
};

public enum TFastScanMethod
{
    fsmNotAligned = 0,
    fsmAligned = 1,
    fsmLastDigits = 2,
};

public enum Tscanregionpreference
{
    scanDontCare,
    scanExclude,
    scanInclude,
};

/// <summary>
/// 为 cheatengine-library 导出的公共 API 提供托管包装
/// </summary>
/// <remarks>
/// 该包装公开了进程、虚拟表和内存扫描器功能
/// </remarks>
public sealed unsafe partial class CheatEngineLibrary
{
    const string DllName = "ce-lib";
    static readonly Lock SyncRoot = new();
    static IntPtr libraryHandle;

    static CheatEngineLibrary()
    {
        NativeLibrary.SetDllImportResolver(typeof(CheatEngineLibrary).Assembly, ResolveLibraryImport);
    }

    static string GetPlatformLibraryName()
    {
#if TARGET_X64
        return "ce-lib64";
#elif TARGET_X86
        return "ce-lib32";
#else
        return Environment.Is64BitProcess ? "ce-lib64" : "ce-lib32";
#endif
    }

    static IntPtr ResolveLibraryImport(string libraryName, System.Reflection.Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (!string.Equals(libraryName, DllName, StringComparison.Ordinal))
        {
            return IntPtr.Zero;
        }

        lock (SyncRoot)
        {
            if (libraryHandle != IntPtr.Zero)
            {
                return libraryHandle;
            }

            if (NativeLibrary.TryLoad(GetPlatformLibraryName(), assembly, searchPath, out libraryHandle))
            {
                return libraryHandle;
            }

            return IntPtr.Zero;
        }
    }

    static void EnsureLoaded()
    {
        if (ResolveLibraryImport(DllName, typeof(CheatEngineLibrary).Assembly, null) == IntPtr.Zero)
        {
            throw new DllNotFoundException($"Unable to load {GetPlatformLibraryName()}.dll.");
        }
    }

    [LibraryImport(DllName, EntryPoint = "IGetProcessList")]
    private static partial void GetProcessListNative([MarshalAs(UnmanagedType.BStr)] out string processes);

    [LibraryImport(DllName, EntryPoint = "IOpenProcess")]
    private static partial void OpenProcessNative([MarshalAs(UnmanagedType.BStr)] string pid);

    [LibraryImport(DllName, EntryPoint = "IResetTable")]
    private static partial void ResetTableNative();

    [LibraryImport(DllName, EntryPoint = "IAddScript")]
    private static partial void AddScriptNative([MarshalAs(UnmanagedType.BStr)] string name, [MarshalAs(UnmanagedType.BStr)] string script);

    [LibraryImport(DllName, EntryPoint = "IActivateRecord")]
    private static partial void ActivateRecordNative(int id, [MarshalAs(UnmanagedType.Bool)] bool activate);

    [LibraryImport(DllName, EntryPoint = "IRemoveRecord")]
    private static partial void RemoveRecordNative(int id);

    [LibraryImport(DllName, EntryPoint = "IApplyFreeze")]
    private static partial void ApplyFreezeNative();

    [LibraryImport(DllName, EntryPoint = "IAddAddressManually")]
    private static partial void AddAddressManuallyNative([MarshalAs(UnmanagedType.BStr)] string initialaddress, TVariableType vartype);

    [LibraryImport(DllName, EntryPoint = "IGetValue")]
    private static partial void GetValueNative(int id, [MarshalAs(UnmanagedType.BStr)] out string value);

    [LibraryImport(DllName, EntryPoint = "ISetValue")]
    private static partial void SetValueNative(int id, [MarshalAs(UnmanagedType.BStr)] string value, [MarshalAs(UnmanagedType.Bool)] bool freezer);

    [LibraryImport(DllName, EntryPoint = "IProcessAddress")]
    private static partial void ProcessAddressNative(
        [MarshalAs(UnmanagedType.BStr)] string address,
        TVariableType vartype,
        [MarshalAs(UnmanagedType.Bool)] bool showashexadecimal,
        [MarshalAs(UnmanagedType.Bool)] bool showAsSigned,
        int bytesize,
        [MarshalAs(UnmanagedType.BStr)] out string value);

    [LibraryImport(DllName, EntryPoint = "IInitMemoryScanner")]
    private static partial void InitMemoryScannerNative(int handle);

    [LibraryImport(DllName, EntryPoint = "INewScan")]
    private static partial void NewScanNative();

    [LibraryImport(DllName, EntryPoint = "IConfigScanner")]
    private static partial void ConfigScannerNative(Tscanregionpreference scanWritable, Tscanregionpreference scanExecutable, Tscanregionpreference scanCopyOnWrite);

    [LibraryImport(DllName, EntryPoint = "IFirstScan")]
    private static partial void FirstScanNative(
        TScanOption scanOption,
        TVariableType variableType,
        TRoundingType roundingtype,
        [MarshalAs(UnmanagedType.BStr)] string scanvalue1,
        [MarshalAs(UnmanagedType.BStr)] string scanvalue2,
        [MarshalAs(UnmanagedType.BStr)] string startaddress,
        [MarshalAs(UnmanagedType.BStr)] string stopaddress,
        [MarshalAs(UnmanagedType.Bool)] bool hexadecimal,
        [MarshalAs(UnmanagedType.Bool)] bool binaryStringAsDecimal,
        [MarshalAs(UnmanagedType.Bool)] bool unicode,
        [MarshalAs(UnmanagedType.Bool)] bool casesensitive,
        TFastScanMethod fastscanmethod,
        [MarshalAs(UnmanagedType.BStr)] string fastscanparameter);

    [LibraryImport(DllName, EntryPoint = "INextScan")]
    private static partial void NextScanNative(
        TScanOption scanOption,
        TRoundingType roundingtype,
        [MarshalAs(UnmanagedType.BStr)] string scanvalue1,
        [MarshalAs(UnmanagedType.BStr)] string scanvalue2,
        [MarshalAs(UnmanagedType.Bool)] bool hexadecimal,
        [MarshalAs(UnmanagedType.Bool)] bool binaryStringAsDecimal,
        [MarshalAs(UnmanagedType.Bool)] bool unicode,
        [MarshalAs(UnmanagedType.Bool)] bool casesensitive,
        [MarshalAs(UnmanagedType.Bool)] bool percentage,
        [MarshalAs(UnmanagedType.Bool)] bool compareToSavedScan,
        [MarshalAs(UnmanagedType.BStr)] string savedscanname);

    [LibraryImport(DllName, EntryPoint = "ICountAddressesFound")]
    private static partial long CountAddressesFoundNative();

    [LibraryImport(DllName, EntryPoint = "IGetAddress")]
    private static partial void GetAddressNative(long index, [MarshalAs(UnmanagedType.BStr)] out string address, [MarshalAs(UnmanagedType.BStr)] out string value);

    [LibraryImport(DllName, EntryPoint = "IInitFoundList")]
    private static partial void InitFoundListNative(
        TVariableType vartype,
        int varlength,
        [MarshalAs(UnmanagedType.Bool)] bool hexadecimal,
        [MarshalAs(UnmanagedType.Bool)] bool signed,
        [MarshalAs(UnmanagedType.Bool)] bool binaryasdecimal,
        [MarshalAs(UnmanagedType.Bool)] bool unicode);

    [LibraryImport(DllName, EntryPoint = "IResetValues")]
    private static partial void ResetValuesNative();

    [LibraryImport(DllName, EntryPoint = "IGetBinarySize")]
    private static partial int GetBinarySizeNative();

    /// <summary>
    /// 获取当前所有正在运行的进程列表
    /// </summary>
    /// <param name="processes">接收进程列表</param>
    public void iGetProcessList(out string processes) => GetProcessListNative(out processes);

    /// <summary>
    /// 打开指定 ProcessId 的进程，并清空 Virtual Cheat Table 
    /// </summary>
    /// <param name="pid">8 位十六进制字符串形式的进程标识符</param>
    public void iOpenProcess(string pid) => OpenProcessNative(pid);

    /// <summary>
    /// 清空 Virtual Cheat Table 
    /// </summary>
    public void iResetTable() => ResetTableNative();

    /// <summary>
    /// 向 Virtual Cheat Table 中添加一段 Auto Assembler 脚本
    /// </summary>
    /// <param name="name">脚本名称</param>
    /// <param name="script">脚本文本内容</param>
    public void iAddScript(string name, string script) => AddScriptNative(name, script);

    /// <summary>
    /// 激活或停用 Virtual Cheat Table 中的脚本或内存记录
    /// </summary>
    /// <param name="id">记录索引</param>
    /// <param name="activate">设为 <see langword="true"/> 表示激活，设为 <see langword="false"/> 表示停用</param>
    /// <remarks>
    /// 对脚本而言，激活表示注入或移除脚本；对内存记录而言，激活表示冻结或取消冻结
    /// </remarks>
    public void iActivateRecord(int id, bool activate) => ActivateRecordNative(id, activate);

    /// <summary>
    /// 从 Virtual Cheat Table 中移除脚本或地址记录
    /// </summary>
    /// <param name="id">从 0 开始的记录索引</param>
    public void iRemoveRecord(int id) => RemoveRecordNative(id);

    /// <summary>
    /// 对 Virtual Cheat Table 中所有已激活的地址应用冻结操作
    /// </summary>
    /// <remarks>
    /// 应在地址已被添加、赋值并激活后由定时器周期性调用
    /// </remarks>
    public void iApplyFreeze() => ApplyFreezeNative();

    /// <summary>
    /// 将指定地址添加到 Virtual Cheat Table 中
    /// </summary>
    /// <param name="initialaddress">格式为 $XXXXXXXXXXXXXXXX 的地址字符串</param>
    /// <param name="vartype">与该地址关联的变量类型</param>
    public void iAddAddressManually(string initialaddress, TVariableType vartype) => AddAddressManuallyNative(initialaddress, vartype);

    /// <summary>
    /// 读取虚拟表中指定索引地址的当前值
    /// </summary>
    /// <param name="id">记录索引</param>
    /// <param name="value">接收当前值</param>
    public void iGetValue(int id, out string value) => GetValueNative(id, out value);

    /// <summary>
    /// 向虚拟表中指定索引的地址写入一个值
    /// </summary>
    /// <param name="id">记录索引</param>
    /// <param name="value">要写入的值</param>
    /// <param name="freezer">设置值时，是否将该记录按冻结项的方式更新</param>
    public void iSetValue(int id, string value, bool freezer) => SetValueNative(id, value, freezer);

    /// <summary>
    /// 读取指定地址，并返回该地址指向的值
    /// </summary>
    /// <param name="address">格式为 $XXXXXXXXXXXXXXXX 的地址字符串</param>
    /// <param name="vartype">要读取的变量类型</param>
    /// <param name="showashexadecimal">返回值是否按十六进制格式化</param>
    /// <param name="showAsSigned">返回值是否按有符号数格式化</param>
    /// <param name="bytesize">本机读取器使用的字节大小</param>
    /// <param name="value">接收格式化后的值</param>
    public void iProcessAddress(string address, TVariableType vartype, bool showashexadecimal, bool showAsSigned, int bytesize, out string value) =>
        ProcessAddressNative(address, vartype, showashexadecimal, showAsSigned, bytesize, out value);

    /// <summary>
    /// 初始化内存扫描器
    /// </summary>
    /// <param name="handle">传给本机扫描器的宿主窗口句柄</param>
    /// <remarks>
    /// 对同一个扫描器实例，这个方法只应调用一次
    /// </remarks>
    public void iInitMemoryScanner(int handle) => InitMemoryScannerNative(handle);

    /// <summary>
    /// 开始一次新的扫描
    /// </summary>
    public void iNewScan() => NewScanNative();

    /// <summary>
    /// 配置扫描时包含哪些内存区域
    /// </summary>
    /// <param name="scanWritable">控制是否扫描可写页面</param>
    /// <param name="scanExecutable">控制是否扫描可执行页面</param>
    /// <param name="scanCopyOnWrite">控制是否扫描写时复制页面</param>
    public void iConfigScanner(Tscanregionpreference scanWritable, Tscanregionpreference scanExecutable, Tscanregionpreference scanCopyOnWrite) =>
        ConfigScannerNative(scanWritable, scanExecutable, scanCopyOnWrite);

    /// <summary>
    /// 按指定条件启动第一次内存扫描
    /// </summary>
    /// <param name="scanOption">要执行的扫描模式</param>
    /// <param name="variableType">要搜索的变量类型</param>
    /// <param name="roundingtype">浮点扫描使用的舍入模式</param>
    /// <param name="scanvalue1">主扫描值</param>
    /// <param name="scanvalue2">次扫描值，用于范围类扫描</param>
    /// <param name="startaddress">扫描起始地址，包含边界</param>
    /// <param name="stopaddress">扫描结束地址，包含边界</param>
    /// <param name="hexadecimal">扫描值是否按十六进制解释</param>
    /// <param name="binaryStringAsDecimal">二进制字符串输入是否按十进制解释</param>
    /// <param name="unicode">字符串扫描是否使用 Unicode 文本</param>
    /// <param name="casesensitive">字符串扫描是否区分大小写</param>
    /// <param name="fastscanmethod">要使用的快速扫描模式</param>
    /// <param name="fastscanparameter">快速扫描参数，例如对齐值或尾数字过滤条件</param>
    public void iFirstScan(
        TScanOption scanOption,
        TVariableType variableType,
        TRoundingType roundingtype,
        string scanvalue1,
        string scanvalue2,
        string startaddress,
        string stopaddress,
        bool hexadecimal,
        bool binaryStringAsDecimal,
        bool unicode,
        bool casesensitive,
        TFastScanMethod fastscanmethod,
        string fastscanparameter) =>
        FirstScanNative(
            scanOption,
            variableType,
            roundingtype,
            scanvalue1,
            scanvalue2,
            startaddress,
            stopaddress,
            hexadecimal,
            binaryStringAsDecimal,
            unicode,
            casesensitive,
            fastscanmethod,
            fastscanparameter);

    /// <summary>
    /// 按指定的下一次扫描条件继续当前扫描
    /// </summary>
    /// <param name="scanOption">要执行的下一次扫描模式</param>
    /// <param name="roundingtype">浮点扫描使用的舍入模式</param>
    /// <param name="scanvalue1">主比较值</param>
    /// <param name="scanvalue2">次比较值，用于范围类扫描</param>
    /// <param name="hexadecimal">扫描值是否按十六进制解释</param>
    /// <param name="binaryStringAsDecimal">二进制字符串输入是否按十进制解释</param>
    /// <param name="unicode">字符串扫描是否使用 Unicode 文本</param>
    /// <param name="casesensitive">字符串扫描是否区分大小写</param>
    /// <param name="percentage">比较值是否应按百分比处理</param>
    /// <param name="compareToSavedScan">是否与已保存扫描结果比较，而不是与上一次结果比较</param>
    /// <param name="savedscanname">当启用 <paramref name="compareToSavedScan"/> 时使用的已保存扫描名称</param>
    public void iNextScan(
        TScanOption scanOption,
        TRoundingType roundingtype,
        string scanvalue1,
        string scanvalue2,
        bool hexadecimal,
        bool binaryStringAsDecimal,
        bool unicode,
        bool casesensitive,
        bool percentage,
        bool compareToSavedScan,
        string savedscanname) =>
        NextScanNative(
            scanOption,
            roundingtype,
            scanvalue1,
            scanvalue2,
            hexadecimal,
            binaryStringAsDecimal,
            unicode,
            casesensitive,
            percentage,
            compareToSavedScan,
            savedscanname);

    /// <summary>
    /// 获取当前扫描找到的地址数量
    /// </summary>
    /// <returns>找到的地址总数</returns>
    public long iCountAddressesFound() => CountAddressesFoundNative();

    /// <summary>
    /// 从当前结果列表中读取一组地址和值
    /// </summary>
    /// <param name="index">要读取的结果列表索引</param>
    /// <param name="address">接收地址</param>
    /// <param name="value">接收格式化后的值</param>
    /// <remarks>
    /// 本机结果列表按 1024 个地址为一页做缓冲，并围绕请求的索引进行加载
    /// </remarks>
    public void iGetAddress(long index, out string address, out string value) => GetAddressNative(index, out address, out value);

    /// <summary>
    /// 在扫描完成后初始化结果列表
    /// </summary>
    /// <param name="vartype">结果列表公开的变量类型</param>
    /// <param name="varlength">结果列表使用的变量长度</param>
    /// <param name="hexadecimal">值是否按十六进制格式化</param>
    /// <param name="signed">值是否按有符号数格式化。</param>
    /// <param name="binaryasdecimal">二进制值是否按十进制格式化</param>
    /// <param name="unicode">字符串值是否按 Unicode 处理</param>
    /// <remarks>
    /// 这个方法只应调用一次，并且应在扫描完成回调之后调用
    /// </remarks>
    public void iInitFoundList(TVariableType vartype, int varlength, bool hexadecimal, bool signed, bool binaryasdecimal, bool unicode) =>
        InitFoundListNative(vartype, varlength, hexadecimal, signed, binaryasdecimal, unicode);

    /// <summary>
    /// 重置当前结果列表中的缓存值
    /// </summary>
    public void iResetValues() => ResetValuesNative();

    /// <summary>
    /// 获取当前扫描结果关联的二进制大小
    /// </summary>
    /// <returns>本机扫描器返回的当前二进制大小</returns>
    public int iGetBinarySize() => GetBinarySizeNative();

    /// <summary>
    /// 加载与当前进程架构匹配的本机 Cheat Engine 库
    /// </summary>
    public void loadEngine()
    {
        EnsureLoaded();
    }

    /// <summary>
    /// 释放已加载的本机 Cheat Engine 库句柄
    /// </summary>
    public void unloadEngine()
    {
        lock (SyncRoot)
        {
            if (libraryHandle == IntPtr.Zero)
            {
                return;
            }

            NativeLibrary.Free(libraryHandle);
            libraryHandle = IntPtr.Zero;
        }
    }

}
