using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Serialization;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace SpectraEngine.Core.Projects;

/// <summary>Writes a <see cref="SpectraProject"/> as canonical UTF-8 JSON.</summary>
public static class ProjectWriter
{
    public static byte[] Write(SpectraProject project)
    {
        ArgumentNullException.ThrowIfNull(project);
        return CanonicalJson.Write(writer => WriteProject(writer, project));
    }

    private static void WriteProject(Utf8JsonWriter writer, SpectraProject project)
    {
        writer.WriteStartObject();

        CanonicalJson.Flush(writer, project.Unknown, -1);

        writer.WriteNumber(ProjectFormat.FormatVersionMember, project.FormatVersion);
        CanonicalJson.Flush(writer, project.Unknown, 0);

        writer.WriteNumber(ProjectFormat.MinimumReadableMember, project.MinimumReadableVersion);
        CanonicalJson.Flush(writer, project.Unknown, 1);

        writer.WriteString(ProjectFormat.EngineMember, project.Engine);
        CanonicalJson.Flush(writer, project.Unknown, 2);

        writer.WriteString(ProjectFormat.NameMember, project.Name);
        CanonicalJson.Flush(writer, project.Unknown, 3);

        writer.WriteString(ProjectFormat.IdMember, project.Id.ToString("D"));
        CanonicalJson.Flush(writer, project.Unknown, 4);

        // Omitted when the file names none, so such a file keeps its bytes.
        if (project.Language is { } language)
            writer.WriteString(ProjectFormat.LanguageMember, language);
        CanonicalJson.Flush(writer, project.Unknown, 5);

        // Omitted, not null: absent reads as "not chosen".
        if (!string.IsNullOrEmpty(project.StartupMap))
            writer.WriteString(ProjectFormat.StartupMapMember, project.StartupMap);
        CanonicalJson.Flush(writer, project.Unknown, 6);

        // One path per line, so adding a level is a one-line diff.
        var maps = new List<byte[]>(project.Maps.Count);
        foreach (string map in project.Maps)
            maps.Add(CanonicalJson.Compact(w => w.WriteStringValue(map)));
        CanonicalJson.WriteRecordArray(writer, ProjectFormat.MapsMember, maps);
        CanonicalJson.Flush(writer, project.Unknown, 7);

        // Omitted when empty: a manifest without 'packs' must round-trip
        // byte-identical.
        if (project.Packs.Count > 0)
        {
            writer.WritePropertyName(ProjectFormat.PacksMember);
            writer.WriteRawValue(CanonicalJson.Compact(w =>
            {
                w.WriteStartArray();
                foreach (string pack in project.Packs)
                    w.WriteStringValue(pack);
                w.WriteEndArray();
            }));
        }
        CanonicalJson.Flush(writer, project.Unknown, 8);

        writer.WritePropertyName(ProjectFormat.DisplayMember);
        writer.WriteRawValue(CompactDisplay(project.Display));
        CanonicalJson.Flush(writer, project.Unknown, 9);

        if (project.DefaultBackend is { } backend)
            writer.WriteString(ProjectFormat.DefaultBackendMember, ProjectFormat.ToWire(backend));
        CanonicalJson.Flush(writer, project.Unknown, 10);

        // Empty means no restriction, so it is omitted.
        if (project.AllowedBackends.Count > 0)
        {
            writer.WritePropertyName(ProjectFormat.AllowedBackendsMember);
            writer.WriteRawValue(CanonicalJson.Compact(w =>
            {
                w.WriteStartArray();
                foreach (GraphicsBackend allowed in project.AllowedBackends)
                    w.WriteStringValue(ProjectFormat.ToWire(allowed));
                w.WriteEndArray();
            }));
        }
        CanonicalJson.Flush(writer, project.Unknown, 11);

        writer.WriteEndObject();
    }

    private static byte[] CompactDisplay(ProjectDisplay display) => CanonicalJson.Compact(w =>
    {
        w.WriteStartObject();
        CanonicalJson.Flush(w, display.Unknown, -1);

        w.WriteNumber(ProjectFormat.WidthMember, display.Width);
        CanonicalJson.Flush(w, display.Unknown, 0);

        w.WriteNumber(ProjectFormat.HeightMember, display.Height);
        CanonicalJson.Flush(w, display.Unknown, 1);

        w.WriteBoolean(ProjectFormat.VsyncMember, display.Vsync);
        CanonicalJson.Flush(w, display.Unknown, 2);

        w.WriteString(ProjectFormat.ModeMember, ProjectFormat.ToWire(display.Mode));
        CanonicalJson.Flush(w, display.Unknown, 3);

        w.WriteEndObject();
    });
}
