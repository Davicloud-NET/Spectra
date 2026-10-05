<#
.SYNOPSIS
    Publishes every variant of the spike with NativeAOT and runs the checks.

.DESCRIPTION
    Run build-luau.ps1 first. Output goes to out/win-x64/<variant>/, with one
    log per variant under out/win-x64/logs/.

    The AOT link step needs vswhere.exe, so the Visual Studio Installer folder
    is put on PATH for this script.
#>
[CmdletBinding()]
param(
    [switch]$SkipPublish
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$project = Join-Path $root 'LuauSpike/LuauSpike.csproj'
$out = Join-Path $root 'out/win-x64'
$logs = Join-Path $out 'logs'
$env:PATH = 'C:\Program Files (x86)\Microsoft Visual Studio\Installer;' + $env:PATH

$variants = @(
    @{ Link = 'static-full';     Unwind = 'longjmp' }
    @{ Link = 'static-full';     Unwind = 'cxx' }
    @{ Link = 'static-compiler'; Unwind = 'longjmp' }
    @{ Link = 'static-vm';       Unwind = 'longjmp' }
    @{ Link = 'shared';          Unwind = 'longjmp' }
)

New-Item -ItemType Directory -Force -Path $logs | Out-Null

if (-not $SkipPublish) {
    foreach ($v in $variants) {
        $name = "$($v.Link)-$($v.Unwind)"
        Write-Host "Publishing $name"
        $log = & dotnet publish $project -c Release -r win-x64 "-p:LuauLink=$($v.Link)" "-p:LuauUnwind=$($v.Unwind)" -o (Join-Path $out $name) --disable-build-servers -nologo -v:m 2>&1
        if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "Publish of $name failed ($LASTEXITCODE)." }
        $log | Where-Object { $_ -match 'warning' } | Out-Host
    }
}

function Invoke-Checks([string]$Name, [string[]]$Arguments) {
    $exe = Join-Path $out "$Name/LuauSpike.exe"
    $log = Join-Path $logs "$Name.txt"
    Write-Host ""
    Write-Host "== $Name"
    & $exe @Arguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    Write-Host "exit code $LASTEXITCODE"
}

Invoke-Checks 'static-full-longjmp' @()
Invoke-Checks 'static-full-cxx' @()
Invoke-Checks 'static-compiler-longjmp' @()

# The VM-only build cannot compile, so the full build writes the bytecode it runs.
$bytecode = Join-Path $out 'bytecode'
& (Join-Path $out 'static-full-longjmp/LuauSpike.exe') "--emit-bytecode=$bytecode" | Out-Null
Invoke-Checks 'static-vm-longjmp' @("--scripts=$bytecode")

Invoke-Checks 'shared-longjmp' @()

Write-Host ""
Write-Host "== sizes"
foreach ($v in $variants) {
    $name = "$($v.Link)-$($v.Unwind)"
    $exe = Get-Item (Join-Path $out "$name/LuauSpike.exe")
    '{0,-26} LuauSpike.exe {1,10:N0} B' -f $name, $exe.Length | Out-Host
}
$dll = Get-Item (Join-Path $out 'shared-longjmp/luau.dll')
'{0,-26} luau.dll      {1,10:N0} B' -f 'shared-longjmp', $dll.Length | Out-Host
