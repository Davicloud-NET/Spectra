namespace SoundCornersSpike;

// One timing: the median, and how far three rounds of it lay apart.
internal readonly record struct Timed(double MedianMs, double SpreadPercent, double LowMs, double HighMs)
{
    public string Ms => MedianMs.ToString(MedianMs < 1.0 ? "0.000" : MedianMs < 10.0 ? "0.00" : "0.0");

    public string Spread => SpreadPercent.ToString("0") + "%";
}
