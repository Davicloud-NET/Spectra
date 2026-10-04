using Avalonia.Media;
using System;
using System.Numerics;

namespace SpectraEngine.Editor.Shell;

/// <summary>The colour a picker is showing, as hue, saturation and value.</summary>
// HSV is stored, not derived from the colour: black has no hue and grey has
// no saturation, so deriving them would make the markers jump.
public sealed class ColorPickerModel : ObservableObject
{
    private float _hue;
    private float _saturation;
    private float _value = 1f;
    private bool _isMixed;

    /// <summary>Where the picker opens when the selection disagrees.</summary>
    public static Vector3 MixedStart { get; } = new(0.5f, 0.5f, 0.5f);

    /// <summary>Hue in degrees, 0 to 360.</summary>
    public float Hue
    {
        get => _hue;
        private set
        {
            if (Set(ref _hue, value)) RaiseDerived(hueChanged: true);
        }
    }

    /// <summary>Saturation, 0 to 1.</summary>
    public float Saturation
    {
        get => _saturation;
        private set
        {
            if (Set(ref _saturation, value)) RaiseDerived(hueChanged: false);
        }
    }

    /// <summary>Value, 0 to 1.</summary>
    public float Value
    {
        get => _value;
        private set
        {
            if (Set(ref _value, value)) RaiseDerived(hueChanged: false);
        }
    }

    /// <summary>
    /// Whether the picker opened over a selection that disagreed. Cleared by the first movement.
    /// </summary>
    public bool IsMixed
    {
        get => _isMixed;
        private set => Set(ref _isMixed, value);
    }

    /// <summary>The colour as <c>#RRGGBB</c>.</summary>
    public string Hex => ColorMath.ToHex(Srgb);

    /// <summary>The colour, in sRGB.</summary>
    public Vector3 Srgb => ColorMath.HsvToSrgb(_hue, _saturation, _value);

    /// <summary>The colour, linear.</summary>
    public Vector3 Linear => ColorMath.SrgbToLinear(Srgb);

    /// <summary>The fully saturated hue the square is tinted with.</summary>
    public IBrush HueBrush => Brush(ColorMath.HsvToSrgb(_hue, 1f, 1f));

    /// <summary>The colour itself, for the preview swatch.</summary>
    public IBrush PreviewBrush => Brush(Srgb);

    /// <summary>Raised whenever the colour moves, with the linear colour.</summary>
    public event Action<Vector3>? Changed;

    /// <summary>
    /// Opens on a colour. Pass a vector whose X is NaN for a mixed selection.
    /// </summary>
    public void Load(Vector3 linear)
    {
        bool mixed = float.IsNaN(linear.X);
        Vector3 srgb = mixed ? MixedStart : ColorMath.LinearToSrgb(linear);

        (float hue, float saturation, float value) = ColorMath.SrgbToHsv(srgb);
        _hue = hue;
        _saturation = saturation;
        _value = value;

        Raise(nameof(Hue));
        Raise(nameof(Saturation));
        Raise(nameof(Value));
        RaiseDerived(hueChanged: true);

        IsMixed = mixed;
    }

    /// <summary>Moves the square's marker.</summary>
    public void SetSaturationValue(float saturation, float value)
    {
        _saturation = Math.Clamp(saturation, 0f, 1f);
        _value = Math.Clamp(value, 0f, 1f);

        Raise(nameof(Saturation));
        Raise(nameof(Value));
        Moved();
    }

    /// <summary>Moves the hue strip's marker.</summary>
    public void SetHue(float degrees)
    {
        _hue = ((degrees % 360f) + 360f) % 360f;

        Raise(nameof(Hue));
        RaiseDerived(hueChanged: true);
        Moved();
    }

    /// <summary>Takes a typed hex, or refuses it without changing anything.</summary>
    public bool TrySetHex(string? text)
    {
        if (!ColorMath.TryParseHex(text, out Vector3 srgb)) return false;

        (float hue, float saturation, float value) = ColorMath.SrgbToHsv(srgb);

        // A grey has no hue: keep the strip where it was.
        _hue = saturation <= 1e-6f ? _hue : hue;
        _saturation = saturation;
        _value = value;

        Raise(nameof(Hue));
        Raise(nameof(Saturation));
        Raise(nameof(Value));
        RaiseDerived(hueChanged: true);
        Moved();
        return true;
    }

    private void Moved()
    {
        IsMixed = false;
        Changed?.Invoke(Linear);
    }

    private void RaiseDerived(bool hueChanged)
    {
        Raise(nameof(Hex));
        Raise(nameof(Srgb));
        Raise(nameof(Linear));
        Raise(nameof(PreviewBrush));
        if (hueChanged) Raise(nameof(HueBrush));
    }

    private static IBrush Brush(Vector3 srgb) => new SolidColorBrush(Color.FromRgb(
        (byte)Math.Clamp(MathF.Round(srgb.X * 255f), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(srgb.Y * 255f), 0f, 255f),
        (byte)Math.Clamp(MathF.Round(srgb.Z * 255f), 0f, 255f)));
}
