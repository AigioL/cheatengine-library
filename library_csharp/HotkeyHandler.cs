using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Windows.Win32;

namespace CheatEngine.Library;

public sealed class HotkeyItem
{
    public KeyCombo Keys { get; init; }
    public nint WindowToNotify { get; set; }
    public int Id { get; init; }
    public object? MemoryRecordHotkey { get; init; }
    public GenericHotkey? GenericHotkey { get; init; }
    public ushort Modifiers { get; init; }
    public ushort VirtualKey { get; init; }
    public bool Handler2 { get; init; }
    public uint LastActivate { get; set; }
    public int DelayBetweenActivate { get; set; }
}

public static class HotkeyHandler
{
    private static readonly object SyncRoot = new();
    private static readonly List<HotkeyItem> Hotkeys = new();
    private static readonly Thread PollThread;
    private static volatile bool _suspended;

    static HotkeyHandler()
    {
        PollThread = new Thread(PollHotkeys)
        {
            IsBackground = true,
            Name = "CheatEngine.Library.HotkeyHandler",
        };
        PollThread.Start();
    }

    public static int HotkeyPollInterval { get; set; } = 100;

    public static int HotkeyIdleTime { get; set; } = 100;

    public static bool RegisterHotKey(nint hWnd, int id, uint fsModifiers, uint vk)
    {
        ConvertOldHotkeyToKeyCombo(fsModifiers, vk, out var combo);
        return RegisterHotKey2(hWnd, id, combo);
    }

    public static bool RegisterHotKey2(nint hWnd, int id, KeyCombo keys, object? memoryRecordHotkey = null, GenericHotkey? genericHotkey = null)
    {
        lock (SyncRoot)
        {
            Hotkeys.Add(new HotkeyItem
            {
                WindowToNotify = hWnd,
                Keys = keys,
                Id = id,
                Modifiers = 0,
                VirtualKey = 0,
                Handler2 = true,
                DelayBetweenActivate = 0,
                MemoryRecordHotkey = memoryRecordHotkey,
                GenericHotkey = genericHotkey,
            });
        }

        return true;
    }

    public static bool UnregisterHotKey(nint hWnd, int id)
    {
        lock (SyncRoot)
        {
            return Hotkeys.RemoveAll(item => item.WindowToNotify == hWnd && item.Id == id) > 0;
        }
    }

    public static bool UnregisterAddressHotkey(object memoryRecordHotkey)
    {
        lock (SyncRoot)
        {
            return Hotkeys.RemoveAll(item => ReferenceEquals(item.MemoryRecordHotkey, memoryRecordHotkey)) > 0;
        }
    }

    public static bool UnregisterGenericHotkey(GenericHotkey genericHotkey)
    {
        lock (SyncRoot)
        {
            return Hotkeys.RemoveAll(item => ReferenceEquals(item.GenericHotkey, genericHotkey)) > 0;
        }
    }

    public static HotkeyItem? GetGenericHotkeyKeyItem(GenericHotkey genericHotkey)
    {
        lock (SyncRoot)
        {
            return Hotkeys.FirstOrDefault(item => ReferenceEquals(item.GenericHotkey, genericHotkey));
        }
    }

    public static int GetKeyComboLength(KeyCombo keyCombo)
    {
        return keyCombo.ToArray().Count(key => key != 0);
    }

    public static bool CheckKeyCombo(KeyCombo keyCombo)
    {
        var keys = keyCombo.ToArray();
        if (keys[0] == 0)
        {
            return false;
        }

        foreach (var key in keys)
        {
            if (key == 0)
            {
                break;
            }

            if (!IsKeyPressed(key))
            {
                return false;
            }
        }

        return true;
    }

    public static void ConvertOldHotkeyToKeyCombo(uint fsModifiers, uint vk, out KeyCombo combo)
    {
        var keys = new ushort[5];
        keys[0] = (ushort)vk;
        var index = 1;
        if ((fsModifiers & 0x0002U) != 0)
        {
            keys[index++] = 0x11;
        }

        if ((fsModifiers & 0x0001U) != 0)
        {
            keys[index++] = 0x10;
        }

        if ((fsModifiers & 0x0004U) != 0)
        {
            keys[index++] = 0x12;
        }

        combo = new KeyCombo(keys[0], keys[1], keys[2], keys[3], keys[4]);
    }

    public static void ClearHotkeyList()
    {
        lock (SyncRoot)
        {
            Hotkeys.Clear();
        }
    }

    public static void SuspendHotkeyHandler()
    {
        _suspended = true;
    }

    public static void ResumeHotkeyHandler()
    {
        _suspended = false;
    }

    public static void HotkeyTargetWindowHandleChanged(nint oldHandle, nint newHandle)
    {
        lock (SyncRoot)
        {
            foreach (var item in Hotkeys)
            {
                if (item.WindowToNotify == oldHandle)
                {
                    item.WindowToNotify = newHandle;
                }
            }
        }
    }

    private static void PollHotkeys()
    {
        while (true)
        {
            if (!_suspended)
            {
                List<HotkeyItem> snapshot;
                lock (SyncRoot)
                {
                    snapshot = [.. Hotkeys];
                }

                var tick = Environment.TickCount64;
                foreach (var item in snapshot)
                {
                    if (!CheckKeyCombo(item.Keys))
                    {
                        continue;
                    }

                    if (item.DelayBetweenActivate > 0 && (tick - item.LastActivate) < item.DelayBetweenActivate)
                    {
                        continue;
                    }

                    item.LastActivate = (uint)tick;
                    item.GenericHotkey?.Notify();
                }
            }

            Thread.Sleep(Math.Max(1, HotkeyPollInterval));
        }
    }

    private static bool IsKeyPressed(ushort key)
    {
        return (PInvoke.GetAsyncKeyState(key) & 0x8000) != 0;
    }
}