using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using SpectraEngine.Core.Bsp;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Bsp.Tests;

if (args.Contains("cook")) { CookMemoryProbe.Run(); return; }
if (args.Contains("structural")) { StructuralProbe.Run(); return; }
if (args.Contains("models")) { ModelMemoryProbe.Run(); return; }
if (args.Contains("workspace")) { WorkspaceProbe.Run(); return; }
if (args.Contains("maintenance")) { SceneMaintenanceProbe.Run(); return; }

// Review instrument: no window, GPU, CSG compilation, physics or mesh upload.
// All boxes sit behind the camera. This measures rejection work, not drawing.
CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
Console.WriteLine($"Runtime {Environment.Version}; CPUs {Environment.ProcessorCount}; tiering {AppContext.GetData("System.Runtime.TieredCompilation")}");
Console.WriteLine("3 timed samples, 2 warmups; culling sample = 200 calls; blocking GC before samples.");
Console.WriteLine("One shared brush; preconstructed nodes. Attachment includes scene membership and BVH insertion.");
Console.WriteLine("nodes | attach world ms | attach part ms | BuildRenderView us | QueryFrustum us | view bytes/call");
MeasureAttach(64, BrushKind.World);
MeasureAttach(64, BrushKind.Part);
foreach (int count in new[] { 1_000, 10_000, 50_000 })
{
    double worldMs = Median(() => MeasureAttach(count, BrushKind.World));
    double partMs = Median(() => MeasureAttach(count, BrushKind.Part));
    Scene scene = Populate(count);
    var view = new RenderView();
    var results = new List<SceneNode>();
    Frustum frustum = scene.Camera.GetFrustum();
    Action build = () => scene.BuildRenderView(scene.Camera, view);
    Action query = () => { results.Clear(); scene.QueryFrustum(in frustum, results); };
    double buildUs = Median(() => TimeCalls(build)) * 1000;
    double queryUs = Median(() => TimeCalls(query)) * 1000;
    long before = GC.GetAllocatedBytesForCurrentThread();
    for (int i = 0; i < 200; i++) build();
    long bytes = GC.GetAllocatedBytesForCurrentThread() - before;
    if (results.Count != 0 || view.Items.Count != 0)
        throw new InvalidOperationException("The rejection-only fixture has visible nodes.");
    Console.WriteLine($"{count,5} | {worldMs,15:F2} | {partMs,14:F2} | {buildUs,18:F2} | {queryUs,15:F2} | {bytes / 200.0,15:F1}");
    foreach (int visible in new[] { 0, 100, count })
    {
        var population = new Scene();
        var renderer = new FakeRenderer();
        Mesh mesh = renderer.CreateMesh([-0.5f,-0.5f,-0.5f, .5f,.5f,.5f], [0,1,0], [new VertexAttribute(0,3)]);
        var material = new Material(new NoopShaderProgram());
        for (int i = 0; i < count; i++)
        {
            var node = population.Root.CreateChild("mesh");
            node.LocalPosition = new((i % 10) * .05f, (i % 7) * .05f, i < visible ? -10 : 100);
            node.MeshRenderer = new(mesh, material);
        }
        var renderView = new RenderView();
        Action collect = () => population.BuildRenderView(population.Camera, renderView);
        double us = Median(() => TimeCalls(collect)) * 1000;
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 200; i++) collect();
        allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
        if (renderView.Items.Count != visible) throw new InvalidOperationException($"Expected {visible} visible, found {renderView.Items.Count}.");
        Console.WriteLine($"visibility {count} / {visible}: {us:F3} us; {allocated / 200d:F1} B/call");
    }
}

static SceneNode[] MakeNodes(int count, BrushKind kind)
{
    Brush brush = Brush.CreateBox(new Vector3(-0.5f), new Vector3(0.5f));
    var nodes = new SceneNode[count];
    for (int i = 0; i < count; i++)
        nodes[i] = new SceneNode("box")
        {
            BrushKind = kind,
            Brush = brush,
            LocalPosition = new Vector3((i % 100) * 3, (i / 100 % 100) * 3, 100 + (i / 10_000) * 3),
        };
    return nodes;
}

static double MeasureAttach(int count, BrushKind kind)
{
    SceneNode[] nodes = MakeNodes(count, kind);
    var scene = new Scene();
    long start = Stopwatch.GetTimestamp();
    foreach (SceneNode node in nodes) scene.Root.AddChild(node);
    double ms = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    GC.KeepAlive(scene);
    return ms;
}

static Scene Populate(int count)
{
    var scene = new Scene(); // Default camera looks down -Z.
    foreach (SceneNode node in MakeNodes(count, BrushKind.Part)) scene.Root.AddChild(node);
    return scene;
}

static double TimeCalls(Action action)
{
    long start = Stopwatch.GetTimestamp();
    for (int i = 0; i < 200; i++) action();
    return Stopwatch.GetElapsedTime(start).TotalMilliseconds / 200;
}

static double Median(Func<double> measure)
{
    for (int i = 0; i < 2; i++) measure();
    var samples = new double[3];
    for (int i = 0; i < samples.Length; i++)
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        samples[i] = measure();
    }
    Array.Sort(samples);
    return samples[1];
}

