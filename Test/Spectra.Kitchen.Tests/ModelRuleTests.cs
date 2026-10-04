using Microsoft.Extensions.Logging.Abstractions;
using Spectra.Kitchen.Cooking;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Models;
using Spectra.Kitchen.Packs;
using Spectra.Kitchen.Rules;
using SpectraEngine.Bsp.Tests;
using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Packs;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Projects;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;

namespace Spectra.Kitchen.Tests;

/// <summary>
/// The model cook end to end: a glTF in a project folder, a <c>.smodel</c> in a
/// pack, and a prop the engine loads out of it.
/// </summary>
public class ModelRuleTests
{
    private const string Model = "Models/fixture.gltf";
    private const string MaterialPath = "Materials/" + GltfFixture.MaterialName + ".spectramat";
    private const string TexturePath = "Textures/fixture.png";

    private const string Signpost = "Models/signpost.gltf";

    [Fact]
    public void A_cooked_model_names_the_material_the_project_authors()
    {
        using var project = new TempProject();
        WriteFixture(project);

        CookResult cook = Cook(project);
        cook.Succeeded.ShouldBeTrue(Describe(cook.Diagnostics));

        // The authored .gltf is not copied into the pack as well.
        Read(cook, Model).ShouldBeNull();

        byte[] cooked = Read(cook, "Models/fixture.smodel").ShouldNotBeNull();
        SmodelModel model = SmodelReader.Read(cooked, "Models/fixture.smodel");

        model.Submeshes.Length.ShouldBe(1);
        model.GetName(model.Submeshes[0].MaterialNameOffset).ShouldBe(MaterialPath);
    }

    [Fact]
    public void A_model_whose_material_the_project_does_not_author_is_reported_and_names_none()
    {
        using var project = new TempProject();
        WriteFixture(project, authorMaterial: false);

        CookResult cook = Cook(project);

        // Soft by default: the model is valid, the limit is the cooked format's.
        cook.Succeeded.ShouldBeTrue(Describe(cook.Diagnostics));

        CookDiagnostic warning = cook.Diagnostics.Single(d => d.Id.ToString() == "SC3002");
        warning.IsError.ShouldBeFalse();
        warning.Message.ShouldContain(GltfFixture.MaterialName);
        warning.Message.ShouldContain(MaterialPath);

        // Names the texture the file described, so the material can be authored.
        warning.Message.ShouldContain("../Textures/fixture.png");

        byte[] cooked = Read(cook, "Models/fixture.smodel").ShouldNotBeNull();
        SmodelReader.Read(cooked, "m").Submeshes[0].HasMaterial.ShouldBeFalse();
    }

    [Fact]
    public void Strict_refuses_the_model_a_lax_cook_carries()
    {
        using var project = new TempProject();
        WriteFixture(project, authorMaterial: false);

        CookResult strict = new CookSession(
            project.Layout,
            new CookSettings { UseCache = false, Strict = true, Targets = [GraphicsBackend.OpenGL] }).Run();

        strict.Succeeded.ShouldBeFalse();
        strict.Diagnostics.Single(d => d.Id.ToString() == "SC3002").IsError.ShouldBeTrue();
    }

    [Fact]
    public void A_model_the_reader_refuses_is_reported_and_emits_nothing()
    {
        using var project = new TempProject();
        project.WriteAsset(Model, GltfFixture.Json(mode: 5));

        CookResult cook = Cook(project);

        cook.Succeeded.ShouldBeFalse();

        CookDiagnostic refused = cook.Diagnostics.Single(d => d.Id.ToString() == "SC3001");
        refused.Message.ShouldContain("mode 5");
        refused.Message.ShouldContain("TRIANGLE_STRIP");

        // No pack: a raw copy would ship a broken glTF under a path the engine resolves.
        cook.OutputPath.ShouldBeNull();
    }

    [Fact]
    public void What_the_cooked_format_cannot_carry_is_named_rather_than_dropped_in_silence()
    {
        using var project = new TempProject();
        WriteFixture(project, extraAttribute: "COLOR_0");

        CookResult cook = Cook(project);

        cook.Succeeded.ShouldBeTrue(Describe(cook.Diagnostics));

        CookDiagnostic note = cook.Diagnostics.Single(d => d.Id.ToString() == "SC3004");
        note.IsError.ShouldBeFalse();
        note.Message.ShouldContain("COLOR_0");
    }

    [Fact]
    public void A_sidecar_buffer_and_a_material_probe_are_both_recorded_dependencies()
    {
        // Without the .bin an edited buffer does not re-cook the model. Without
        // the probe, authoring the missing .spectramat does not either.
        using var project = new TempProject();
        project.WriteAsset(Model, GltfFixture.Json(bufferUri: "fixture.bin"));
        project.WriteAsset("Models/fixture.bin", GltfFixture.Buffer());

        var context = new RuleContext(project.Layout.AssetsPath, Model, CookProfile.Ship);
        new ModelRule().Cook(context);

        context.Dependencies.Select(d => d.Path).ShouldBe([Model, "Models/fixture.bin", MaterialPath]);
        context.Dependencies[1].Kind.ShouldBe(RuleDependencyKind.Read);
        context.Dependencies[2].Kind.ShouldBe(RuleDependencyKind.ProbeMissing);

        context.Emissions.ShouldHaveSingleItem().Kind.ShouldBe(PackEntryKind.Model);
    }

    [Fact]
    public void A_glb_cooks_to_the_same_bytes_as_the_json_it_wraps()
    {
        // Two projects: both spellings emit Models/fixture.smodel, so one project
        // holding both is a collision (SC9002).
        using var json = new TempProject("FromJson");
        json.WriteAsset(Model, GltfFixture.Json());

        using var glb = new TempProject("FromGlb");
        glb.WriteAsset(Model, GltfFixture.Glb(GltfFixture.GlbJson(), GltfFixture.Buffer()));

        CookResult fromJson = Cook(json);
        CookResult fromGlb = Cook(glb);

        fromJson.Succeeded.ShouldBeTrue(Describe(fromJson.Diagnostics));
        fromGlb.Succeeded.ShouldBeTrue(Describe(fromGlb.Diagnostics));

        Read(fromGlb, "Models/fixture.smodel").ShouldBe(Read(fromJson, "Models/fixture.smodel"));
    }

    [Fact]
    public void Two_models_that_would_land_on_one_cooked_path_are_a_collision()
    {
        using var project = new TempProject();
        project.WriteAsset(Model, GltfFixture.Json());
        project.WriteAsset("Models/fixture.glb", GltfFixture.Glb(GltfFixture.GlbJson(), GltfFixture.Buffer()));

        CookResult cook = Cook(project);

        cook.Succeeded.ShouldBeFalse();
        cook.Diagnostics.Single(d => d.Id.ToString() == "SC9002")
            .Message.ShouldContain("Models/fixture.smodel");
    }

    [Fact]
    public void A_cooked_model_whose_material_is_not_in_the_pack_is_refused_by_the_verifier()
    {
        // Pack written by hand: the cook refuses to produce one like this.
        using var project = new TempProject();
        string pack = Path.Combine(project.Root, "hole.spack");

        var writer = new PackWriter();
        writer.Add("Models/prop.smodel", PackEntryKind.Model, OneTriangle(MaterialPath));
        writer.WriteToFile(pack);

        PackVerifyResult result = PackVerifier.Verify(pack);

        result.Succeeded.ShouldBeFalse();

        CookDiagnostic missing = result.Diagnostics.Single(d => d.IsError);
        missing.Id.ToString().ShouldBe("SC3006");
        missing.Message.ShouldContain("Models/prop.smodel");
        missing.Message.ShouldContain(MaterialPath);
    }

    [Fact]
    public void A_cooked_model_the_engines_own_reader_refuses_is_reported_by_the_verifier()
    {
        using var project = new TempProject();
        string pack = Path.Combine(project.Root, "broken.spack");

        byte[] model = OneTriangle(null);
        model[0x04] = 0xEE;   // unknown format version

        var writer = new PackWriter();
        writer.Add("Models/prop.smodel", PackEntryKind.Model, model);
        writer.WriteToFile(pack);

        PackVerifyResult result = PackVerifier.Verify(pack);

        result.Succeeded.ShouldBeFalse();

        CookDiagnostic refused = result.Diagnostics.Single(d => d.IsError);
        refused.Id.ToString().ShouldBe("SC3005");
        refused.Message.ShouldContain("Recook");
    }

    [Fact]
    public void A_model_whose_material_IS_in_the_pack_passes_and_is_counted()
    {
        using var project = new TempProject();
        WriteFixture(project);

        CookResult cook = Cook(project);
        PackVerifyResult result = PackVerifier.Verify(cook.OutputPath!);

        result.Succeeded.ShouldBeTrue(Describe(result.Diagnostics));

        // One from the model's submesh, one from the material's texture slot.
        result.ReferencesChecked.ShouldBe(2);
    }

    // Loads the same prop loose (native importer) and cooked, then compares
    // quantised triangle sets per material. The two paths need not agree on
    // vertex order: the importer welds and reorders, the cook does neither.
    [Fact]
    public void The_cooked_prop_wears_the_same_materials_on_the_same_triangles_as_the_loose_import()
    {
        using var project = new TempProject();
        CopyFromEngineAssets(
            project,
            Signpost,
            "Materials/PostWood.spectramat",
            "Materials/SignFace.spectramat",
            "Textures/wall_brick.png",
            "Textures/dev_grid.png");

        CookResult cook = Cook(project);
        cook.Succeeded.ShouldBeTrue(Describe(cook.Diagnostics));

        using ProjectContentMount mount = ProjectContentMount.Open(
            NullLogger.Instance, project.Layout, ContentMountProfile.Shipped);

        // Nothing loose is mounted, so only the .smodel can serve the model.
        mount.Content.Exists(Signpost).ShouldBeFalse();

        using var cookedAssets = new AssetManager(
            NullLogger<AssetManager>.Instance, project.Layout.AssetsPath, mount.Content, hotReloadEnabled: false);
        cookedAssets.AttachRenderer(new FakeRenderer());

        cookedAssets.IsModelCooked(Signpost).ShouldBeTrue();
        ModelAsset cooked = cookedAssets.LoadModel(Signpost);

        using var looseAssets = new AssetManager(
            NullLogger<AssetManager>.Instance, ContentRoot.Path, hotReloadEnabled: false);
        looseAssets.AttachRenderer(new FakeRenderer());

        looseAssets.IsModelCooked(Signpost).ShouldBeFalse();

        // FlipTextureV off: Assimp already converts glTF's UV origin, so the
        // option would flip it back. Same options the demo passes.
        ModelAsset loose = looseAssets.LoadModel(
            Signpost, ModelImportOptions.Default with { FlipTextureV = false });

        Dictionary<string, List<string>> cookedByMaterial = ByMaterial(cooked);
        Dictionary<string, List<string>> looseByMaterial = ByMaterial(loose);

        // Two names, not one: both paths falling back to the default material
        // would otherwise compare equal.
        cookedByMaterial.Keys.Order(StringComparer.Ordinal)
            .ShouldBe(looseByMaterial.Keys.Order(StringComparer.Ordinal));
        cookedByMaterial.Count.ShouldBe(2);
        cookedByMaterial.Keys.ShouldContain("PostWood");
        cookedByMaterial.Keys.ShouldContain("SignFace");

        // Catches a cook that swapped the two submeshes' materials.
        foreach ((string material, List<string> triangles) in cookedByMaterial)
            triangles.ShouldBe(looseByMaterial[material], $"material '{material}'");

        cooked.LocalBounds.Min.X.ShouldBe(loose.LocalBounds.Min.X, 1e-3f);
        cooked.LocalBounds.Max.Y.ShouldBe(loose.LocalBounds.Max.Y, 1e-3f);

        cookedAssets.ReleaseGraphicsResources();
        looseAssets.ReleaseGraphicsResources();
    }

    // Triangles in model space, grouped by material name. Node transforms are
    // applied here because the cook has already baked its own into the vertices.
    private static Dictionary<string, List<string>> ByMaterial(ModelAsset asset)
    {
        ModelData data = asset.Data.ShouldNotBeNull();
        var byMaterial = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var placements = new List<(int Mesh, Matrix4x4 World)>();

        Walk(data.Root, Matrix4x4.Identity, placements);

        foreach ((int mesh, Matrix4x4 world) in placements)
        {
            ModelMesh source = data.Meshes[mesh];
            string material = asset.MaterialFor(source).Name;

            if (!byMaterial.TryGetValue(material, out List<string>? triangles))
                byMaterial[material] = triangles = [];

            for (int i = 0; i + 2 < source.Indices.Length; i += 3)
            {
                triangles.Add(Canonical(
                    Corner(source, source.Indices[i], world),
                    Corner(source, source.Indices[i + 1], world),
                    Corner(source, source.Indices[i + 2], world)));
            }
        }

        foreach (List<string> triangles in byMaterial.Values) triangles.Sort(StringComparer.Ordinal);
        return byMaterial;
    }

    private static void Walk(ModelNode node, Matrix4x4 parent, List<(int, Matrix4x4)> into)
    {
        Matrix4x4 world = node.LocalMatrix * parent;
        foreach (int mesh in node.MeshIndices) into.Add((mesh, world));
        foreach (ModelNode child in node.Children) Walk(child, world, into);
    }

    private static string Corner(in ModelMesh mesh, uint index, Matrix4x4 world)
    {
        int at = (int)index * ModelVertexLayout.FloatsPerVertex;

        var position = Vector3.Transform(
            new Vector3(mesh.Vertices[at], mesh.Vertices[at + 1], mesh.Vertices[at + 2]), world);

        Vector3 normal = Vector3.TransformNormal(
            new Vector3(mesh.Vertices[at + 3], mesh.Vertices[at + 4], mesh.Vertices[at + 5]), world);

        normal = normal.LengthSquared() > 0f ? Vector3.Normalize(normal) : normal;

        // Two decimals: the paths agree to about 1e-6, and the fixture's
        // coordinates are far coarser than 0.01. Invariant culture, or the
        // comparison depends on the machine's locale.
        return string.Create(CultureInfo.InvariantCulture,
            $"{position.X:F2},{position.Y:F2},{position.Z:F2}|" +
            $"{normal.X:F2},{normal.Y:F2},{normal.Z:F2}|" +
            $"{mesh.Vertices[at + 6]:F2},{mesh.Vertices[at + 7]:F2}");
    }

    // Rotates the smallest corner first. Sorting instead would lose the winding.
    private static string Canonical(string a, string b, string c)
    {
        if (string.CompareOrdinal(b, a) < 0 && string.CompareOrdinal(b, c) <= 0) return $"{b};{c};{a}";
        if (string.CompareOrdinal(c, a) < 0 && string.CompareOrdinal(c, b) < 0) return $"{c};{a};{b}";
        return $"{a};{b};{c}";
    }

    private static void WriteFixture(
        TempProject project, bool authorMaterial = true, string? extraAttribute = null)
    {
        project.WriteAsset(Model, GltfFixture.Json(extraNodeAttribute: extraAttribute));

        if (!authorMaterial) return;

        project.WriteAsset(TexturePath, TempProject.Png(8, 8, seed: 5));
        project.WriteAsset(MaterialPath, $"shader = lit\ntexture uDiffuse = {TexturePath}, nearest, clamp\n");
    }

    private static void CopyFromEngineAssets(TempProject project, params string[] contentPaths)
    {
        foreach (string path in contentPaths)
        {
            project.WriteAsset(
                path, File.ReadAllBytes(ContentRoot.ResolveAbsolute(ContentRoot.Path, path)));
        }
    }

    private static CookResult Cook(TempProject project) => new CookSession(
        project.Layout,
        new CookSettings { UseCache = false, Targets = [GraphicsBackend.OpenGL] }).Run();

    private static byte[]? Read(CookResult cook, string contentPath)
    {
        using var contents = new PackSource(NullLogger.Instance, cook.OutputPath!);
        if (!contents.TryOpen(contentPath, out SpectraEngine.Core.Assets.Sources.ContentBlob? blob)) return null;

        using (blob) return blob.Span.ToArray();
    }

    // A valid .smodel from the real writer.
    private static byte[] OneTriangle(string? material)
    {
        var vertices = new float[3 * 8];
        vertices[8] = 1f;
        vertices[17] = 1f;

        return SmodelWriter.Write(vertices, [0, 1, 2], [new SmodelSubmeshSpec(0, 3, material)]);
    }

    private static string Describe(IReadOnlyList<CookDiagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(d => d.ToString()));
}
