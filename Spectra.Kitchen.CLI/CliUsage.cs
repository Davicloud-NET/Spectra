namespace Spectra.Kitchen.CLI;

// scook's help text.
internal static class CliUsage
{
    public static void Print(TextWriter w, bool color)
    {
        var s = new AnsiStyle(color);

        w.WriteLine($"{s.Title}Spectra cook{s.Reset} {s.Dim}(scook){s.Reset}");
        w.WriteLine();
        w.WriteLine($"{s.Header}Usage:{s.Reset}");
        w.WriteLine($"  {s.Command}scook{s.Reset} {s.Dim}[cook]{s.Reset} {s.Dim}[options]{s.Reset} {s.Placeholder}<projectDir>{s.Reset}");
        w.WriteLine($"  {s.Command}scook verify{s.Reset} {s.Placeholder}<pack>{s.Reset}");
        w.WriteLine($"  {s.Command}scook inspect{s.Reset} {s.Dim}[--json]{s.Reset} {s.Placeholder}<pack>{s.Reset}");
        w.WriteLine($"  {s.Command}scook clean{s.Reset} {s.Dim}[options]{s.Reset} {s.Placeholder}<projectDir>{s.Reset}");
        w.WriteLine($"  {s.Command}scook sounds{s.Reset} {s.Flag}-o{s.Reset} {s.Placeholder}<dir>{s.Reset} {s.Placeholder}<contentDir>{s.Reset}");
        w.WriteLine();

        PrintVerbs(w, s);
        w.WriteLine();
        PrintOptions(w, s);
        w.WriteLine();

        w.WriteLine($"{s.Header}Diagnostics{s.Reset} are printed on stderr in IDE-parseable form:");
        w.WriteLine($"  {s.Placeholder}<file>{s.Reset}({s.Placeholder}<line>{s.Reset},{s.Placeholder}<col>{s.Reset}): {s.Error}error{s.Reset}|{s.Warning}warning{s.Reset}|{s.Info}info{s.Reset} {s.Value}SC####{s.Reset}: {s.Placeholder}<message>{s.Reset}");
        w.WriteLine($"  {s.Dim}Bands: 0xxx project/CLI, 1xxx discovery, 2xxx image, 3xxx model, 4xxx audio,{s.Reset}");
        w.WriteLine($"  {s.Dim}       5xxx material, 6xxx shader, 7xxx map, 8xxx script, 9xxx pack.{s.Reset}");
        w.WriteLine($"  {s.Dim}A shader error keeps its own SS#### code rather than being renumbered.{s.Reset}");
        w.WriteLine();
        w.WriteLine($"{s.Header}Exit codes:{s.Reset} " +
            $"{s.Value}0{s.Reset}=success, " +
            $"{s.Value}1{s.Reset}=cook error, " +
            $"{s.Value}2{s.Reset}=usage error, " +
            $"{s.Value}3{s.Reset}=I/O error");
    }

    private static void PrintVerbs(TextWriter w, AnsiStyle s)
    {
        w.WriteLine($"{s.Header}Verbs:{s.Reset}");
        WriteOption(w, s, "cook", null,
            "Cook a project into a pack. The default verb.");
        WriteOption(w, s, "verify", null,
            "Check a cooked pack: every payload decodes, every",
            "in-pack reference resolves, the table is searchable,",
            "the digest agrees.");
        WriteOption(w, s, "inspect", null,
            "List a pack's header and entries: ids, names, sizes,",
            "kinds and codecs.");
        WriteOption(w, s, "clean", null,
            "Delete a project's cook output and its cook cache.");
        WriteOption(w, s, "sounds", null,
            "Cook every WAV under a content folder to a .saudio",
            "under -o, with no project and no pack. For a build",
            "that runs from loose files. Only a sound whose files",
            "changed is cooked again, and the cooked file of a",
            "WAV that is gone is removed.");
    }

    private static void PrintOptions(TextWriter w, AnsiStyle s)
    {
        w.WriteLine($"{s.Header}Options:{s.Reset}");
        WriteOption(w, s, "-o, --output", "<path>",
            "Where output goes",
            $"(default: the project's {s.Value}cooked/{s.Reset} folder)");
        WriteOption(w, s, "    --profile", "<name>",
            $"Values: {s.Value}ship, fast, preview{s.Reset}",
            $"Default: {s.Value}ship{s.Reset}");
        WriteOption(w, s, "-t, --target", "<backend>",
            "Backend(s) shaders are cooked for. Comma-separated.",
            $"Values: {s.Value}opengl, vulkan, d3d11, d3d12, all{s.Reset}",
            $"Default: {s.Value}opengl, d3d11, d3d12{s.Reset} {s.Dim}(as ssc){s.Reset}");
        WriteOption(w, s, "-j, --jobs", "<n>",
            $"Worker count. {s.Value}-j1{s.Reset} is the determinism-oracle mode.");
        WriteOption(w, s, "    --cache", null,
            $"Use the cook cache in {s.Value}.spectra-cook/{s.Reset} (default).");
        WriteOption(w, s, "    --no-cache", null,
            "Neither read nor write the cook cache: re-cook",
            "everything and leave what is cached alone.");
        WriteOption(w, s, "    --loose", null,
            "Emit a cooked directory tree instead of a pack.");
        WriteOption(w, s, "    --watch", null,
            "Re-cook on change. Implies --loose.");
        WriteOption(w, s, "    --keep-brush-source", null,
            "Keep authored brushes in a cooked map, so a verify",
            "can recompile them and compare.");
        WriteOption(w, s, "    --script-source", "<mode>",
            $"Values: {s.Value}embed, strip{s.Reset}. Default: {s.Value}embed{s.Reset}");
        WriteOption(w, s, "    --encoder", "<name>",
            $"Values: {s.Value}managed, native{s.Reset}. Default: {s.Value}managed{s.Reset}");
        WriteOption(w, s, "    --strict", null,
            "Treat warnings as errors.");
        WriteOption(w, s, "    --manifest", "<path>",
            "Write a JSON manifest of every asset, its id, its",
            "inputs and its output hash. This is what CI diffs.");
        WriteOption(w, s, "    --json", null,
            $"{s.Command}inspect{s.Reset} only: print the pack as JSON rather than",
            "as a table whose columns move with the content.");
        WriteOption(w, s, "-q, --quiet", null,
            "Suppress non-error output.");
        WriteOption(w, s, "    --no-color", null,
            $"Disable ANSI color output {s.Dim}(NO_COLOR is honoured){s.Reset}.");
        WriteOption(w, s, "-h, --help", null,
            "Show this help and exit.");
        WriteOption(w, s, "    --version", null,
            "Print version and exit.");
    }

    private static void WriteOption(TextWriter w, AnsiStyle s, string flags, string? arg, params string[] description)
    {
        const int descCol = 27;
        var argText = arg is null ? string.Empty : $" {s.Placeholder}{arg}{s.Reset}";
        var preLen = 2 + flags.Length + (arg is null ? 0 : 1 + arg.Length);
        var pad = preLen < descCol ? new string(' ', descCol - preLen) : "  ";
        w.WriteLine($"  {s.Flag}{flags}{s.Reset}{argText}{pad}{description[0]}");
        var contIndent = new string(' ', descCol);
        for (int i = 1; i < description.Length; i++)
            w.WriteLine($"{contIndent}{description[i]}");
    }
}
