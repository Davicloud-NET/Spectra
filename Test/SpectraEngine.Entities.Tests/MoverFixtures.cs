using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;
using System.Runtime.InteropServices;

namespace SpectraEngine.Entities.Tests;

internal static class Movers
{
    public const float Dt = 1f / 60f;

    // EntityRuntime's classes plus the probe.
    public static EntityCatalog Catalog(List<string> log)
    {
        EntityCatalog catalog = EntityRuntime.Catalog(log);
        catalog.Add(new EntitySchema(MoverProbe.WireName), static () => new MoverProbe());
        return catalog;
    }

    // A part brush of the given size, centred on its node.
    public static SceneNode Part(SceneNode parent, string name, Vector3 size, Vector3 position)
    {
        SceneNode node = parent.CreateChild(name);
        node.LocalPosition = position;

        // Kind before brush, or the node is briefly a world brush.
        node.BrushKind = BrushKind.Part;
        node.Brush = Brush.CreateBox(size * -0.5f, size * 0.5f);
        return node;
    }

    // Wires each output to a recorder named "sink", as an input of the same name.
    public static void WireToSink(SceneNode node, params string[] outputs)
    {
        foreach (string output in outputs)
            EntityRuntime.Wire(node, output, "sink", output);
    }

    public static void Run(EntityWorld world, int ticks)
    {
        for (int i = 0; i < ticks; i++)
            world.Tick(Dt);
    }

    // Every bit of a transform, so -0 and 0 cannot pass as equal.
    public static byte[] Bits(Transform transform) =>
        MemoryMarshal.AsBytes(new ReadOnlySpan<Transform>(in transform)).ToArray();
}
