using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;

namespace CheatEngine.Library;

[StructLayout(LayoutKind.Sequential)]
public struct MemoryBasicInformation
{
    public nint BaseAddress;
    public nint AllocationBase;
    public uint AllocationProtect;
    public nuint RegionSize;
    public uint State;
    public uint Protect;
    public uint Type;
}

public static class FileHandler
{
    private const uint PageExecuteReadWrite = 0x40;
    private const uint MemCommit = 0x1000;
    private const uint MemPrivate = 0x20000;

    public static nint FileHandle { get; set; }

    public static bool ReadProcessMemoryFile(nint processHandle, nint baseAddress, Span<byte> buffer, out uint numberOfBytesRead)
    {
        numberOfBytesRead = 0;
        if (!TryGetFileSize(processHandle, out var fileSize))
        {
            return false;
        }

        var offset = baseAddress.ToInt64();
        if (offset < 0 || offset >= fileSize)
        {
            return false;
        }

        buffer.Clear();
        var bytesToRead = (int)Math.Min(buffer.Length, fileSize - offset);
        try
        {
            using var handle = new SafeFileHandle(processHandle, ownsHandle: false);
            numberOfBytesRead = (uint)RandomAccess.Read(handle, buffer[..bytesToRead], offset);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static bool WriteProcessMemoryFile(nint processHandle, nint baseAddress, ReadOnlySpan<byte> buffer, out uint numberOfBytesWritten)
    {
        numberOfBytesWritten = 0;
        if (baseAddress.ToInt64() < 0)
        {
            return false;
        }

        try
        {
            using var handle = new SafeFileHandle(processHandle, ownsHandle: false);
            RandomAccess.Write(handle, buffer, baseAddress.ToInt64());
            numberOfBytesWritten = (uint)buffer.Length;
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static uint VirtualQueryExFile(nint processHandle, nint address, out MemoryBasicInformation buffer)
    {
        buffer = default;
        if (!TryGetFileSize(processHandle, out var fileSize))
        {
            return 0;
        }

        var addressValue = address.ToInt64();
        if (addressValue < 0 || addressValue > fileSize)
        {
            return 0;
        }

        var pageBase = (nint)((ulong)addressValue & ~0xfffUL);
        var regionSize = (nuint)(fileSize - pageBase.ToInt64());
        var remainder = regionSize % 0x1000;
        if (remainder != 0)
        {
            regionSize += 0x1000 - remainder;
        }

        buffer = new MemoryBasicInformation
        {
            BaseAddress = pageBase,
            AllocationBase = pageBase,
            AllocationProtect = PageExecuteReadWrite,
            RegionSize = regionSize,
            State = MemCommit,
            Protect = PageExecuteReadWrite,
            Type = MemPrivate,
        };

        return (uint)Marshal.SizeOf<MemoryBasicInformation>();
    }

    private static bool TryGetFileSize(nint processHandle, out long fileSize)
    {
        if (processHandle == 0)
        {
            fileSize = 0;
            return false;
        }

        using var handle = new SafeFileHandle(processHandle, ownsHandle: false);
        return PInvoke.GetFileSizeEx(handle, out fileSize);
    }
}