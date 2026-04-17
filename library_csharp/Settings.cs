namespace CheatEngine.Library;

public static class Settings
{
    public static bool AllIncludesCustomTypes
    {
        get => CustomTypeHandler.AllIncludesCustomType;
        set => CustomTypeHandler.AllIncludesCustomType = value;
    }
}