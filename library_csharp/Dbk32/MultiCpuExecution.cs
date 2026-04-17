using CheatEngine.Library;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace CheatEngine.Library.Dbk32;

public static class MultiCpuExecution
{
    public static Task RunOnAllProcessorsAsync(Action<int> action)
    {
        return Task.WhenAll(System.Linq.Enumerable.Range(0, Environment.ProcessorCount).Select(index => Task.Run(() => action(index))));
    }
}