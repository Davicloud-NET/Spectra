<#
.SYNOPSIS
    Builds Luau and its shim from the external/luau submodule.

.DESCRIPTION
    Two files come out. Neither is committed.

      native/runtimes/<rid>/native/spectra_luau.dll   for dotnet run and the test suites
      native/runtimes/<rid>/static/spectra_luau.lib   linked into a NativeAOT publish

    Each holds the shim (native/luau) and the parts of Luau it needs: the VM and
    the compiler, no code generator. Errors are raised by longjmp, a vector is
    three floats, and the C runtime is the static one (/MT). The options live in
    native/luau/CMakeLists.txt.

    No Developer prompt is needed. CMake finds MSVC itself.

.PARAMETER Rid
    The .NET runtime identifier to build for.

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

$repoRoot = Split-Path -Parent $PSScriptRoot
$source = Join-Path $repoRoot 'external/luau'
$shim = Join-Path $PSScriptRoot 'luau'
$buildRoot = Join-Path $repoRoot "build/luau-$Rid"
$runtime = Join-Path $PSScriptRoot "runtimes/$Rid"
$arch = if ($Rid -eq 'win-arm64') { 'ARM64' } else { 'x64' }

if (-not (Test-Path (Join-Path $source 'VM/include/lua.h'))) {
    throw "Luau sources are missing at '$source'. Run: git submodule update --init --recursive"
}

function Get-SubmoduleTag {
    # With no tag git answers on stderr, which 'Stop' turns into an error.
    $ErrorActionPreference = 'Continue'
    & git -C $source describe --tags --exact-match 2>$null
}

$header = Get-Content (Join-Path $shim 'spectra_luau.h') -Raw
if ($header -notmatch '#define\s+SL_LUAU_RELEASE\s+(\d+)') {
    throw "native/luau/spectra_luau.h does not define SL_LUAU_RELEASE."
}
$release = "0.$($Matches[1])"

# A shallow checkout has no tags. The check then waits for a machine that has them.
$tag = Get-SubmoduleTag
if ($tag -and $tag -ne $release) {
    throw "external/luau is at $tag and native/luau/spectra_luau.h says $release. Change SL_LUAU_RELEASE there and Lua.Release in SpectraEngine.Scripting.Luau."
}

function Build-Library([string]$Kind, [string]$File, [string]$Destination) {
    $buildDir = Join-Path $buildRoot $Kind
    if ($Clean -and (Test-Path $buildDir)) { Remove-Item -Recurse -Force $buildDir }

    $shared = if ($Kind -eq 'shared') { 'ON' } else { 'OFF' }

    # Out-Host: what a command prints inside a function is otherwise its result.
    Write-Host "Configuring Luau ($Rid, $Kind)"
    & cmake -S $shim -B $buildDir -A $arch "-DLUAU_SOURCE_DIR=$source" "-DSPECTRA_LUAU_SHARED=$shared" | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "CMake configure failed ($LASTEXITCODE)." }

    Write-Host "Building Luau ($Rid, $Kind)"
    & cmake --build $buildDir --config Release --target spectra_luau -- /v:m /nologo | Out-Host
    if ($LASTEXITCODE -ne 0) { throw "CMake build failed ($LASTEXITCODE)." }

    $built = Join-Path $buildDir "Release/$File"
    if (-not (Test-Path $built)) { throw "The build reported success and left no '$built'." }

    New-Item -ItemType Directory -Force -Path $Destination | Out-Null
    Copy-Item -Path $built -Destination $Destination -Force
    Get-Item (Join-Path $Destination $File)
}

$dll = Build-Library 'shared' 'spectra_luau.dll' (Join-Path $runtime 'native')
$lib = Build-Library 'static' 'spectra_luau.lib' (Join-Path $runtime 'static')

$pinned = (& git -C $source rev-parse HEAD).Trim()
Write-Host ""
Write-Host ("{0,12:N0}  {1}" -f $dll.Length, $dll.FullName)
Write-Host ("{0,12:N0}  {1}" -f $lib.Length, $lib.FullName)
Write-Host "Luau $release, pinned at $pinned"
