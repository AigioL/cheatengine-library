using System;
using System.Threading;
using Windows.Win32;

namespace CheatEngine.Library;

public sealed class GuiSafeCriticalSection : IDisposable
{
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly object _stateLock = new();
    private int _lockCount;
    private uint _lockedThreadId;

    public void Enter(uint maxTimeout = uint.MaxValue, uint currentThreadId = 0)
    {
        currentThreadId = currentThreadId == 0 ? PInvoke.GetCurrentThreadId() : currentThreadId;

        lock (_stateLock)
        {
            if (_lockCount > 0 && currentThreadId == _lockedThreadId)
            {
                _lockCount++;
                return;
            }
        }

        var timeout = maxTimeout == uint.MaxValue ? Timeout.Infinite : checked((int)Math.Min(maxTimeout, int.MaxValue));
        if (!_semaphore.Wait(timeout))
        {
            throw new TimeoutException("Timed out while waiting for the critical section.");
        }

        lock (_stateLock)
        {
            _lockedThreadId = currentThreadId;
            _lockCount = 1;
        }
    }

    public void Leave(uint currentThreadId = 0)
    {
        currentThreadId = currentThreadId == 0 ? PInvoke.GetCurrentThreadId() : currentThreadId;

        lock (_stateLock)
        {
            if (_lockCount == 0 || currentThreadId != _lockedThreadId)
            {
                throw new InvalidOperationException("Criticalsection leave without enter");
            }

            _lockCount--;
            if (_lockCount != 0)
            {
                return;
            }

            _lockedThreadId = 0;
        }

        _semaphore.Release();
    }

    public void Dispose()
    {
        _semaphore.Dispose();
    }
}