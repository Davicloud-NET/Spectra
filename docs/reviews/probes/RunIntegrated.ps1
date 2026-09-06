param([ValidateRange(1,3)][int]$FrameContexts = 2)
$ErrorActionPreference = 'Stop'
$workspace = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../../..'))
$executable = Join-Path $workspace 'SpectraEngine.Executable/bin/Release/net10.0/SpectraEngine.Executable.exe'
$data = Join-Path $workspace 'docs/reviews/data'
$env:DOTNET_TieredCompilation = '0'
foreach ($layout in @('standard','extended')) {
    $suffix = if ($FrameContexts -eq 2) { 'long' } else { "contexts-$FrameContexts-long" }
    $output = Join-Path $data "2026-09-05-gbuffer-770-$layout-$suffix.txt"
    $arguments = @('d3d12','--profile','--uncapped','--debug-layer=false','--selftest=false','--demo-animation=off','--adapter=770','--size=3840x2160',"--gbuffer=$layout", "--frame-contexts=$FrameContexts")
    $process = Start-Process -FilePath $executable -WorkingDirectory $workspace -ArgumentList $arguments -WindowStyle Hidden -RedirectStandardOutput $output -RedirectStandardError ($output + '.stderr') -PassThru
    for ($i = 0; $i -lt 3; $i++) { if ($process.WaitForExit(34000)) { break } }
    if (-not $process.HasExited) {
        $process.Refresh(); $null = $process.CloseMainWindow()
        if (-not $process.WaitForExit(15000)) { Stop-Process -Id $process.Id; throw 'Integrated capture failed to close.' }
    }
    "gbuffer-770-$layout-$suffix exit=$($process.ExitCode) duration=102s warmup=75s" | Add-Content (Join-Path $data '2026-09-05-gpu-runs.txt')
    if ($process.ExitCode -ne 0) { throw 'Integrated capture failed.' }
}
