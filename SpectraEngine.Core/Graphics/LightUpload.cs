using System;
using System.Numerics;

namespace SpectraEngine.Core.Graphics;

/// <summary>Pushes a frame's lights into a shader program or a full-screen pass.</summary>
public static class LightUpload
{
    /// <summary>Ambient colour for a surface facing straight up.</summary>
    // Sky and ground average to a luminance of one, so ambient strength keeps
    // its scale.
    public static Vector3 AmbientSky { get; set; } = new(0.92f, 1.06f, 1.40f);

    /// <summary>Ambient colour for a surface facing straight down.</summary>
    public static Vector3 AmbientGround { get; set; } = new(1.01f, 0.93f, 0.84f);

    // Reused so the draw path does not allocate.
    [ThreadStatic] private static Vector4[]? _positions;
    [ThreadStatic] private static Vector4[]? _colors;
    [ThreadStatic] private static Vector4[]? _axes;
    [ThreadStatic] private static Vector4[]? _tangents;

    /// <summary>
    /// Writes the view's lights and ambient level into <paramref name="shader"/>.
    /// Safe on a shader that declares none of the light uniforms.
    /// </summary>
    public static void Apply(ShaderProgram shader, RenderView view, float ambient)
        => Apply(shader, view, ambient, AmbientSky, AmbientGround);

    /// <inheritdoc cref="Apply(ShaderProgram, RenderView, float)"/>
    public static void Apply(
        ShaderProgram shader, RenderView view, float ambient, Vector3 sky, Vector3 ground)
    {
        Packed packed = Fill(view);
        shader.SetUniform("uAmbientSky", sky);
        shader.SetUniform("uAmbientGround", ground);

        shader.SetUniform("uLightPositions", packed.Positions);
        shader.SetUniform("uLightColors", packed.Colors);
        shader.SetUniform("uLightAxis", packed.Axes);
        shader.SetUniform("uLightTangent", packed.Tangents);
        shader.SetUniform("uLightCount", packed.Count);
        shader.SetUniform("uAmbient", ambient);
    }

    /// <summary>The same upload, staged into a full-screen pass.</summary>
    public static void Apply(PostPass pass, RenderView view, float ambient)
        => Apply(pass, view, ambient, AmbientSky, AmbientGround);

    /// <inheritdoc cref="Apply(PostPass, RenderView, float)"/>
    public static void Apply(
        PostPass pass, RenderView view, float ambient, Vector3 sky, Vector3 ground)
    {
        // PostPass copies the arrays; they are shared scratch.
        Packed packed = Fill(view);
        pass.SetUniform("uAmbientSky", sky);
        pass.SetUniform("uAmbientGround", ground);

        pass.SetUniform("uLightPositions", packed.Positions.AsSpan());
        pass.SetUniform("uLightColors", packed.Colors.AsSpan());
        pass.SetUniform("uLightAxis", packed.Axes.AsSpan());
        pass.SetUniform("uLightTangent", packed.Tangents.AsSpan());
        pass.SetUniform("uLightCount", packed.Count);
        pass.SetUniform("uAmbient", ambient);
    }

    // An array uniform must be written whole, so slots past the count are zeroed.
    private readonly record struct Packed(
        Vector4[] Positions, Vector4[] Colors, Vector4[] Axes, Vector4[] Tangents, int Count);

    private static Packed Fill(RenderView view)
    {
        Vector4[] positions = _positions ??= new Vector4[RenderView.MaxLights];
        Vector4[] colors = _colors ??= new Vector4[RenderView.MaxLights];
        Vector4[] axes = _axes ??= new Vector4[RenderView.MaxLights];
        Vector4[] tangents = _tangents ??= new Vector4[RenderView.MaxLights];

        ReadOnlySpan<RenderLight> lights = view.Lights;
        for (int i = 0; i < RenderView.MaxLights; i++)
        {
            positions[i] = i < lights.Length ? lights[i].PositionRange : default;
            colors[i] = i < lights.Length ? lights[i].ColorIntensity : default;
            axes[i] = i < lights.Length ? lights[i].Axis : default;
            tangents[i] = i < lights.Length ? lights[i].Tangent : default;
        }

        return new Packed(positions, colors, axes, tangents, lights.Length);
    }
}
