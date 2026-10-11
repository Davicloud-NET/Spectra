using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Icons;
using SpectraEngine.Editor.Shell;
using System.Globalization;
using System.Text.RegularExpressions;

namespace SpectraEngine.Editor.Tests;

/// <summary>
/// What keeps one thing from getting a second drawing: who may name an icon,
/// where each file came from, and that the 3D view's colours are the theme's.
/// </summary>
// Checked against the sources and the files on disk.
public sealed class IconDriftConventionTests
{
    // The one file in the shell that may name the icon of an entity, a light
    // or a file.
    private const string Registry = "EditorIcons.cs";

    // Icons.targets writes this one from the icon files.
    private const string Generated = "Icons.axaml";

    private static readonly Regex GuardedKey = new(@"\bIcon(?:Entity|Light|File)[A-Za-z0-9]*\b");

    [Fact]
    public void Outside_the_registry_no_shell_source_names_an_entity_light_or_file_icon()
    {
        var offenders = new List<string>();
        int examined = 0;

        IEnumerable<string> sources = Directory
            .EnumerateFiles(Path.Combine(IconFiles.SourceRoot(), "SpectraEngine.Editor"), "*.*", SearchOption.AllDirectories)
            .Where(file => file.EndsWith(".axaml", StringComparison.Ordinal) || file.EndsWith(".cs", StringComparison.Ordinal))
            .Where(file => !IsBuildOutput(file));

        foreach (string file in sources)
        {
            string name = Path.GetFileName(file);
            if (name is Registry or Generated)
                continue;

            examined++;
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                foreach (Match match in GuardedKey.Matches(lines[i]))
                    offenders.Add($"{name}({i + 1}): {match.Value}");
            }
        }

        examined.ShouldBeGreaterThan(100, "the shell's sources should have been read, not skipped");
        offenders.ShouldBeEmpty(
            "ask EditorIcons what icon a thing has. A key spelled anywhere else is a second answer to the same question");
    }

    [Fact]
    public void The_registry_does_name_them_so_the_rule_above_is_not_empty()
    {
        string registry = File.ReadAllText(
            Path.Combine(IconFiles.SourceRoot(), "SpectraEngine.Editor", "Shell", Registry));

        GuardedKey.Matches(registry).ShouldNotBeEmpty();
    }

    [Fact]
    public void Every_key_in_the_tabler_script_is_a_file_in_its_folder_that_names_the_same_tabler_icon()
    {
        Dictionary<string, Dictionary<string, string>> tables = TablerTables();
        var problems = new List<string>();

        foreach ((string table, Dictionary<string, string> rows) in tables)
        {
            string folder = table == "WORLD" ? IconFiles.WorldFolder : IconFiles.ShellFolder;
            rows.Count.ShouldBeGreaterThan(8, $"{table} should have been read, not skipped");

            foreach ((string key, string tabler) in rows)
            {
                string file = Path.Combine(folder, key + ".svg");
                if (!File.Exists(file))
                {
                    problems.Add($"{table}: {key} has no file in {Path.GetFileName(Path.GetDirectoryName(folder))}");
                    continue;
                }

                string? noted = TablerNote(File.ReadAllText(file));
                if (noted != tabler)
                    problems.Add($"{table}: {key} is '{tabler}' in the script and '{noted ?? "no note"}' in its file");
            }
        }

        problems.ShouldBeEmpty("tabler.py is the record of where each Tabler file came from");
    }

    [Fact]
    public void Every_file_that_carries_the_tabler_note_is_in_the_scripts_table_for_its_folder()
    {
        Dictionary<string, Dictionary<string, string>> tables = TablerTables();
        var strays = new List<string>();
        int noted = 0;

        foreach (string folder in IconFiles.Folders)
        {
            bool isWorld = folder == IconFiles.WorldFolder;

            foreach (string file in Directory.EnumerateFiles(folder, "*.svg"))
            {
                if (TablerNote(File.ReadAllText(file)) is null)
                    continue;

                noted++;
                string key = Path.GetFileNameWithoutExtension(file);
                string table = isWorld ? "WORLD" : key.StartsWith("IconLg", StringComparison.Ordinal) ? "LARGE" : "SMALL";
                if (!tables[table].ContainsKey(key))
                    strays.Add($"{key} is not in {table}");
            }
        }

        noted.ShouldBeGreaterThan(50, "the Tabler files should have been read, not skipped");
        strays.ShouldBeEmpty("a Tabler file gets in through tabler.py and no other way");
    }

    [Fact]
    public void No_icon_is_a_file_in_both_folders()
    {
        IconFiles.KeysIn(IconFiles.ShellFolder).Intersect(IconFiles.KeysIn(IconFiles.WorldFolder)).ShouldBeEmpty();
    }

    [Fact]
    public void The_world_folder_holds_the_entity_and_light_icons_and_the_shells_folder_none_of_them()
    {
        Regex world = new("^Icon(?:Entity|Light)");

        IconFiles.KeysIn(IconFiles.WorldFolder).Where(key => !world.IsMatch(key)).ShouldBeEmpty();
        IconFiles.KeysIn(IconFiles.ShellFolder).Where(key => world.IsMatch(key)).ShouldBeEmpty();
    }

    [Fact]
    public void Every_built_in_class_names_an_icon_and_that_name_has_a_file()
    {
        HashSet<string> world = IconFiles.KeysIn(IconFiles.WorldFolder);

        foreach (EntitySchema schema in BuiltinClasses.Catalog.Schemas)
        {
            schema.Icon.ShouldNotBeEmpty($"{schema.ClassName} names no icon");
            world.ShouldContain(WorldIcons.KeyForName(schema.Icon), $"{schema.ClassName} names '{schema.Icon}'");
        }
    }

    [Fact]
    public void Every_entity_and_light_icon_file_is_used_by_a_class_a_light_kind_or_a_fallback()
    {
        var used = new HashSet<string>(StringComparer.Ordinal) { WorldIcons.Entity, WorldIcons.UnknownEntity };

        foreach (EntitySchema schema in BuiltinClasses.Catalog.Schemas)
            used.Add(WorldIcons.ForClass(schema));

        foreach (LightKind kind in Enum.GetValues<LightKind>())
            used.Add(WorldIcons.ForLight(kind));

        IconFiles.KeysIn(IconFiles.WorldFolder).Except(used).ShouldBeEmpty(
            "a file nothing shows is a drawing waiting to drift");
    }

    [Fact]
    public void The_colours_the_3d_view_draws_groups_in_are_the_themes()
    {
        Dictionary<string, string> brushes = IconFiles.ThemeBrushes();

        EntityGroupTints.Table.Count.ShouldBeGreaterThan(3);
        foreach ((string group, uint rgb) in EntityGroupTints.Table)
        {
            string token = EditorIcons.GroupTintPrefix + group;
            brushes.ShouldContainKey(token);
            brushes[token].ShouldBe(Hex(rgb), $"{token} against EntityGroupTints");
            EditorIcons.GroupTint(group).ShouldBe(token);
        }

        brushes[EditorIcons.PlainEntity.Tint].ShouldBe(Hex(EntityGroupTints.UnknownRgb));
    }

    [Fact]
    public void Every_group_token_in_the_theme_is_a_group_the_3d_view_has_a_colour_for()
    {
        string[] themed = IconFiles.ThemeBrushes().Keys
            .Where(key => key.StartsWith(EditorIcons.GroupTintPrefix, StringComparison.Ordinal))
            .Select(key => key[EditorIcons.GroupTintPrefix.Length..])
            .ToArray();

        themed.ShouldBe(EntityGroupTints.Table.Select(row => row.Group), ignoreOrder: true);
    }

    [Fact]
    public void Every_built_in_group_has_a_tint_of_its_own()
    {
        foreach (EntitySchema schema in BuiltinClasses.Catalog.Schemas)
            EntityGroupTints.Has(schema.Group).ShouldBeTrue($"{schema.ClassName} is filed under '{schema.Group}'");
    }

    private static string Hex(uint rgb) => "#" + rgb.ToString("X6", CultureInfo.InvariantCulture);

    private static bool IsBuildOutput(string file) =>
        file.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal) ||
        file.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal);

    // The name in a file's first-line note: Tabler Icons "door", MIT...
    private static string? TablerNote(string svg)
    {
        Match note = Regex.Match(svg, @"<!--\s*Tabler Icons ""(?<name>[a-z0-9-]+)""");
        return note.Success ? note.Groups["name"].Value : null;
    }

    // The script's three tables, read as text: the script fetches from the
    // network when it runs.
    private static Dictionary<string, Dictionary<string, string>> TablerTables()
    {
        string script = File.ReadAllText(Path.Combine(IconFiles.ShellFolder, "tabler.py"));
        var tables = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

        foreach (Match table in Regex.Matches(script, @"^(?<name>[A-Z]+) = \{(?<rows>[^}]*)\}", RegexOptions.Multiline))
        {
            var rows = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match row in Regex.Matches(table.Groups["rows"].Value, @"""(?<key>\w+)"":\s*""(?<tabler>[a-z0-9-]+)"""))
                rows.Add(row.Groups["key"].Value, row.Groups["tabler"].Value);

            if (rows.Count > 0)
                tables.Add(table.Groups["name"].Value, rows);
        }

        tables.Keys.ShouldBe(["SMALL", "WORLD", "LARGE"], ignoreOrder: true);
        return tables;
    }
}
