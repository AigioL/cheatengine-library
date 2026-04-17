using System;

namespace CheatEngine.Library;

public sealed class GenericHotkey : IDisposable
{
    private readonly nint _owner;

    public GenericHotkey(nint owner, Action<GenericHotkey>? routine, KeyCombo keys)
    {
        _owner = owner;
        Keys = keys;
        OnNotify = routine;
        HotkeyHandler.RegisterHotKey2(_owner, -1, keys, null, this);
    }

    public KeyCombo Keys { get; }

    public Action<GenericHotkey>? OnNotify { get; }

    public int DelayBetweenActivate
    {
        get => HotkeyHandler.GetGenericHotkeyKeyItem(this)?.DelayBetweenActivate ?? 0;
        set
        {
            var item = HotkeyHandler.GetGenericHotkeyKeyItem(this);
            if (item is not null)
            {
                item.DelayBetweenActivate = value;
            }
        }
    }

    public void Notify()
    {
        OnNotify?.Invoke(this);
    }

    public void Dispose()
    {
        HotkeyHandler.UnregisterGenericHotkey(this);
    }
}