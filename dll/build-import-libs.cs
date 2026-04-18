using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;

internal sealed record Options(string Architecture, bool BuildDll, bool ShowUsage);

internal static class Program
{
    private static readonly Regex ExportLineRegex = new(@"^\s*\[\s*\d+\]\s+(.+?)\s*$", RegexOptions.Compiled);
    private static readonly string[] VisualStudioRoots =
    [
        @"C:\Program Files\Microsoft Visual Studio",
        @"C:\Program Files (x86)\Microsoft Visual Studio"
    ];

    private static int Main(string[] args)
    {
        try
        {
            var scriptPath = Path.GetFullPath(GetScriptPath());
            var dllDirectory = Path.GetDirectoryName(scriptPath) ?? throw new InvalidOperationException("Unable to resolve script directory.");
            var outputDirectory = Path.Combine(dllDirectory, "bin");
            var repoRoot = Directory.GetParent(dllDirectory)?.FullName ?? throw new InvalidOperationException("Unable to resolve repository root.");
            var projectFile = Path.Combine(dllDirectory, "ce_lib.lpi");

            var options = ParseArguments(args);
            if (options.ShowUsage)
            {
                PrintUsage(Path.GetFileName(scriptPath));
                return 0;
            }

            var lazbuildExe = GetLazbuildPath();
            var objdumpExe = GetObjdumpPath();
            var libExe = GetLibExePath();

            Directory.CreateDirectory(outputDirectory);

            if (options.Architecture is "x64" or "both")
            {
                BuildImportLib(
                    options,
                    lazbuildExe,
                    objdumpExe,
                    libExe,
                    repoRoot,
                    projectFile,
                    dllDirectory,
                    outputDirectory,
                    architectureName: "x64",
                    buildMode: "Release 64-Bit",
                    machine: "X64",
                    dllName: "ce-lib64.dll");
            }

            if (options.Architecture is "x86" or "both")
            {
                BuildImportLib(
                    options,
                    lazbuildExe,
                    objdumpExe,
                    libExe,
                    repoRoot,
                    projectFile,
                    dllDirectory,
                    outputDirectory,
                    architectureName: "x86",
                    buildMode: "Release 32-Bit",
                    machine: "X86",
                    dllName: "ce-lib32.dll");
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static string GetScriptPath([CallerFilePath] string path = "") => path;

    private static Options ParseArguments(string[] args)
    {
        var architecture = "both";
        var buildDll = false;

        for (var index = 0; index < args.Length; index++)
        {
            var argument = args[index];

            if (Matches(argument, "-?") || Matches(argument, "/?") || Matches(argument, "-h") || Matches(argument, "--help"))
            {
                return new Options(architecture, buildDll, ShowUsage: true);
            }

            if (Matches(argument, "x64") || Matches(argument, "x86") || Matches(argument, "both"))
            {
                architecture = argument.ToLowerInvariant();
                continue;
            }

            if (Matches(argument, "-Architecture") || Matches(argument, "/Architecture") || Matches(argument, "--architecture"))
            {
                if (index + 1 >= args.Length)
                {
                    throw new InvalidOperationException("Missing value for -Architecture.");
                }

                architecture = args[++index].ToLowerInvariant();
                continue;
            }

            if (Matches(argument, "-BuildDll") || Matches(argument, "/BuildDll") || Matches(argument, "--build-dll"))
            {
                buildDll = true;
                continue;
            }

            throw new InvalidOperationException($"Unknown argument: {argument}");
        }

        if (architecture is not ("x64" or "x86" or "both"))
        {
            throw new InvalidOperationException($"Invalid architecture: {architecture}");
        }

        return new Options(architecture, buildDll, ShowUsage: false);
    }

    private static bool Matches(string left, string right) => string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static void PrintUsage(string scriptName)
    {
        Console.WriteLine($"Usage: dotnet run --file {scriptName} [x64|x86|both]");
        Console.WriteLine($"       dotnet run --file {scriptName} -- -Architecture [x64|x86|both] [-BuildDll]");
        Console.WriteLine("Artifacts are written to the bin directory next to this script.");
    }

    private static string GetLazbuildPath()
    {
        return FindFirstExistingFile(
                   new[]
                   {
                       FindExecutableInPath("lazbuild.exe"),
                       @"C:\lazarus\lazbuild.exe"
                   },
                   "lazbuild.exe",
                   Array.Empty<string>())
               ?? throw new InvalidOperationException("Unable to locate lazbuild.exe.");
    }

    private static string GetObjdumpPath()
    {
        return FindFirstExistingFile(
                   new[]
                   {
                       FindExecutableInPath("objdump.exe"),
                       @"C:\lazarus\fpc\3.2.2\bin\x86_64-win64\objdump.exe"
                   },
                   "objdump.exe",
                   new[] { @"C:\lazarus\fpc" })
               ?? throw new InvalidOperationException("Unable to locate objdump.exe.");
    }

    private static string GetLibExePath()
    {
        var pathValue = FindExecutableInPath("lib.exe");
        if (!string.IsNullOrWhiteSpace(pathValue))
        {
            return pathValue;
        }

        string? fallback = null;

        foreach (var root in VisualStudioRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            foreach (var file in EnumerateFilesSafe(root, "lib.exe"))
            {
                if (file.EndsWith(@"Hostx64\x64\lib.exe", StringComparison.OrdinalIgnoreCase))
                {
                    return file;
                }

                fallback ??= file;
            }
        }

        return fallback ?? throw new InvalidOperationException("Unable to locate MSVC lib.exe.");
    }

    private static string? FindFirstExistingFile(IEnumerable<string?> candidates, string filter, IEnumerable<string> searchRoots)
    {
        foreach (var candidate in candidates)
        {
            if (!string.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var root in searchRoots)
        {
            if (!Directory.Exists(root))
            {
                continue;
            }

            var match = EnumerateFilesSafe(root, filter).FirstOrDefault();
            if (!string.IsNullOrWhiteSpace(match))
            {
                return match;
            }
        }

        return null;
    }

    private static string? FindExecutableInPath(string fileName)
    {
        var pathValue = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrWhiteSpace(pathValue))
        {
            return null;
        }

        foreach (var entry in pathValue.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var directory = entry.Trim('"');
            if (string.IsNullOrWhiteSpace(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, fileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return null;
    }

    private static IEnumerable<string> EnumerateFilesSafe(string root, string pattern)
    {
        var pending = new Queue<string>();
        pending.Enqueue(root);

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            string[] files;
            try
            {
                files = Directory.GetFiles(current, pattern);
            }
            catch
            {
                files = Array.Empty<string>();
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current);
            }
            catch
            {
                directories = Array.Empty<string>();
            }

            foreach (var directory in directories)
            {
                pending.Enqueue(directory);
            }
        }
    }

    private static void BuildImportLib(
        Options options,
        string lazbuildExe,
        string objdumpExe,
        string libExe,
        string repoRoot,
        string projectFile,
        string dllDirectory,
        string outputDirectory,
        string architectureName,
        string buildMode,
        string machine,
        string dllName)
    {
        var dllPath = Path.Combine(outputDirectory, dllName);
        var libPath = Path.ChangeExtension(dllPath, ".lib");
        var defPath = Path.ChangeExtension(dllPath, ".def");

        BuildDllIfNeeded(options, lazbuildExe, repoRoot, projectFile, buildMode, dllDirectory, outputDirectory, dllName);

        var exports = GetExports(objdumpExe, dllPath);
        WriteDefFile(dllPath, defPath, exports);

        try
        {
            var exitCode = RunProcess(
                libExe,
                new[] { $"/def:{defPath}", $"/machine:{machine}", $"/out:{libPath}" },
                workingDirectory: dllDirectory,
                captureOutput: false,
                out _,
                out _);

            if (exitCode != 0)
            {
                throw new InvalidOperationException($"lib.exe failed for {architectureName}.");
            }
        }
        finally
        {
            if (File.Exists(defPath))
            {
                File.Delete(defPath);
            }
        }

        Console.WriteLine($"Created {libPath}");
    }

    private static void BuildDllIfNeeded(
        Options options,
        string lazbuildExe,
        string repoRoot,
        string projectFile,
        string buildMode,
        string dllDirectory,
        string outputDirectory,
        string dllName)
    {
        var sourceDllPath = Path.Combine(dllDirectory, dllName);
        var targetDllPath = Path.Combine(outputDirectory, dllName);

        if (File.Exists(targetDllPath) && !options.BuildDll)
        {
            return;
        }

        if (!options.BuildDll && File.Exists(sourceDllPath))
        {
            CopyArtifact(sourceDllPath, targetDllPath);
            EnsureDebugArtifact(sourceDllPath, targetDllPath);
            return;
        }

        var exitCode = RunProcess(
            lazbuildExe,
            new[]
            {
                $"--build-mode={buildMode}",
                $"--opt=-FE{outputDirectory}",
                $"--opt=-o{targetDllPath}",
                projectFile
            },
            workingDirectory: repoRoot,
            captureOutput: false,
            out _,
            out _);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"lazbuild failed for mode {buildMode}.");
        }

        if (!File.Exists(targetDllPath))
        {
            if (!File.Exists(sourceDllPath))
            {
                throw new InvalidOperationException($"Expected build output was not found: {targetDllPath}");
            }

            CopyArtifact(sourceDllPath, targetDllPath);
        }

        EnsureDebugArtifact(sourceDllPath, targetDllPath);
    }

    private static void EnsureDebugArtifact(string sourceDllPath, string targetDllPath)
    {
        var targetDebugPath = Path.ChangeExtension(targetDllPath, ".dbg");
        if (File.Exists(targetDebugPath))
        {
            return;
        }

        var sourceDebugPath = Path.ChangeExtension(sourceDllPath, ".dbg");
        if (!File.Exists(sourceDebugPath))
        {
            return;
        }

        CopyArtifact(sourceDebugPath, targetDebugPath);
    }

    private static void CopyArtifact(string sourcePath, string targetPath)
    {
        var targetDirectory = Path.GetDirectoryName(targetPath);
        if (!string.IsNullOrWhiteSpace(targetDirectory))
        {
            Directory.CreateDirectory(targetDirectory);
        }

        File.Copy(sourcePath, targetPath, overwrite: true);
    }

    private static IReadOnlyList<string> GetExports(string objdumpExe, string dllPath)
    {
        var exitCode = RunProcess(
            objdumpExe,
            new[] { "-p", dllPath },
            workingDirectory: Path.GetDirectoryName(dllPath),
            captureOutput: true,
            out var standardOutput,
            out var standardError);

        if (exitCode != 0)
        {
            throw new InvalidOperationException($"objdump failed for {dllPath}.{Environment.NewLine}{standardError}".Trim());
        }

        var exports = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var insideExportTable = false;

        using var reader = new StringReader(standardOutput);
        while (reader.ReadLine() is { } line)
        {
            if (!insideExportTable)
            {
                if (string.Equals(line.Trim(), "[Ordinal/Name Pointer] Table", StringComparison.Ordinal))
                {
                    insideExportTable = true;
                }

                continue;
            }

            var match = ExportLineRegex.Match(line);
            if (match.Success)
            {
                var name = match.Groups[1].Value.Trim();
                if (name.Length > 0 && seen.Add(name))
                {
                    exports.Add(name);
                }

                continue;
            }

            if (exports.Count > 0 && string.IsNullOrWhiteSpace(line))
            {
                break;
            }
        }

        if (exports.Count == 0)
        {
            throw new InvalidOperationException($"No exported symbols found in {dllPath}.");
        }

        return exports;
    }

    private static void WriteDefFile(string dllPath, string defPath, IEnumerable<string> exports)
    {
        var lines = new List<string>
        {
            $"LIBRARY {Path.GetFileName(dllPath)}",
            "EXPORTS"
        };

        lines.AddRange(exports.Select(exportName => $"    {exportName}"));
        File.WriteAllLines(defPath, lines, Encoding.ASCII);
    }

    private static int RunProcess(
        string fileName,
        IReadOnlyList<string> arguments,
        string? workingDirectory,
        bool captureOutput,
        out string standardOutput,
        out string standardError)
    {
        using var process = new Process();
        process.StartInfo.FileName = fileName;
        process.StartInfo.UseShellExecute = false;
        process.StartInfo.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        process.StartInfo.RedirectStandardOutput = captureOutput;
        process.StartInfo.RedirectStandardError = captureOutput;

        foreach (var argument in arguments)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        standardOutput = captureOutput ? process.StandardOutput.ReadToEnd() : string.Empty;
        standardError = captureOutput ? process.StandardError.ReadToEnd() : string.Empty;
        process.WaitForExit();
        return process.ExitCode;
    }
}