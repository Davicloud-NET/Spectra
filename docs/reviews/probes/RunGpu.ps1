param([switch]$SmokeOnly)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$executable = Join-Path $workspace 'SpectraEngine.Executable/bin/Release/net10.0/SpectraEngine.Executable.exe'
$data = Join-Path $workspace 'docs/reviews/data'
$env:DOTNET_TieredCompilation = '0'
function Capture([string]$Name, [string[]]$ExtraArguments, [int]$Seconds = 32, [switch]$Reload) {
    $output = Join-Path $data ('2026-09-05-' + $Name + '.txt')
    $errors = Join-Path $data ('2026-09-05-' + $Name + '.stderr.txt')
    $process = Start-Process -FilePath $executable -WorkingDirectory $workspace -ArgumentList $ExtraArguments -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError $errors -PassThru
    if ($Reload) {
        # Touching the built-in source exercises the real file watcher without changing shader bytes.
        $shader = Join-Path $workspace 'SpectraEngine.Core/Graphics/BaseShaders/GBufferFillCompact.spectrashade'
        $clock = [Diagnostics.Stopwatch]::StartNew()
        while ($clock.Elapsed.TotalSeconds -lt $Seconds -and -not $process.HasExited) {
            if ($process.WaitForExit(2000)) { break }
            [IO.File]::SetLastWriteTimeUtc($shader, [DateTime]::UtcNow)
        }
    } else { $null = $process.WaitForExit($Seconds * 1000) }
    if (-not $process.HasExited) {
        $process.Refresh()
        $closed = $process.CloseMainWindow()
        if (-not $process.WaitForExit(15000)) {
            Stop-Process -Id $process.Id
            throw "Capture $Name did not shut down after WM_CLOSE (sent=$closed)."
        }
    }
    "$Name exit=$($process.ExitCode) duration=${Seconds}s args=$($ExtraArguments -join ' ')" | Add-Content (Join-Path $data '2026-09-05-gpu-runs.txt')
    if ($process.ExitCode -ne 0) { throw "Capture $Name failed; inspect $output and $errors." }
}
if (-not $SmokeOnly) {
    $orders = @(@(1,2,3), @(3,2,1), @(2,1,3))
    for ($rep = 0; $rep -lt 3; $rep++) {
        foreach ($contexts in $orders[$rep]) {
            Capture "frame-$contexts-rep-$rep" @('d3d12','--profile','--uncapped','--debug-layer=false','--selftest=false','--demo-animation=off','--adapter=4070','--size=1920x1080',"--frame-contexts=$contexts")
        }
    }
    foreach ($adapter in @('4070','770')) {
        for ($rep = 0; $rep -lt 2; $rep++) {
            $layouts = if ($rep -eq 0) { @('extended','standard') } else { @('standard','extended') }
            foreach ($layout in $layouts) {
                Capture "gbuffer-$adapter-$layout-rep-$rep" @('d3d12','--profile','--uncapped','--debug-layer=false','--selftest=false','--demo-animation=off',"--adapter=$adapter",'--size=3840x2160',"--gbuffer=$layout")
            }
        }
    }
}
Capture 'final-d3d12-smoke' @('d3d12','--profile','--debug-layer=true','--selftest','--demo-animation=csg','--fullscreen-cycle=2','--adapter=4070','--size=1280x720') -Seconds 18 -Reload
Capture 'final-d3d11-smoke' @('d3d11','--profile','--debug-layer=true','--selftest','--demo-animation=csg','--adapter=4070','--size=1280x720') -Seconds 16
Capture 'final-opengl-smoke' @('opengl','--profile','--selftest','--demo-animation=csg','--size=1280x720') -Seconds 16
foreach ($backend in @('d3d11','d3d12')) {
    & $executable $backend --viewport-compare *> (Join-Path $data "2026-09-05-final-$backend-viewport-compare.txt")
    if ($LASTEXITCODE -ne 0) { throw "$backend viewport comparison failed." }
    & $executable $backend --pacing-probe *> (Join-Path $data "2026-09-05-final-$backend-pacing.txt")
    if ($LASTEXITCODE -ne 0) { throw "$backend pacing probe failed." }
}
