$projectRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Split-Path -Parent $projectRoot
$sourceRoot = Join-Path $repoRoot 'library'

$nameMap = @{
    'addresschangeunit' = 'AddressChangeUnit'
    'addresslist' = 'AddressList'
    'addressparser' = 'AddressParser'
    'assemblerunit' = 'AssemblerUnit'
    'autoassembler' = 'AutoAssembler'
    'byteinterpreter' = 'ByteInterpreter'
    'cedebugger' = 'CeDebugger'
    'cefuncproc' = 'CeFuncProc'
    'customtypehandler' = 'CustomTypeHandler'
    'cvconst' = 'CvConst'
    'dbk64secondaryloader' = 'Dbk64SecondaryLoader'
    'debughelper' = 'DebugHelper'
    'driverlist' = 'DriverList'
    'fileaccess' = 'FileAccess'
    'filehandler' = 'FileHandler'
    'filemapping' = 'FileMapping'
    'foundlisthelper' = 'FoundListHelper'
    'generichotkey' = 'GenericHotkey'
    'groupscancommandparser' = 'GroupScanCommandParser'
    'guisafecriticalsection' = 'GuiSafeCriticalSection'
    'hotkeyhandler' = 'HotkeyHandler'
    'hypermode' = 'HyperMode'
    'manualmoduleloader' = 'ManualModuleLoader'
    'memoryrecorddatabase' = 'MemoryRecordDatabase'
    'memoryrecordunit' = 'MemoryRecordUnit'
    'memscan' = 'MemScan'
    'newkernelhandler' = 'NewKernelHandler'
    'peinfo' = 'PeInfo'
    'peinfofunctions' = 'PeInfoFunctions'
    'peinfounit' = 'PeInfoUnit'
    'processhandlerunit' = 'ProcessHandlerUnit'
    'savedscanhandler' = 'SavedScanHandler'
    'savefirstscan' = 'SaveFirstScan'
    'scanner' = 'Scanner'
    'settings' = 'Settings'
    'symbolhandler' = 'SymbolHandler'
    'symbollisthandler' = 'SymbolListHandler'
    'dbk32functions' = 'Dbk32Functions'
    'debug' = 'Debug'
    'multicpuexecution' = 'MultiCpuExecution'
    'vmxfunctions' = 'VmxFunctions'
}

Get-ChildItem -Path $sourceRoot -Recurse -Filter '*.pas' | ForEach-Object {
    $unitName = [System.IO.Path]::GetFileNameWithoutExtension($_.Name).ToLowerInvariant()
    if (-not $nameMap.ContainsKey($unitName)) {
        throw "No C# name mapping found for $($_.FullName)"
    }

    $typeName = $nameMap[$unitName]
    $relativePath = $_.FullName.Substring($sourceRoot.Length).TrimStart('\')
    $relativeDirectory = [System.IO.Path]::GetDirectoryName($relativePath)
    $targetDirectory = $projectRoot
    $namespace = 'CheatEngine.Library'

    if ($relativeDirectory) {
        foreach ($segment in ($relativeDirectory.Split('\') | Where-Object { $_ })) {
            if ($segment -eq 'dbk32') {
                $targetDirectory = Join-Path $targetDirectory 'Dbk32'
                $namespace += '.Dbk32'
            }
            else {
                $targetDirectory = Join-Path $targetDirectory $segment
                $namespace += '.' + $segment
            }
        }
    }

    if (-not (Test-Path $targetDirectory)) {
        New-Item -ItemType Directory -Path $targetDirectory | Out-Null
    }

    $targetPath = Join-Path $targetDirectory ($typeName + '.cs')
    if (Test-Path $targetPath) {
        return
    }

    $supportReference = if ($namespace -eq 'CheatEngine.Library') { '' } else { "using CheatEngine.Library;`r`n`r`n" }
    $content = $supportReference + @"
namespace $namespace;

public static partial class $typeName
{
    public static void EnsurePorted()
    {
        global::CheatEngine.Library.PascalPorting.NotSupported("$typeName");
    }
}
"@

    [System.IO.File]::WriteAllText($targetPath, $content, [System.Text.UTF8Encoding]::new($false))
}