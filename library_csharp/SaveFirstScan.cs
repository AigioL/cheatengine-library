namespace CheatEngine.Library;

public static class SaveFirstScan
{
    public static void Save(string folder)
    {
        ScanResultBinaryFormat.CopyResultFiles(folder, "TMP", "FIRST");
    }
}