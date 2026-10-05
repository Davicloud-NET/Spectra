<#
.SYNOPSIS
    Fetches Luau at the tag in luau.pin and builds it for the spike.

.DESCRIPTION
    The checkout goes to .luau/src and the build output to build/<rid>/. Both
    are git-ignored. No Developer prompt is needed: CMake finds MSVC itself.

    Three builds come out of one run:
      build/<rid>/longjmp/lib      static libraries, errors raised with longjmp
      build/<rid>/cxx/lib          static libraries, errors raised with C++ exceptions
      build/<rid>/longjmp/shared   luau.dll, everything in one library

.PARAMETER Clean
    Delete the CMake build trees first.
#>
[CmdletBinding()]
param(
    [ValidateSet('win-x64', 'win-arm64')]
    [string]$Rid = 'win-x64',

    [switch]$Clean
)

$ErrorActionPreference = 'Stop'

$root = $PSScriptRoot
$tag = (Get-Content (Join-Path $root 'luau.pin') -TotalCount 1).Trim()
$source = Join-Path $root '.luau/src'
$arch = if ($Rid -eq 'win-arm64') { 'ARM64' } else { 'x64' }

if (-not (Test-Path (Join-Path $source 'VM/include/lua.h'))) {
    Write-Host "Fetching Luau $tag"
    & git clone --quiet --depth 1 --branch $tag -c advice.detachedHead=false https://github.com/luau-lang/luau $source
    if ($LASTEXITCODE -ne 0) { throw "git clone failed ($LASTEXITCODE)." }
}

Push-Location $source
try {
    $have = (& git describe --tags --exact-match 2>$null)
    $commit = (& git rev-parse HEAD).Trim()
}
finally { Pop-Location }
if ($have -ne $tag) { throw "The checkout at '$source' is at '$have', luau.pin says '$tag'. Delete .luau and run again." }

function Build-Variant([string]$Unwind, [bool]$Shared) {
    $kind = if ($Shared) { 'shared' } else { 'lib' }
    $buildDir = Join-Path $root "build/$Rid/$Unwind/cmake-$kind"
    $outDir = Join-Path $root "build/$Rid/$Unwind/$kind"

    if ($Clean -and (Test-Path $buildDir)) { Remove-Item -Recurse -Force $buildDir }

    $sharedFlag = if ($Shared) { 'ON' } else { 'OFF' }
    Write-Host "Building Luau ($Rid, $Unwind, $kind)"
    $log = & cmake -S (Join-Path $root 'native') -B $buildDir -A $arch "-DLUAU_SOURCE_DIR=$source" "-DSPIKE_UNWIND=$Unwind" "-DSPIKE_SHARED=$sharedFlag" 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "CMake configure failed ($LASTEXITCODE)." }

    $log = & cmake --build $buildDir --config Release -- /v:m /nologo /nr:false 2>&1
    if ($LASTEXITCODE -ne 0) { $log | Out-Host; throw "CMake build failed ($LASTEXITCODE)." }
    $log | Where-Object { $_ -match 'warning' } | Out-Host

    if (Test-Path $outDir) { Remove-Item -Recurse -Force $outDir }
    New-Item -ItemType Directory -Force -Path $outDir | Out-Null

    $pattern = if ($Shared) { 'luau.dll' } else { '*.lib' }
    Get-ChildItem -Path $buildDir -Recurse -Filter $pattern |
        Where-Object { $_.FullName -like '*\Release\*' } |
        Copy-Item -Destination $outDir -Force

    Get-ChildItem $outDir | ForEach-Object { '{0,12:N0}  {1}' -f $_.Length, $_.Name } | Out-Host
}

Build-Variant 'longjmp' $false
Build-Variant 'cxx' $false
Build-Variant 'longjmp' $true

Write-Host ""
Write-Host "Luau $tag at $commit"
Write-Host "cmake $((& cmake --version | Select-Object -First 1) -replace 'cmake version ', '')"
