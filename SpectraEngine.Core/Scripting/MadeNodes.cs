using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Scene;
using System;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// The nodes a script can make from a class name alone: what needs no file
/// and no other data. A mesh needs a model, a script a file and an entity
/// its brush and keyvalues, so those are cloned from a node of the level.
/// </summary>
public static class MadeNodes
{
    /// <summary>The length of each side of a new Part, in units.</summary>
    public const float PartSize = 1f;

    /// <summary>How far a new point light reaches, as in Roblox.</summary>
    public const float PointLightRange = 8f;

    /// <summary>How far a new spot, surface or disc light reaches, as a new spot light in Roblox.</summary>
    public const float BeamLightRange = 16f;

    /// <summary>The classes that can be made, as a sentence names them.</summary>
    public const string Classes =
        "a Part, a Folder, a PointLight, a SpotLight, a SurfaceLight, a DirectionalLight or a DiscLight";

    // Never on a node. Each new part gets a copy, which shares its arrays.
    private static readonly Brush UnitCube = Brush.CreateBox(new Vector3(-PartSize / 2f), new Vector3(PartSize / 2f));

    /// <summary>
    /// A new node of that class, named after it and in no tree. Null for a
    /// class that cannot be made from its name.
    /// </summary>
    // A Part is a cube of PartSize that is solid, drawn and can be touched.
    // A light is white with a brightness of 1.
    public static SceneNode? Make(ReadOnlySpan<char> className) => className switch
    {
        NodeClassName.Part => new SceneNode(NodeClassName.Part)
        {
            // Kind before brush, so the node is never a world brush.
            BrushKind = BrushKind.Part,
            Brush = UnitCube.CloneShape(),
        },
        NodeClassName.Folder => new SceneNode(NodeClassName.Folder),
        "PointLight" => Lit("PointLight", LightKind.Point, PointLightRange),
        "SpotLight" => Lit("SpotLight", LightKind.Spot, BeamLightRange),
        "SurfaceLight" => Lit("SurfaceLight", LightKind.Rect, BeamLightRange),
        "DiscLight" => Lit("DiscLight", LightKind.Disc, BeamLightRange),
        "DirectionalLight" => Lit("DirectionalLight", LightKind.Directional, BeamLightRange),
        _ => null,
    };

    private static SceneNode Lit(string name, LightKind kind, float range) =>
        new(name) { Light = new Light { Kind = kind, Intensity = 1f, Color = Vector3.One, Range = range } };
}
