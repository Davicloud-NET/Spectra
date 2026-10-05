using System;
using System.Collections.Generic;
using System.Numerics;
using SoundCornersSpike.Grid;

namespace SoundCornersSpike.Questions;

// What one way of answering made of a walk, frame by frame.
internal readonly record struct WalkStats(
    float LargestJump, float Jump95, int JumpsOver3, float MeanError, float LargestError, float LargestLengthError)
{
    public static readonly string[] Header =
    [
        "largest jump, deg", "95th percentile jump, deg", "jumps over 3 deg", "mean error, deg",
        "largest error, deg", "largest length error",
    ];

    public static WalkStats Of(Walk walk, Vector3[] legs, float[] lengths)
    {
        var jumps = new List<float>();
        float total = 0f, worst = 0f, worstLength = 0f;

        for (int i = 0; i < walk.Frames; i++)
        {
            float error = PathReader.AngleDegrees(legs[i], walk.TrueLegs[i]);
            total += error;
            worst = MathF.Max(worst, error);
            worstLength = MathF.Max(worstLength, MathF.Abs(lengths[i] - walk.TrueLengths[i]) / walk.TrueLengths[i]);

            if (i > 0) jumps.Add(PathReader.AngleDegrees(legs[i - 1], legs[i]));
        }

        jumps.Sort();
        return new WalkStats(
            jumps[^1],
            jumps[(int)(jumps.Count * 0.95f)],
            jumps.FindAll(j => j > 3f).Count,
            total / walk.Frames,
            worst,
            worstLength * 100f);
    }

    public string[] Cells() =>
    [
        Report.F(LargestJump, 1), Report.F(Jump95, 2), JumpsOver3.ToString(), Report.F(MeanError, 1),
        Report.F(LargestError, 1), Report.F(LargestLengthError, 1) + "%",
    ];
}
