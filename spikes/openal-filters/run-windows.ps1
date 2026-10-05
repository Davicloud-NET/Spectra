<#
.SYNOPSIS
    Runs the spike under the JIT and as a NativeAOT executable, several times
    each, and compares what they print.

.DESCRIPTION
    Nothing is played. Every sound is rendered into memory.

    Output goes to out/jit/ and out/win-x64/aot/, one log per run to
    out/win-x64/logs/. Lines that start with INFO or TIME are left out of the
    comparison: they name the machine and the timings.

    The AOT link step needs vswhere.exe, so the Visual Studio Installer folder
    is put on PATH for this script.
#>
[CmdletBinding()]
param(
    [int]$Runs = 3
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'OpenAlFilterSpike/OpenAlFilterSpike.csproj'
$out = Join-Path $root 'out'
$logs = Join-Path $out 'win-x64/logs'
$env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH

New-Item -ItemType Directory -Force -Path $logs | Out-Null
Remove-Item (Join-Path $logs '*.txt') -ErrorAction SilentlyContinue

Write-Host 'Building for the JIT'
$log = & dotnet build $project -c Release -o (Join-Path $out 'jit') --disable-build-servers -nologo -v:m
if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Build failed ($LASTEXITCODE)." }

Write-Host 'Publishing with NativeAOT'
# A second publish would reuse the object file. The IL compiler, which prints
# the AOT warnings, would not run.
Remove-Item (Join-Path $root 'OpenAlFilterSpike/obj/Release/net10.0/win-x64/native') -Recurse -Force -ErrorAction SilentlyContinue
$log = & dotnet publish $project -c Release -r win-x64 -o (Join-Path $out 'win-x64/aot') --disable-build-servers -nologo -v:m
if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Publish failed ($LASTEXITCODE)." }
$warnings = @($log | Where-Object { $_ -match 'warning' })
Write-Host "Publish warnings: $($warnings.Count)"
$warnings | ForEach-Object { ($_ -replace '\[.*\]$', '').Trim() } | Out-Host

$builds = @(
    @{ Name = 'jit'; Exe = Join-Path $out 'jit/OpenAlFilterSpike.exe' }
    @{ Name = 'aot'; Exe = Join-Path $out 'win-x64/aot/OpenAlFilterSpike.exe' }
)

foreach ($build in $builds) {
    foreach ($run in 1..$Runs) {
        $file = Join-Path $logs "$($build.Name)-$run.txt"
        # stderr is left alone: check 1g makes OpenAL Soft print one line there.
        & $build.Exe | Set-Content -Path $file -Encoding utf8
        Write-Host "$($build.Name) run ${run}: exit code $LASTEXITCODE"
    }
}

function Get-Stable([string]$Path) {
    Get-Content $Path | Where-Object { $_ -notmatch '^(INFO|TIME)' }
}

$first = Get-ChildItem $logs -Filter '*.txt' | Sort-Object Name | Select-Object -First 1
$reference = Get-Stable $first.FullName
foreach ($file in Get-ChildItem $logs -Filter '*.txt' | Sort-Object Name) {
    $same = -not (Compare-Object $reference (Get-Stable $file.FullName) -CaseSensitive)
    Write-Host ("{0,-10} {1}" -f $file.BaseName, $(if ($same) { "same as $($first.BaseName)" } else { "DIFFERS from $($first.BaseName)" }))
}

Write-Host ''
Write-Host "== $(Join-Path $logs 'aot-1.txt')"
Get-Content (Join-Path $logs 'aot-1.txt') | Out-Host
