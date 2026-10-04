using Spectra.Kitchen.Cache;
using Spectra.Kitchen.Diagnostics;
using Spectra.Kitchen.Models;
using SpectraEngine.Core.Assets.Models;
using SpectraEngine.Core.Assets.Packs;
using System;
using System.Collections.Generic;

namespace Spectra.Kitchen.Rules;

/// <summary>
/// Turns an authored glTF or GLB into a <c>.smodel</c>: one vertex buffer, one
/// index buffer, submeshes as index ranges, materials named by path. Node
/// transforms are baked into the vertices, so the cooked model has no hierarchy.
/// </summary>
public sealed class ModelRule : IRule
{
    /// <inheritdoc/>
    public RuleKind Kind => RuleKind.Model;

    /// <inheritdoc/>
    public int Version => 1;

    /// <inheritdoc/>
    public CookSettingKeys SettingsRead => CookSettingKeys.None;

    /// <summary>Whether <paramref name="contentPath"/> is a model this rule cooks.</summary>
    public static bool Handles(string contentPath) => GltfReader.Handles(contentPath);

    /// <inheritdoc/>
    public void Cook(IRuleContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        byte[] source = context.Read(context.SourcePath);

        GltfModel model;
        try
        {
            model = GltfReader.Read(source, context.SourcePath, path => ReadBuffer(context, path));
        }
        catch (GltfFormatException ex)
        {
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ModelUndecodable,
                $"'{context.SourcePath}' could not be read: {ex.Message}",
                context.SourcePath));

            return;
        }

        foreach (string dropped in model.Dropped)
        {
            context.Report(CookDiagnostic.Info(
                CookDiagnosticCodes.ModelDataDropped,
                $"'{context.SourcePath}' carries {dropped}, which a v1 .smodel does not.",
                context.SourcePath));
        }

        string?[] materials = ResolveMaterials(context, model);

        var vertices = new List<float>();
        var indices = new List<uint>();
        var submeshes = new List<SmodelSubmeshSpec>(model.Submeshes.Count);

        for (int i = 0; i < model.Submeshes.Count; i++)
        {
            GltfSubmesh submesh = model.Submeshes[i];
            var start = (uint)indices.Count;
            var vertexBase = (uint)(vertices.Count / (int)SmodelStandardLayout.StrideFloats);

            vertices.AddRange(submesh.Vertices);
            for (int at = 0; at < submesh.Indices.Length; at++)
                indices.Add(submesh.Indices[at] + vertexBase);

            submeshes.Add(new SmodelSubmeshSpec(
                start,
                (uint)submesh.Indices.Length,
                (uint)submesh.MaterialIndex < (uint)materials.Length
                    ? materials[submesh.MaterialIndex]
                    : null));
        }

        byte[] cooked;
        try
        {
            cooked = SmodelWriter.Write(
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(vertices),
                System.Runtime.InteropServices.CollectionsMarshal.AsSpan(indices),
                submeshes);
        }
        catch (ArgumentException ex)
        {
            // Report, don't throw, so the diagnostic names the asset.
            context.Report(CookDiagnostic.Error(
                CookDiagnosticCodes.ModelEncodeFailed,
                $"'{context.SourcePath}' produced a model the container cannot hold: {ex.Message}",
                context.SourcePath));

            return;
        }

        context.Emit(ModelContentPath.CookedPathFor(context.SourcePath), cooked, PackEntryKind.Model);
    }

    // Sidecar .bin. Probe first so a miss returns null and the reader can name
    // the buffer, where Read would throw.
    private static byte[]? ReadBuffer(IRuleContext context, string contentPath) =>
        context.Probe(contentPath) ? context.Read(contentPath) : null;

    // Index-aligned with the file's material table. Only slots a submesh draws
    // with are looked up: exporters often emit materials nothing references.
    private static string?[] ResolveMaterials(IRuleContext context, GltfModel model)
    {
        var paths = new string?[model.Materials.Count];

        Span<bool> referenced = model.Materials.Count <= 64
            ? stackalloc bool[model.Materials.Count]
            : new bool[model.Materials.Count];
        referenced.Clear();

        for (int i = 0; i < model.Submeshes.Count; i++)
        {
            int index = model.Submeshes[i].MaterialIndex;
            if ((uint)index < (uint)referenced.Length) referenced[index] = true;
        }

        for (int i = 0; i < paths.Length; i++)
        {
            if (!referenced[i]) continue;

            GltfMaterial material = model.Materials[i];
            string? candidate = ModelMaterialOverride.PathFor(material.Name);

            if (candidate is not null && context.Probe(candidate))
            {
                paths[i] = candidate;
                continue;
            }

            context.Report(CookDiagnostic.Warning(
                CookDiagnosticCodes.ModelMaterialUnauthored,
                Describe(context.SourcePath, material, candidate),
                context.SourcePath));
        }

        return paths;
    }

    private static string Describe(string sourcePath, in GltfMaterial material, string? candidate)
    {
        string named = material.Name.Length > 0 ? $"material '{material.Name}'" : "an unnamed material";
        string wanted = candidate is null
            ? $"and a cooked model can only reference a material by path, so there is nothing to name. " +
              $"Give it a name and author '{ModelMaterialOverride.Folder}/<name>" +
              $"{SpectraEngine.Core.Assets.MaterialParser.FileExtension}'"
            : $"and this project has no '{candidate}'. Author one";

        string texture = material.BaseColorImageUri is { Length: > 0 } uri
            ? $" The file's own base colour image is '{uri}'."
            : string.Empty;

        return $"'{sourcePath}' draws with {named} {wanted}, or the cooked submesh binds the engine's " +
            $"default material where a loose import would have used what the file describes.{texture}";
    }
}
