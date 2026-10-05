namespace SpectraEngine.Core.Audio.Acoustics;

/// <summary>
/// What a sound loses by passing through one kind of wall. Getting through
/// the wall at all costs the surface loss, and every metre of it costs the
/// loss per metre on top. Each comes twice: how much quieter the whole sound
/// gets, and how much more than that the high end loses, which is what makes
/// it dull. All four are decibels, so the walls along a path add up.
/// </summary>
public sealed class AcousticPreset
{
    internal AcousticPreset(string name, float surfaceDb, float surfaceHfDb, float perMeterDb, float perMeterHfDb)
    {
        Name = name;
        SurfaceDb = surfaceDb;
        SurfaceHfDb = surfaceHfDb;
        PerMeterDb = perMeterDb;
        PerMeterHfDb = perMeterHfDb;
    }

    /// <summary>The name a material file writes after <c>acoustic =</c>, in lower case.</summary>
    public string Name { get; }

    /// <summary>Decibels the whole sound loses by crossing a wall of this, however thin.</summary>
    public float SurfaceDb { get; }

    /// <summary>Decibels the high end loses at that crossing, beyond <see cref="SurfaceDb"/>.</summary>
    public float SurfaceHfDb { get; }

    /// <summary>Decibels the whole sound loses for each metre of thickness.</summary>
    public float PerMeterDb { get; }

    /// <summary>Decibels the high end loses for each metre, beyond <see cref="PerMeterDb"/>.</summary>
    public float PerMeterHfDb { get; }

    /// <summary>
    /// What one wall of this costs. Add the walls along a path and call
    /// <see cref="AcousticLoss.ToGains"/> on the sum.
    /// </summary>
    /// <param name="thickness">Metres of it the sound goes through. Below zero counts as zero.</param>
    public AcousticLoss LossThrough(float thickness)
    {
        // Also catches NaN, which then costs the surface alone.
        if (!(thickness > 0f)) thickness = 0f;

        return new AcousticLoss(
            SurfaceDb + PerMeterDb * thickness,
            SurfaceHfDb + PerMeterHfDb * thickness);
    }

    /// <summary>The factors the engine applies for one wall of this and nothing else in the way.</summary>
    /// <param name="thickness">Metres of it the sound goes through. Below zero counts as zero.</param>
    public AcousticGains GainsThrough(float thickness) => LossThrough(thickness).ToGains();

    /// <inheritdoc/>
    public override string ToString() => Name;
}
