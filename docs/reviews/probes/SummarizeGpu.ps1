$data = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '../data'))
function Median($Values) {
    $sorted = @($Values | Sort-Object)
    if ($sorted.Count -eq 0) { return $null }
    return $sorted[[int][Math]::Floor($sorted.Count / 2)]
}
$rows = foreach ($file in Get-ChildItem $data -File | Where-Object { $_.Name -match '^2026-09-05-(frame-\d-rep-\d|gbuffer-\d+-(standard|extended)-(rep-\d|long|contexts-\d-long))\.txt$' }) {
    $lines = Get-Content -LiteralPath $file.FullName
    if (-not ($lines -match 'Spectra Engine shut down')) { continue }
    # Reports occur every five seconds after scene load. Discard the first two
    # (warmup); the later reports each describe a 2048-frame rolling window.
    $skip = if ($file.BaseName.EndsWith('-long')) { 15 } else { 2 }
    $samples = @($lines | Where-Object { $_ -match 'CPU frame EMA' } | Select-Object -Skip $skip | ForEach-Object {
        if ($_ -match '(?<frame>[\d.]+) ms/frame .*CPU frame EMA (?<cpu>[\d.]+) ms; frame p50/p95/p99/max (?<p50>[\d.]+)/(?<p95>[\d.]+)/(?<p99>[\d.]+)/(?<max>[\d.]+).*GPU total/shadow/geometry/light/resolve (?<gpu>[\d.]+)/(?<shadow>[\d.]+)/(?<geometry>[\d.]+)/(?<light>[\d.]+)/(?<resolve>[\d.]+)') {
            if ([double]$Matches.gpu -gt 0) {
                [pscustomobject]@{ Frame=[double]$Matches.frame; Cpu=[double]$Matches.cpu; P50=[double]$Matches.p50; P95=[double]$Matches.p95; P99=[double]$Matches.p99; Max=[double]$Matches.max; Gpu=[double]$Matches.gpu; Geometry=[double]$Matches.geometry }
            }
        }
    })
    [pscustomobject]@{
        Run=$file.BaseName.Replace('2026-09-05-',''); Reports=$samples.Count
        Frame=Median $samples.Frame; Cpu=Median $samples.Cpu; P50=Median $samples.P50
        P95=Median $samples.P95; P99=Median $samples.P99
        WindowMax=($samples.Max | Measure-Object -Maximum).Maximum
        Gpu=Median $samples.Gpu; Geometry=Median $samples.Geometry
    }
}
$rows | Export-Csv -NoTypeInformation (Join-Path $data '2026-09-05-gpu-summary.csv')
$rows | Format-Table -AutoSize
