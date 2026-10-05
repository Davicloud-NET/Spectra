namespace Spectra.Kitchen.CLI;

internal readonly struct ParseResult
{
    public CliMode Mode { get; }
    public CliOptions? Options { get; }
    public string? Error { get; }

    private ParseResult(CliMode mode, CliOptions? options, string? error)
    {
        Mode = mode;
        Options = options;
        Error = error;
    }

    public static ParseResult ForMode(CliMode mode) => new(mode, null, null);
    public static ParseResult ForOptions(CliOptions opts) => new(CliMode.Run, opts, null);
    public static ParseResult Usage(string error) => new(CliMode.UsageError, null, error);
}
