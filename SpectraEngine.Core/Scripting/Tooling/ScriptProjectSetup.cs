using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace SpectraEngine.Core.Scripting.Tooling;

/// <summary>
/// Writes the files of <see cref="ScriptProjectFiles"/> into a project's
/// folder. The definitions file is the engine's and follows the engine. The
/// other three belong to the person once they exist, and are never written
/// over. A folder that cannot be written is reported, never thrown.
/// </summary>
public static class ScriptProjectSetup
{
    // No byte order mark: luau-lsp and Visual Studio Code read the files as they are.
    private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

    // Two scripts opened at once would both find a file missing and both write it.
    private static readonly Lock Gate = new();

    /// <summary>
    /// Writes every file the project lacks, and the definitions file when
    /// its text is not <paramref name="definitions"/>.
    /// </summary>
    /// <param name="projectFolder">The folder that holds Assets.</param>
    /// <param name="definitions">The definitions file's text, with <c>\n</c> line ends.</param>
    public static ScriptProjectReport Prepare(string projectFolder, string definitions) =>
        Run(projectFolder, definitions, writesMissing: true);

    /// <summary>
    /// Writes the definitions file again when the project has one with
    /// another text. A project that has none is left as it is.
    /// </summary>
    public static ScriptProjectReport Refresh(string projectFolder, string definitions) =>
        Run(projectFolder, definitions, writesMissing: false);

    private static ScriptProjectReport Run(string projectFolder, string definitions, bool writesMissing)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectFolder);
        ArgumentNullException.ThrowIfNull(definitions);

        var tally = new Tally(projectFolder);
        lock (Gate)
        {
            // A project folder is opened, never made here.
            if (!Directory.Exists(projectFolder))
                return ScriptProjectReport.Nothing;

            // A project that was never set up has no file to bring up.
            if (!writesMissing && !File.Exists(Path.Combine(projectFolder, ScriptProjectFiles.Definitions)))
                return ScriptProjectReport.Nothing;

            tally.Try(ScriptProjectFiles.Definitions, path => WriteDefinitions(path, definitions, tally));
            if (writesMissing)
            {
                tally.Try(ScriptProjectFiles.LuauRc, path => WriteNew(path, ScriptProjectFiles.LuauRcText()));
                tally.Try(ScriptProjectFiles.CodeSettings, path => WriteNew(path, ScriptProjectFiles.CodeSettingsText()));
                tally.Try(ScriptProjectFiles.CodeExtensions, path => WriteNew(path, ScriptProjectFiles.CodeExtensionsText()));
            }
        }

        return tally.Report();
    }

    // Left alone when it holds the same text, so a watcher of the file is
    // not woken for nothing.
    private static bool WriteDefinitions(string path, string text, Tally tally)
    {
        bool isThere = File.Exists(path);
        if (isThere && IsSameText(File.ReadAllBytes(path), text))
            return false;

        File.WriteAllBytes(path, Utf8.GetBytes(text));
        tally.IsRenewal = isThere;
        return true;
    }

    // A checkout on Windows may have given the file carriage returns.
    private static bool IsSameText(byte[] onDisk, string text) =>
        Utf8.GetString(onDisk).Replace("\r\n", "\n", StringComparison.Ordinal) == text;

    // False when the file is there. CreateNew is what keeps this from ever
    // writing over a person's file, also one that appeared after the look.
    private static bool WriteNew(string path, string text)
    {
        if (File.Exists(path))
            return false;

        if (Path.GetDirectoryName(path) is { Length: > 0 } folder)
            Directory.CreateDirectory(folder);

        FileStream stream;
        try
        {
            stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        }
        catch (IOException) when (File.Exists(path))
        {
            return false;
        }

        bool isFilled = false;
        try
        {
            using (stream)
                stream.Write(Utf8.GetBytes(text));

            isFilled = true;
            return true;
        }
        finally
        {
            // Left there empty it would count as the person's own for good.
            if (!isFilled)
                RemoveUnfilled(path);
        }
    }

    private static void RemoveUnfilled(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // The write's own failure is the one to report.
        }
    }

    // What one run wrote and what it could not.
    private sealed class Tally(string projectFolder)
    {
        private readonly List<string> _written = new(4);
        private readonly List<string> _refused = new(4);
        private string _reason = string.Empty;

        public bool IsRenewal { get; set; }

        public void Try(string file, Func<string, bool> write)
        {
            string path = Path.Combine(projectFolder, file.Replace('/', Path.DirectorySeparatorChar));
            try
            {
                if (Directory.Exists(path))
                    Refuse(file, $"{file} is a folder.");
                else if (write(path))
                    _written.Add(file);
            }
            catch (Exception ex) when (
                ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // A folder that cannot be written, a file another program holds, a full disk.
                Refuse(file, ex.Message);
            }
        }

        public ScriptProjectReport Report() =>
            _written.Count == 0 && _refused.Count == 0
                ? ScriptProjectReport.Nothing
                : new ScriptProjectReport(_written, _refused, _reason) { IsRenewal = IsRenewal };

        private void Refuse(string file, string reason)
        {
            _refused.Add(file);
            if (_reason.Length == 0)
                _reason = ScriptProjectReport.Sentence(reason);
        }
    }
}
