<#
.SYNOPSIS
    Builds the spike in Release and runs it.

.DESCRIPTION
    Nothing is played and no window opens. The worlds are built in code.

    With no arguments every section runs, three times over, and each run's
    output goes to out/run-<n>.txt. Lines that start with INFO name the
    machine. Pass section names to run only those, once, to the console:

        spikes/sound-corners/run.ps1 accuracy doors

    The sections: cells, accuracy, cost, outdoors, direction, staleness,
    sounds, doors, breaks, threads. One more runs only when named: dump,
    which prints the hand cases' paths corner by corner.

    Arguments that start with two dashes go to the program as they are:
    --scale=0.3 takes fewer timing runs, --chosen=8 picks another flood for
    the sections that try only one, --no-pin leaves the process unpinned.

    -Aot publishes a NativeAOT executable and runs that. The link step needs
    vswhere.exe, so the Visual Studio Installer folder is put on PATH.
#>
[CmdletBinding(PositionalBinding = $false)]
param(
    [Parameter(Position = 0, ValueFromRemainingArguments = $true)]
    [string[]]$Sections,
    [int]$Runs = 3,
    [switch]$Aot
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'SoundCornersSpike/SoundCornersSpike.csproj'
$out = Join-Path $root 'out'

if ($Aot) {
    $bin = Join-Path $out 'aot'
    $env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH
    $log = & dotnet publish $project -c Release -r win-x64 -p:SpikeAot=true -o $bin --disable-build-servers -nologo -v:q
    if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Publish failed ($LASTEXITCODE)." }
}
else {
    $bin = Join-Path $out 'bin'
    $log = & dotnet build $project -c Release -o $bin --disable-build-servers -nologo -v:q
    if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Build failed ($LASTEXITCODE)." }
}

$exe = Join-Path $bin 'SoundCornersSpike.exe'

if ($Sections) {
    & $exe @Sections
    exit $LASTEXITCODE
}

$name = if ($Aot) { 'aot' } else { 'run' }
foreach ($run in 1..$Runs) {
    $file = Join-Path $out "$name-$run.txt"
    & $exe | Set-Content -Path $file -Encoding utf8
    Write-Host "run ${run}: exit code $LASTEXITCODE, $file"
}
