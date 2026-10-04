using SpectraEngine.Core.Graphics.Shaders;
using System;
using System.Collections.Generic;

namespace SpectraEngine.Core.Graphics;

/// <summary>
/// One attribute of a vertex layout. There is one per location, not per shader
/// field: a <c>mat4</c> is four attributes.
/// </summary>
public readonly struct VertexAttribute
{
    /// <summary>The per-vertex buffer's slot.</summary>
    public const uint VertexSlot = 0;

    /// <summary>The per-instance buffer's slot.</summary>
    public const uint InstanceSlot = 1;

    public uint Location { get; }
    public uint ComponentCount { get; }

    /// <summary>
    /// Which bound buffer this attribute reads from:
    /// <see cref="VertexSlot"/> or <see cref="InstanceSlot"/>.
    /// </summary>
    public uint InputSlot { get; }

    public VertexInputRate InputRate { get; }

    public VertexAttribute(
        uint location,
        uint componentCount,
        uint inputSlot = VertexSlot,
        VertexInputRate inputRate = VertexInputRate.PerVertex)
    {
        Location = location;
        ComponentCount = componentCount;
        InputSlot = inputSlot;
        InputRate = inputRate;
    }

    private static readonly VertexAttribute[] _standardLayout =
    [
        new(location: 0, componentCount: 3),
        new(location: 1, componentCount: 3),
        new(location: 2, componentCount: 2),
    ];

    /// <summary>
    /// The engine's standard interleaved vertex layout (8 floats): position (3),
    /// normal (3), uv (2).
    /// </summary>
    public static ReadOnlySpan<VertexAttribute> StandardLayout => _standardLayout;

    private static readonly VertexAttribute[] _standardInstanceLayout =
    [
        new(location: 3, componentCount: 4, InstanceSlot, VertexInputRate.PerInstance),
        new(location: 4, componentCount: 4, InstanceSlot, VertexInputRate.PerInstance),
        new(location: 5, componentCount: 4, InstanceSlot, VertexInputRate.PerInstance),
        new(location: 6, componentCount: 4, InstanceSlot, VertexInputRate.PerInstance),
    ];

    /// <summary>
    /// The engine's standard per-instance layout: one <c>mat4</c> world matrix
    /// at locations 3 through 6, directly after <see cref="StandardLayout"/>.
    /// </summary>
    public static ReadOnlySpan<VertexAttribute> StandardInstanceLayout => _standardInstanceLayout;

    /// <summary>Floats per instance in <see cref="StandardInstanceLayout"/>.</summary>
    public const int StandardInstanceFloats = 16;

    /// <summary>
    /// The subset of <paramref name="attributes"/> bound to
    /// <paramref name="slot"/>, in order.
    /// </summary>
    public static VertexAttribute[] ForSlot(ReadOnlySpan<VertexAttribute> attributes, uint slot)
    {
        int count = 0;
        for (int i = 0; i < attributes.Length; i++)
            if (attributes[i].InputSlot == slot)
                count++;

        var result = new VertexAttribute[count];
        int next = 0;
        for (int i = 0; i < attributes.Length; i++)
            if (attributes[i].InputSlot == slot)
                result[next++] = attributes[i];

        return result;
    }

    /// <summary>
    /// Expands a compiled shader's vertex inputs into one attribute per location.
    /// Per-vertex inputs go to <see cref="VertexSlot"/>, per-instance ones to
    /// <see cref="InstanceSlot"/>.
    /// </summary>
    public static VertexAttribute[] FromShaderInputs(IReadOnlyList<VertexInputElement> inputs)
    {
        ArgumentNullException.ThrowIfNull(inputs);

        int total = 0;
        for (int i = 0; i < inputs.Count; i++)
            total += (int)inputs[i].LocationSpan;

        var attributes = new VertexAttribute[total];
        int next = 0;
        for (int i = 0; i < inputs.Count; i++)
        {
            VertexInputElement input = inputs[i];
            bool perInstance = input.Rate == VertexInputRate.PerInstance;

            for (uint row = 0; row < input.LocationSpan; row++)
            {
                attributes[next++] = new VertexAttribute(
                    input.Location + row,
                    input.ComponentCount,
                    perInstance ? InstanceSlot : VertexSlot,
                    input.Rate);
            }
        }

        return attributes;
    }
}
