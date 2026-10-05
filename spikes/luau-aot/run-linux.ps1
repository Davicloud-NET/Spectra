<#
.SYNOPSIS
    Publishes every variant of the spike for linux-x64 and runs the checks in WSL.

.DESCRIPTION
    Run build-luau.ps1 and then build-luau.sh (in WSL) first.

    The WSL distro has a .NET runtime but no SDK, so the publish runs on
    Windows. The IL compiler can target Linux from Windows. What it cannot do
    is link, so cross/clang.cmd and cross/objcopy.cmd hand the link and strip
    steps to the real tools in WSL. DisableUnsupportedError turns off the
    SDK's "Cross-OS native compilation is not supported" check.

    On a Linux machine with the SDK none of this is needed:
        dotnet publish LuauSpike -c Release -r linux-x64 -p:LuauLink=static-full
#>
[CmdletBinding()]
param(
    [string]$Distro = 'Ubuntu',
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'LuauSpike/LuauSpike.csproj'
$out = Join-Path $root 'out/linux-x64'
$logs = Join-Path $out 'logs'
$env:PATH = (Join-Path $root 'cross') + ';' + $env:PATH

$variants = @(
    @{ Link = 'static-full';     Unwind = 'longjmp' }
    @{ Link = 'static-full';     Unwind = 'cxx' }
    @{ Link = 'static-compiler'; Unwind = 'longjmp' }
    @{ Link = 'static-vm';       Unwind = 'longjmp' }
    @{ Link = 'shared';          Unwind = 'longjmp' }
)

function ConvertTo-WslPath([string]$Path) {
    $full = [IO.Path]::GetFullPath($Path)
    '/mnt/' + $full.Substring(0, 1).ToLowerInvariant() + $full.Substring(2).Replace('\', '/')
}

New-Item -ItemType Directory -Force -Path $logs | Out-Null

if (-not $SkipPublish) {
    foreach ($v in $variants) {
        $name = "$($v.Link)-$($v.Unwind)"
        Write-Host "Publishing $name for linux-x64"
        $log = & dotnet publish $project -c Release -r linux-x64 "-p:LuauLink=$($v.Link)" "-p:LuauUnwind=$($v.Unwind)" `
            -p:DisableUnsupportedError=true -o (Join-Path $out $name) --disable-build-servers -nologo -v:m 2>&1
        if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Publish of $name failed ($LASTEXITCODE)." }
        $log | Where-Object { $_ -match 'warning' } | Out-Host
    }
}

function Invoke-Checks([string]$Name, [string]$Arguments) {
    $dir = ConvertTo-WslPath (Join-Path $out $Name)
    $log = Join-Path $logs "$Name.txt"
    Write-Host ""
    Write-Host "== $Name"
    # Copied to the Linux filesystem first: a binary on /mnt runs, but starts slowly.
    $command = "rm -rf /tmp/spectra-luau-run && cp -r '$dir' /tmp/spectra-luau-run && chmod +x /tmp/spectra-luau-run/LuauSpike && /tmp/spectra-luau-run/LuauSpike $Arguments"
    & wsl -d $Distro -- bash -c $command 2>&1 | Tee-Object -FilePath $log | Out-Host
    Write-Host "exit code $LASTEXITCODE"
}

Invoke-Checks 'static-full-longjmp' ''
Invoke-Checks 'static-full-cxx' ''
Invoke-Checks 'static-compiler-longjmp' ''

$bytecode = ConvertTo-WslPath (Join-Path $out 'bytecode')
$full = ConvertTo-WslPath (Join-Path $out 'static-full-longjmp')
& wsl -d $Distro -- bash -c "chmod +x '$full/LuauSpike' && '$full/LuauSpike' --emit-bytecode='$bytecode'" | Out-Null
Invoke-Checks 'static-vm-longjmp' "--scripts='$bytecode'"

Invoke-Checks 'shared-longjmp' ''

Write-Host ""
Write-Host "== sizes"
foreach ($v in $variants) {
    $name = "$($v.Link)-$($v.Unwind)"
    $exe = Get-Item (Join-Path $out "$name/LuauSpike")
    '{0,-26} LuauSpike     {1,10:N0} B' -f $name, $exe.Length | Out-Host
}
$so = Get-Item (Join-Path $out 'shared-longjmp/libluau.so')
'{0,-26} libluau.so    {1,10:N0} B' -f 'shared-longjmp', $so.Length | Out-Host

Write-Host ""
Write-Host "== shared library dependencies of the static build"
& wsl -d $Distro -- bash -c "ldd '$full/LuauSpike'; rm -rf /tmp/spectra-luau-run" | Out-Host
