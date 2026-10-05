<#
.SYNOPSIS
    Runs the spike in WSL, under the JIT and as a NativeAOT executable, and
    compares what it prints with the Windows runs.

.DESCRIPTION
    Run run-windows.ps1 first. It makes out/jit/, which is the same build for
    both systems: the managed files and OpenAL Soft for each of them under
    runtimes/.

    The WSL distro has a .NET runtime at ~/spectra-dotnet10 and no SDK, so
    nothing is built there. The NativeAOT executable is compiled on Windows
    and linked by clang in WSL through the wrappers in ../luau-aot/cross, as
    the Luau spike did. -SkipAot leaves that out.

    run-in-wsl.sh copies both builds to ~/spectra-openal-spike, runs them
    there and removes the folder again.

    On a Linux machine with the SDK none of this is needed:
        dotnet publish OpenAlFilterSpike -c Release -r linux-x64
#>
[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu',
    [int]$Runs = 3,
    [switch]$SkipAot
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'OpenAlFilterSpike/OpenAlFilterSpike.csproj'
$out = Join-Path $root 'out'
$logs = Join-Path $out 'linux-x64/logs'

function ConvertTo-WslPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    '/mnt/' + $full.Substring(0, 1).ToLowerInvariant() + $full.Substring(2).Replace('\', '/')
}

if (-not (Test-Path (Join-Path $out 'jit/OpenAlFilterSpike.dll'))) {
    throw 'out/jit is missing. Run run-windows.ps1 first.'
}

if (-not $SkipAot) {
    $env:PATH = (Join-Path $root '../luau-aot/cross') + ';' + $env:PATH
    Write-Host 'Publishing with NativeAOT for linux-x64'
    # So the IL compiler runs again and prints its warnings.
    Remove-Item (Join-Path $root 'OpenAlFilterSpike/obj/Release/net10.0/linux-x64/native') -Recurse -Force -ErrorAction SilentlyContinue
    $log = & dotnet publish $project -c Release -r linux-x64 -p:DisableUnsupportedError=true `
        -o (Join-Path $out 'linux-x64/aot') --disable-build-servers -nologo -v:m
    if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Publish failed ($LASTEXITCODE)." }
    $warnings = @($log | Where-Object { $_ -match 'warning' })
    Write-Host "Publish warnings: $($warnings.Count)"
    $warnings | ForEach-Object { ($_ -replace '\[.*\]$', '').Trim() } | Out-Host
}

& wsl -d $Distro -- bash (ConvertTo-WslPath (Join-Path $root 'run-in-wsl.sh')) (ConvertTo-WslPath $out) $Runs | Out-Host

function Get-Stable([string]$Path) {
    Get-Content $Path | Where-Object { $_ -notmatch '^(INFO|TIME)' }
}

$windows = Join-Path $out 'win-x64/logs/aot-1.txt'
$reference = Get-Stable $windows
foreach ($file in Get-ChildItem $logs -Filter '*.txt' | Sort-Object Name) {
    $same = -not (Compare-Object $reference (Get-Stable $file.FullName) -CaseSensitive)
    Write-Host ("linux {0,-8} {1}" -f $file.BaseName, $(if ($same) { 'same as the Windows aot-1' } else { 'DIFFERS from the Windows aot-1' }))
}

Write-Host ''
$shown = Get-ChildItem $logs -Filter '*.txt' | Sort-Object Name | Select-Object -First 1
Write-Host "== $($shown.FullName), the lines that differ by machine"
Get-Content $shown.FullName | Where-Object { $_ -match '^(INFO|TIME)' } | Out-Host
