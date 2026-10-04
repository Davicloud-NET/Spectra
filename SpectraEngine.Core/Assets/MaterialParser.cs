using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Numerics;
using System.Text;

namespace SpectraEngine.Core.Assets;

/// <summary>
/// Reads <c>.spectramat</c> material files into a <see cref="MaterialDefinition"/>.
/// </summary>
/// <remarks>
/// One directive per line, <c>name = value</c> or <c>kind name = value</c>.
/// <c>//</c> starts a comment. Names match shader uniforms case-sensitively.
/// <code>
/// shader = lit                                     // optional, lit is the default
/// texture uDiffuse = Textures/wall_brick.png, linearmipmap, repeat
/// color   uBaseColor = #B4A08C                     // or: 0.706 0.627 0.549
/// float   uRoughness = 0.8
/// vec2    uTiling = 4 4
/// </code>
/// Texture options, in any order: <c>nearest</c>/<c>linear</c>/<c>linearmipmap</c>,
/// <c>repeat</c>/<c>clamp</c>, <c>srgb</c>/<c>data</c>. Units follow declaration order.
/// A <c>color</c> is read as sRGB and stored linear (alpha untouched); a
/// <c>vec3</c> is passed through as written. Use <c>data</c> for normal,
/// roughness and mask textures.
/// Nothing in a file is fatal: a bad line becomes a warning and parsing continues.
/// Callable from any thread.
/// </remarks>
public static class MaterialParser
{
    /// <summary>Canonical extension of a material file, including the dot.</summary>
    public const string FileExtension = ".spectramat";

    /// <summary>Shader name that selects the engine's built-in lit shader.</summary>
    public const string BuiltInShaderName = "lit";

    // Same defaults as AssetManager.LoadTexture.
    private const TextureFilter DefaultFilter = TextureFilter.LinearMipmap;
    private const TextureWrap DefaultWrap = TextureWrap.Repeat;

    // sRGB by default: a data map read as colour is easy to spot, an albedo
    // read as data just renders dark.
    private const TextureColorSpace DefaultColorSpace = TextureColorSpace.Srgb;

    /// <summary>Reads and parses the material file at <paramref name="absolutePath"/>.</summary>
    /// <exception cref="IOException">The file could not be read.</exception>
    public static MaterialDefinition ParseFile(string absolutePath)
    {
        ArgumentNullException.ThrowIfNull(absolutePath);
        return Parse(File.ReadAllText(absolutePath), absolutePath);
    }

    /// <summary>Parses material-file bytes as UTF-8 text. A leading BOM is stripped.</summary>
    public static MaterialDefinition ParseUtf8(ReadOnlySpan<byte> utf8, string originForErrors = "<memory>")
    {
        if (utf8.Length >= 3 && utf8[0] == 0xEF && utf8[1] == 0xBB && utf8[2] == 0xBF)
            utf8 = utf8[3..];

        return Parse(Encoding.UTF8.GetString(utf8), originForErrors);
    }

    /// <summary>
    /// Parses material-file text. <paramref name="originForErrors"/> only labels
    /// the messages in <see cref="MaterialDefinition.Warnings"/>.
    /// </summary>
    public static MaterialDefinition Parse(string text, string originForErrors = "<memory>")
    {
        ArgumentNullException.ThrowIfNull(text);

        var textures = new List<MaterialTextureSlot>();
        var parameters = new List<MaterialParameter>();
        var warnings = new List<string>();
        string? shaderName = null;

        int lineNumber = 0;
        foreach (ReadOnlySpan<char> rawLine in text.AsSpan().EnumerateLines())
        {
            lineNumber++;
            ReadOnlySpan<char> line = StripComment(rawLine).Trim();
            if (line.IsEmpty) continue;

            int equals = line.IndexOf('=');
            if (equals < 0)
            {
                Warn(warnings, originForErrors, lineNumber,
                    $"expected 'name = value' or 'kind name = value', got '{line.ToString()}'");
                continue;
            }

            ReadOnlySpan<char> left = line[..equals].Trim();
            ReadOnlySpan<char> value = line[(equals + 1)..].Trim();
            if (left.IsEmpty)
            {
                Warn(warnings, originForErrors, lineNumber, "directive has no name before '='");
                continue;
            }

            // One token is a bare key like 'shader'; two is kind then name.
            int split = IndexOfWhitespace(left);
            if (split < 0)
            {
                ParseBareKey(left, value, ref shaderName, warnings, originForErrors, lineNumber);
                continue;
            }

            ReadOnlySpan<char> kind = left[..split];
            ReadOnlySpan<char> name = left[(split + 1)..].TrimStart();
            if (IndexOfWhitespace(name) >= 0)
            {
                Warn(warnings, originForErrors, lineNumber,
                    $"'{left.ToString()}' has more than a kind and a name before '='");
                continue;
            }

            ParseTypedDirective(
                kind, name, value, textures, parameters, warnings, originForErrors, lineNumber);
        }

        return new MaterialDefinition(originForErrors, shaderName, textures, parameters, warnings);
    }

    private static void ParseBareKey(
        ReadOnlySpan<char> key,
        ReadOnlySpan<char> value,
        ref string? shaderName,
        List<string> warnings,
        string origin,
        int line)
    {
        if (key.Equals("shader", StringComparison.OrdinalIgnoreCase))
        {
            if (value.IsEmpty)
            {
                Warn(warnings, origin, line, "'shader' has no value; using the built-in lit shader");
                return;
            }
            if (shaderName is not null)
                Warn(warnings, origin, line, $"'shader' set again; '{value.ToString()}' replaces '{shaderName}'");

            shaderName = value.ToString();
            return;
        }

        // Warn, don't fail: a file written for a newer engine should still load.
        Warn(warnings, origin, line, $"unknown key '{key.ToString()}' ignored");
    }

    private static void ParseTypedDirective(
        ReadOnlySpan<char> kind,
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> value,
        List<MaterialTextureSlot> textures,
        List<MaterialParameter> parameters,
        List<string> warnings,
        string origin,
        int line)
    {
        if (kind.Equals("texture", StringComparison.OrdinalIgnoreCase))
        {
            ParseTexture(name, value, textures, warnings, origin, line);
            return;
        }

        MaterialParameterKind parameterKind;
        int required;
        if (kind.Equals("float", StringComparison.OrdinalIgnoreCase)) { parameterKind = MaterialParameterKind.Float; required = 1; }
        else if (kind.Equals("vec2", StringComparison.OrdinalIgnoreCase)) { parameterKind = MaterialParameterKind.Vector2; required = 2; }
        else if (kind.Equals("vec3", StringComparison.OrdinalIgnoreCase)) { parameterKind = MaterialParameterKind.Vector3; required = 3; }
        else if (kind.Equals("vec4", StringComparison.OrdinalIgnoreCase)) { parameterKind = MaterialParameterKind.Vector4; required = 4; }
        else if (kind.Equals("color", StringComparison.OrdinalIgnoreCase) ||
                 kind.Equals("colour", StringComparison.OrdinalIgnoreCase))
        {
            ParseColor(name, value, parameters, warnings, origin, line);
            return;
        }
        else
        {
            Warn(warnings, origin, line, $"unknown parameter kind '{kind.ToString()}' ignored");
            return;
        }

        Span<float> components = stackalloc float[4];
        if (!TryParseNumbers(value, components, out int count))
        {
            Warn(warnings, origin, line,
                $"'{name.ToString()}' is not a list of up to 4 numbers: '{value.ToString()}'");
            return;
        }
        if (count != required)
        {
            Warn(warnings, origin, line,
                $"'{kind.ToString()} {name.ToString()}' needs {required} number(s), got {count}");
            return;
        }

        AddParameter(
            parameters, warnings, origin, line,
            new MaterialParameter(name.ToString(), parameterKind,
                new Vector4(components[0], components[1], components[2], components[3])));
    }

    private static void ParseColor(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> value,
        List<MaterialParameter> parameters,
        List<string> warnings,
        string origin,
        int line)
    {
        Span<float> components = stackalloc float[4];
        int count;

        if (!value.IsEmpty && value[0] == '#')
        {
            if (!TryParseHexColor(value[1..], components, out count))
            {
                Warn(warnings, origin, line,
                    $"'{name.ToString()}' is not a #RRGGBB or #RRGGBBAA colour: '{value.ToString()}'");
                return;
            }
        }
        else if (!TryParseNumbers(value, components, out count))
        {
            Warn(warnings, origin, line,
                $"'{name.ToString()}' is neither a hex colour nor a number list: '{value.ToString()}'");
            return;
        }

        if (count is not (3 or 4))
        {
            Warn(warnings, origin, line, $"'color {name.ToString()}' needs 3 or 4 components, got {count}");
            return;
        }

        // sRGB in the file, linear in the shader. Alpha is not converted.
        var authored = new Vector4(components[0], components[1], components[2], components[3]);
        Vector4 linear = ColorSpace.SrgbToLinear(authored);

        AddParameter(
            parameters, warnings, origin, line,
            new MaterialParameter(
                name.ToString(),
                count == 3 ? MaterialParameterKind.Vector3 : MaterialParameterKind.Vector4,
                linear));
    }

    private static void ParseTexture(
        ReadOnlySpan<char> name,
        ReadOnlySpan<char> value,
        List<MaterialTextureSlot> textures,
        List<string> warnings,
        string origin,
        int line)
    {
        // Path runs to the first comma, so it may contain spaces.
        int comma = value.IndexOf(',');
        ReadOnlySpan<char> path = (comma < 0 ? value : value[..comma]).Trim();
        if (path.IsEmpty)
        {
            Warn(warnings, origin, line, $"texture '{name.ToString()}' has no file path");
            return;
        }

        TextureFilter filter = DefaultFilter;
        TextureWrap wrap = DefaultWrap;
        TextureColorSpace colorSpace = DefaultColorSpace;
        ReadOnlySpan<char> options = comma < 0 ? default : value[(comma + 1)..];
        while (!options.IsEmpty)
        {
            int next = options.IndexOf(',');
            ReadOnlySpan<char> option = (next < 0 ? options : options[..next]).Trim();
            options = next < 0 ? default : options[(next + 1)..];
            if (option.IsEmpty) continue;

            if (TryParseFilter(option, out TextureFilter parsedFilter)) filter = parsedFilter;
            else if (TryParseWrap(option, out TextureWrap parsedWrap)) wrap = parsedWrap;
            else if (TryParseColorSpace(option, out TextureColorSpace parsedSpace)) colorSpace = parsedSpace;
            else
                Warn(warnings, origin, line,
                    $"texture '{name.ToString()}' has unknown option '{option.ToString()}' (expected nearest/linear/linearmipmap, repeat/clamp or srgb/data)");
        }

        string slotName = name.ToString();
        for (int i = 0; i < textures.Count; i++)
        {
            if (textures[i].Name != slotName) continue;

            // Keep the original unit so later slots are not renumbered.
            Warn(warnings, origin, line, $"texture '{slotName}' declared more than once; the last one wins");
            textures[i] = new MaterialTextureSlot(
                slotName, path.ToString(), textures[i].Unit, filter, wrap, colorSpace);
            return;
        }

        textures.Add(new MaterialTextureSlot(
            slotName, path.ToString(), textures.Count, filter, wrap, colorSpace));
    }

    private static void AddParameter(
        List<MaterialParameter> parameters,
        List<string> warnings,
        string origin,
        int line,
        MaterialParameter parameter)
    {
        for (int i = 0; i < parameters.Count; i++)
        {
            if (parameters[i].Name != parameter.Name) continue;

            Warn(warnings, origin, line, $"parameter '{parameter.Name}' declared more than once; the last one wins");
            parameters[i] = parameter;
            return;
        }

        parameters.Add(parameter);
    }

    private static ReadOnlySpan<char> StripComment(ReadOnlySpan<char> line)
    {
        int comment = line.IndexOf("//", StringComparison.Ordinal);
        return comment < 0 ? line : line[..comment];
    }

    private static int IndexOfWhitespace(ReadOnlySpan<char> span)
    {
        for (int i = 0; i < span.Length; i++)
        {
            if (char.IsWhiteSpace(span[i])) return i;
        }
        return -1;
    }

    // Whitespace or comma separated, invariant culture.
    private static bool TryParseNumbers(ReadOnlySpan<char> value, Span<float> destination, out int count)
    {
        destination.Clear();
        count = 0;

        ReadOnlySpan<char> remaining = value;
        while (true)
        {
            while (!remaining.IsEmpty && (char.IsWhiteSpace(remaining[0]) || remaining[0] == ','))
                remaining = remaining[1..];
            if (remaining.IsEmpty) break;

            int end = 0;
            while (end < remaining.Length && !char.IsWhiteSpace(remaining[end]) && remaining[end] != ',')
                end++;

            if (count == destination.Length) return false;
            if (!float.TryParse(remaining[..end], NumberStyles.Float, CultureInfo.InvariantCulture, out float parsed))
                return false;

            destination[count++] = parsed;
            remaining = remaining[end..];
        }

        return count > 0;
    }

    private static bool TryParseHexColor(ReadOnlySpan<char> digits, Span<float> destination, out int count)
    {
        destination.Clear();
        count = 0;
        if (digits.Length is not (6 or 8)) return false;

        int components = digits.Length / 2;
        for (int i = 0; i < components; i++)
        {
            if (!byte.TryParse(digits.Slice(i * 2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte b))
                return false;
            destination[i] = b / 255f;
        }

        count = components;
        return true;
    }

    private static bool TryParseFilter(ReadOnlySpan<char> token, out TextureFilter filter)
    {
        // Not Enum.TryParse: AOT, and it would accept numeric spellings.
        if (token.Equals("nearest", StringComparison.OrdinalIgnoreCase)) { filter = TextureFilter.Nearest; return true; }
        if (token.Equals("linear", StringComparison.OrdinalIgnoreCase)) { filter = TextureFilter.Linear; return true; }
        if (token.Equals("linearmipmap", StringComparison.OrdinalIgnoreCase) ||
            token.Equals("mipmap", StringComparison.OrdinalIgnoreCase)) { filter = TextureFilter.LinearMipmap; return true; }

        filter = DefaultFilter;
        return false;
    }

    private static bool TryParseWrap(ReadOnlySpan<char> token, out TextureWrap wrap)
    {
        if (token.Equals("repeat", StringComparison.OrdinalIgnoreCase)) { wrap = TextureWrap.Repeat; return true; }
        if (token.Equals("clamp", StringComparison.OrdinalIgnoreCase)) { wrap = TextureWrap.Clamp; return true; }

        wrap = DefaultWrap;
        return false;
    }

    private static bool TryParseColorSpace(ReadOnlySpan<char> token, out TextureColorSpace colorSpace)
    {
        if (token.Equals("srgb", StringComparison.OrdinalIgnoreCase)) { colorSpace = TextureColorSpace.Srgb; return true; }
        // 'data', not 'linear': that word is already a filter on this line.
        if (token.Equals("data", StringComparison.OrdinalIgnoreCase)) { colorSpace = TextureColorSpace.Linear; return true; }

        colorSpace = DefaultColorSpace;
        return false;
    }

    private static void Warn(List<string> warnings, string origin, int line, string message)
        => warnings.Add($"{origin}({line}): {message}");
}
