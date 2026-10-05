using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets.Sources;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Maps;
using SpectraEngine.Core.Maps.Compiled;
using System;
using System.IO;
using System.Linq;

namespace Spectra.Kitchen.Tests;

// One level twice: cooked through the real rule and loaded as a game loads it,
// and the same bundle bound and compiled as the editor does it.
internal sealed class CookedLevel : IDisposable
{
    private readonly TempProject _project;
    private readonly FakeRenderer _renderer;

    private CookedLevel(TempProject project, FakeRenderer renderer)
    {
        _project = project;
        _renderer = renderer;
    }

    public required byte[] File { get; init; }

    // The loaded compiled map.
    public required SpectraEngine.Core.Scene.Scene Scene { get; init; }

    // The authored bundle, with its static world compiled.
    public required SpectraEngine.Core.Scene.Scene Authored { get; init; }

    public required CompiledMapLoadReport Report { get; init; }

    public required long CarvesDuringLoad { get; init; }

    public static CookedLevel Bake(SpectraEngine.Core.Scene.Scene source)
    {
        var project = new TempProject();

        try
        {
            string bundle = MapFixture.WriteBundle(project, "Room.smap", source);

            var context = new RuleContext(project.Root, "Maps/Room.smap", CookProfile.Ship);
            new MapRule().Cook(context);

            context.Diagnostics.Count.ShouldBe(
                0, string.Join(Environment.NewLine, context.Diagnostics.Select(d => d.ToString())));

            byte[] file = context.Emissions[0].Payload;

            var renderer = new FakeRenderer();
            var scene = new SpectraEngine.Core.Scene.Scene("empty");

            long before = Csg.CarveInvocationsOnThisThread;
            CompiledMapLoadReport report =
                CompiledMapLoader.Load(scene, renderer, ContentBlob.CopyOf(file), "Maps/Room.scmap");
            long carves = Csg.CarveInvocationsOnThisThread - before;

            MapDocument document = MapBundle.Load(bundle);
            var authored = new SpectraEngine.Core.Scene.Scene(document.Scene.Name);
            MapSceneBinder.ApplyTo(document, authored);
            authored.RebuildStaticWorld(new FakeRenderer());

            return new CookedLevel(project, renderer)
            {
                File = file,
                Scene = scene,
                Authored = authored,
                Report = report,
                CarvesDuringLoad = carves,
            };
        }
        catch
        {
            project.Dispose();
            throw;
        }
    }

    public void Dispose()
    {
        Scene.ReleaseCompiledStaticWorld(_renderer);
        _project.Dispose();
    }
}
