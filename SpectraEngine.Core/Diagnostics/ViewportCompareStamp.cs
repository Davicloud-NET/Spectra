using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Serialization;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;

namespace SpectraEngine.Core.Diagnostics;

// Keyed by backend only. The adapter is informational: the renderer reports
// the selection ("system default"), not the device it opened, so a hybrid
// machine that compared on one GPU and composites on the other is not caught.
/// <summary>
/// The result of the last <c>--viewport-compare</c> run, saved per user so
/// the editor shell, a different process, can read it. A missing or damaged
/// file means no measurement, not an error.
/// </summary>
/// <param name="Adapter">What the producer called the adapter it ran on.</param>
/// <param name="Green">Whether the two pictures agreed.</param>
public sealed record ViewportCompareStamp(
    string Adapter, GraphicsBackend Backend, bool Green, DateTime RecordedUtc)
{
    /// <summary>Where the stamp lives for this user, beside the editor's own settings.</summary>
    public static string DefaultPath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "Spectra", "viewport-compare.json");

    /// <summary>Whether this stamp is a green verdict for the given backend. A null stamp is not.</summary>
    public static bool IsGreenFor(ViewportCompareStamp? stamp, GraphicsBackend backend) =>
        stamp is { Green: true } && stamp.Backend == backend;

    /// <summary>Reads the stamp, or null when there is nothing to read.</summary>
    public static ViewportCompareStamp? Load() => Load(DefaultPath);

    /// <summary>Reads from an explicit path, for tests.</summary>
    public static ViewportCompareStamp? Load(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        if (!File.Exists(path))
            return null;

        try
        {
            return Read(File.ReadAllBytes(path));
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return null;
        }
    }

    /// <summary>Writes the stamp. Returns false instead of throwing when the write fails.</summary>
    public bool Save() => Save(DefaultPath);

    /// <summary>Writes to an explicit path, for tests.</summary>
    public bool Save(string path)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);

        try
        {
            if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
                Directory.CreateDirectory(folder);

            File.WriteAllBytes(path, CanonicalJson.Write(Write));
            return true;
        }
        catch (Exception ex) when (IsRecoverable(ex))
        {
            return false;
        }
    }

    private void Write(Utf8JsonWriter writer)
    {
        writer.WriteStartObject();
        writer.WriteString("adapter", Adapter);
        writer.WriteString("backend", BackendName(Backend));
        writer.WriteBoolean("green", Green);
        writer.WriteString("recordedUtc", RecordedUtc.ToString("O", CultureInfo.InvariantCulture));
        writer.WriteEndObject();
    }

    private static ViewportCompareStamp? Read(ReadOnlySpan<byte> utf8)
    {
        var reader = new Utf8JsonReader(CanonicalJson.StripBom(utf8), CanonicalJson.ReaderOptions);

        if (!reader.Read() || reader.TokenType != JsonTokenType.StartObject)
            return null;

        string adapter = string.Empty;
        GraphicsBackend backend = GraphicsBackend.D3D11;
        bool haveBackend = false;
        bool green = false;
        DateTime recorded = DateTime.MinValue;

        while (reader.Read() && reader.TokenType == JsonTokenType.PropertyName)
        {
            if (reader.ValueTextEquals("adapter"))
            {
                reader.Read();
                adapter = reader.GetString() ?? string.Empty;
            }
            else if (reader.ValueTextEquals("backend"))
            {
                reader.Read();
                haveBackend = TryParseBackend(reader.GetString(), out backend);
            }
            else if (reader.ValueTextEquals("green"))
            {
                reader.Read();
                green = reader.GetBoolean();
            }
            else if (reader.ValueTextEquals("recordedUtc"))
            {
                reader.Read();
                DateTime.TryParse(
                    reader.GetString(), CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out recorded);
            }
            else
            {
                // Unknown member from a newer build.
                reader.Read();
                reader.Skip();
            }
        }

        if (!haveBackend)
            return null;

        return new ViewportCompareStamp(adapter, backend, green, recorded);
    }

    // Hand-written, not Enum.ToString/Parse: trimming removes enum names,
    // so those work in a debug run and fail in a published build.
    private static string BackendName(GraphicsBackend backend) => backend switch
    {
        GraphicsBackend.OpenGL => "opengl",
        GraphicsBackend.Vulkan => "vulkan",
        GraphicsBackend.D3D11 => "d3d11",
        GraphicsBackend.D3D12 => "d3d12",
        _ => throw new ArgumentOutOfRangeException(nameof(backend), backend, "Unknown graphics backend."),
    };

    private static bool TryParseBackend(string? text, out GraphicsBackend backend)
    {
        switch (text)
        {
            case "opengl": backend = GraphicsBackend.OpenGL; return true;
            case "vulkan": backend = GraphicsBackend.Vulkan; return true;
            case "d3d11": backend = GraphicsBackend.D3D11; return true;
            case "d3d12": backend = GraphicsBackend.D3D12; return true;
            default: backend = GraphicsBackend.D3D11; return false;
        }
    }

    // What a damaged or unreachable file can throw.
    private static bool IsRecoverable(Exception ex) =>
        ex is JsonException or IOException or UnauthorizedAccessException
            or InvalidOperationException or FormatException or NotSupportedException;
}
