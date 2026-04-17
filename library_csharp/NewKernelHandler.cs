using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;

namespace CheatEngine.Library;

public static class NewKernelHandler
{
    private const uint MemCommit = 0x1000;
    private const uint MemReserve = 0x2000;

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nuint NativeVirtualQueryEx(nint hProcess, nint lpAddress, out MemoryBasicInformation lpBuffer, nuint dwLength);

    [DllImport("kernel32.dll", EntryPoint = "VirtualAllocEx", SetLastError = true)]
    private static extern nint NativeVirtualAllocEx(nint hProcess, nint lpAddress, nuint dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", EntryPoint = "VirtualFreeEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeVirtualFreeEx(nint hProcess, nint lpAddress, nuint dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", EntryPoint = "VirtualProtectEx", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeVirtualProtectEx(nint hProcess, nint lpAddress, nuint dwSize, uint flNewProtect, out uint lpflOldProtect);

    [DllImport("kernel32.dll", EntryPoint = "CreateRemoteThread", SetLastError = true)]
    private static extern nint NativeCreateRemoteThread(nint hProcess, nint lpThreadAttributes, nuint dwStackSize, nint lpStartAddress, nint lpParameter, uint dwCreationFlags, out uint lpThreadId);

    [DllImport("kernel32.dll", EntryPoint = "WaitForSingleObject", SetLastError = true)]
    private static extern uint NativeWaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", EntryPoint = "CloseHandle", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool NativeCloseHandle(nint handle);

    public static bool Is64BitOs()
    {
        return Environment.Is64BitOperatingSystem;
    }

    public static bool Is64BitProcess(nint processHandle)
    {
        if (!Environment.Is64BitOperatingSystem)
        {
            return false;
        }

        if (processHandle == 0)
        {
            return false;
        }

        using var handle = new SafeFileHandle(processHandle, ownsHandle: false);
        if (!PInvoke.IsWow64Process(handle, out var isWow64))
        {
            return false;
        }

        return !isWow64;
    }

    public static SafeFileHandle OpenProcessHandle(uint processId, bool forWrite = false)
    {
        var access = PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_INFORMATION | PROCESS_ACCESS_RIGHTS.PROCESS_VM_READ;
        if (forWrite)
        {
            access |= PROCESS_ACCESS_RIGHTS.PROCESS_VM_WRITE | PROCESS_ACCESS_RIGHTS.PROCESS_VM_OPERATION | PROCESS_ACCESS_RIGHTS.PROCESS_CREATE_THREAD;
        }

        unsafe
        {
            var handle = PInvoke.OpenProcess(access, false, processId);
            return new SafeFileHandle((nint)handle.Value, ownsHandle: true);
        }
    }

    public static bool ReadProcessMemory(nint processHandle, nuint baseAddress, Span<byte> buffer, out nuint numberOfBytesRead)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            var result = FileHandler.ReadProcessMemoryFile(processHandle, (nint)baseAddress, buffer, out var fileBytesRead);
            numberOfBytesRead = fileBytesRead;
            return result;
        }

        unsafe
        {
            var handle = new HANDLE((void*)processHandle);
            nuint bytesRead = 0;
            fixed (byte* bufferPtr = buffer)
            {
                var result = PInvoke.ReadProcessMemory(handle, (void*)baseAddress, bufferPtr, (nuint)buffer.Length, &bytesRead);
                numberOfBytesRead = bytesRead;
                return result;
            }
        }
    }

    public static bool WriteProcessMemory(nint processHandle, nuint baseAddress, ReadOnlySpan<byte> buffer, out nuint numberOfBytesWritten)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            var result = FileHandler.WriteProcessMemoryFile(processHandle, (nint)baseAddress, buffer, out var fileBytesWritten);
            numberOfBytesWritten = fileBytesWritten;
            return result;
        }

        unsafe
        {
            var handle = new HANDLE((void*)processHandle);
            nuint bytesWritten = 0;
            fixed (byte* bufferPtr = buffer)
            {
                var result = PInvoke.WriteProcessMemory(handle, (void*)baseAddress, bufferPtr, (nuint)buffer.Length, &bytesWritten);
                numberOfBytesWritten = bytesWritten;
                return result;
            }
        }
    }

    public static nuint VirtualQueryEx(nint processHandle, nuint baseAddress, out MemoryBasicInformation information)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            return FileHandler.VirtualQueryExFile(processHandle, (nint)baseAddress, out information);
        }

        return NativeVirtualQueryEx(NormalizeProcessHandle(processHandle), (nint)baseAddress, out information, (nuint)Marshal.SizeOf<MemoryBasicInformation>());
    }

    public static nuint VirtualAllocEx(nint processHandle, nuint baseAddress, nuint size, uint allocationType, uint protection)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            return 0;
        }

        if ((allocationType & (MemCommit | MemReserve)) == 0)
        {
            allocationType |= MemCommit | MemReserve;
        }

        return unchecked((nuint)NativeVirtualAllocEx(NormalizeProcessHandle(processHandle), (nint)baseAddress, size, allocationType, protection));
    }

    public static bool VirtualFreeEx(nint processHandle, nuint baseAddress, nuint size, uint freeType)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            return false;
        }

        return NativeVirtualFreeEx(NormalizeProcessHandle(processHandle), (nint)baseAddress, size, freeType);
    }

    public static bool VirtualProtectEx(nint processHandle, nuint baseAddress, nuint size, uint newProtect, out uint oldProtect)
    {
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            oldProtect = 0;
            return false;
        }

        return NativeVirtualProtectEx(NormalizeProcessHandle(processHandle), (nint)baseAddress, size, newProtect, out oldProtect);
    }

    public static nint CreateRemoteThread(nint processHandle, nuint startAddress, nuint parameter, uint creationFlags, out uint threadId)
    {
        threadId = 0;
        if (processHandle != 0 && processHandle == FileHandler.FileHandle)
        {
            return 0;
        }

        return NativeCreateRemoteThread(NormalizeProcessHandle(processHandle), 0, 0, (nint)startAddress, (nint)parameter, creationFlags, out threadId);
    }

    public static uint WaitForSingleObject(nint handle, uint milliseconds)
    {
        return NativeWaitForSingleObject(handle, milliseconds);
    }

    public static bool CloseHandle(nint handle)
    {
        return handle != 0 && NativeCloseHandle(handle);
    }

    private static nint NormalizeProcessHandle(nint processHandle)
    {
        return processHandle != 0 ? processHandle : Process.GetCurrentProcess().Handle;
    }
}