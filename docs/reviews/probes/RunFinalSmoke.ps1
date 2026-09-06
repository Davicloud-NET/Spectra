$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$data = Join-Path $workspace 'docs/reviews/data'
$shader = Join-Path $workspace 'SpectraEngine.Core/Graphics/BaseShaders/GBufferFillCompact.spectrashade'
$runs = @(
    @{ Name='final-d3d12-props-stress'; Exe='SpectraEngine.Executable/bin/Release/net10.0/SpectraEngine.Executable.exe'; Args=@('d3d12','--profile','--debug-layer=true','--selftest','--demo-animation=csg','--fullscreen-cycle=2','--adapter=4070','--size=1280x720','--props=8000'); Seconds=22; Reload=$true },
    @{ Name='final-nativeaot-smoke'; Exe='SpectraEngine.Executable/bin/Release/net10.0/win-x64/publish/SpectraEngine.Executable.exe'; Args=@('d3d11','--profile','--debug-layer=true','--selftest','--demo-animation=csg','--adapter=4070','--size=1280x720'); Seconds=17; Reload=$false }
)
foreach ($run in $runs) {
    $output = Join-Path $data ('2026-09-05-' + $run.Name + '.txt')
    $process = Start-Process -FilePath (Join-Path $workspace $run.Exe) -WorkingDirectory $workspace -ArgumentList $run.Args -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError ($output + '.stderr') -PassThru
    $clock = [Diagnostics.Stopwatch]::StartNew()
    while ($clock.Elapsed.TotalSeconds -lt $run.Seconds -and -not $process.HasExited) {
        if ($process.WaitForExit(2000)) { break }
        if ($run.Reload) { [IO.File]::SetLastWriteTimeUtc($shader, [DateTime]::UtcNow) }
    }
    if (-not $process.HasExited) {
        $process.Refresh()
        $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id; throw "Smoke $($run.Name) failed to close." }
    }
    $lines = Get-Content -LiteralPath $output
    $passes = @($lines | Select-String 'Editing self-test: PASS').Count
    $errors = @($lines | Select-String '\b(ERR|FTL)\b').Count
    "$($run.Name) exit=$($process.ExitCode) selftest=$passes errors=$errors"
    if ($process.ExitCode -ne 0 -or $passes -lt 2 -or $errors -ne 0) { throw "Smoke $($run.Name) failed; inspect $output." }
}
