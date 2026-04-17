using CheatEngine.Library;

namespace CheatEngine.Library.Dbk32;

public static class Debug
{
    public static void WriteTrace(string message)
    {
        DebugHelper.WriteTrace("[DBK32] " + message);
    }
}