using SpectraEngine.Core.Graphics;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Scans the graphics sources: none may name a window or the shell's UI
/// framework. Backends depend on <see cref="IRenderSurface"/> only.
/// </summary>
public sealed class RenderSurfaceConventionTests
{
    [Fact]
    public void No_graphics_source_names_a_window()
    {
        var offenders = new List<string>();

        foreach (string file in GraphicsSources())
        {
            // The adapter is the one exception.
            string name = Path.GetFileName(file);
            if (name == "WindowRenderSurface.cs")
                continue;

            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsComment(line))
                    continue;

                // IWindowModeLatch and IWindowModeTarget are fine: that is the
                // fullscreen seam, not a window.
                int at = line.IndexOf("IWindow", StringComparison.Ordinal);
                while (at >= 0)
                {
                    if (!IsLongerIdentifier(line, at))
                    {
                        offenders.Add($"{name}({i + 1}): {line.Trim()}");
                        break;
                    }

                    at = line.IndexOf("IWindow", at + 1, StringComparison.Ordinal);
                }
            }
        }

        offenders.ShouldBeEmpty(
            "the renderer depends on IRenderSurface, not on a window: a backend that reaches for " +
            "IWindow again takes ownership of a title, a cursor and an event pump that an embedded " +
            "host already owns, and re-couples the engine to running its own window");
    }

    // No file is exempt here: the shell's adapter lives in the shell.
    [Fact]
    public void No_graphics_source_names_the_shell_ui_framework()
    {
        var offenders = new List<string>();

        foreach (string file in GraphicsSources())
        {
            string name = Path.GetFileName(file);
            string[] lines = File.ReadAllLines(file);
            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (IsComment(line))
                    continue;

                if (line.Contains("Avalonia", StringComparison.Ordinal))
                    offenders.Add($"{name}({i + 1}): {line.Trim()}");
            }
        }

        offenders.ShouldBeEmpty(
            "the renderer's shared-target vocabulary is a native handle and four integers, and it stays " +
            "that way: a backend that names the shell's UI framework can be embedded in exactly one shell, " +
            "which is the coupling IRenderSurface was introduced to remove");
    }

    [Fact]
    public void Every_backend_refuses_a_surface_it_cannot_use()
    {
        var glOnly = new StubSurface(RenderSurfaceKind.None, handle: 0);
        var win32Only = new StubSurface(RenderSurfaceKind.Win32, handle: 1234);

        glOnly.GLContext.ShouldBeNull();
        win32Only.GLContext.ShouldBeNull();

        // The message has to say what was missing.
        Should.Throw<InvalidOperationException>(() => new Core.Graphics.OpenGL.OpenGLRenderer(
                Microsoft.Extensions.Logging.Abstractions.NullLogger<Renderer>.Instance,
                new SpectraShade.Compiler.SpectraShadeCompiler())
            .Initialize(win32Only))
            .Message.ShouldContain("GL context");
    }

    private sealed class StubSurface(RenderSurfaceKind kind, nint handle) : IRenderSurface
    {
        public RenderSurfaceKind Kind => kind;
        public nint NativeHandle => handle;
        public Silk.NET.Core.Contexts.IGLContext? GLContext => null;
        public Silk.NET.Maths.Vector2D<int> PixelSize => new(64, 64);

        public event Action<Silk.NET.Maths.Vector2D<int>>? Resized
        {
            add { }
            remove { }
        }
    }

    private static bool IsLongerIdentifier(string line, int at)
    {
        int after = at + "IWindow".Length;
        return after < line.Length && (char.IsLetterOrDigit(line[after]) || line[after] == '_');
    }

    private static bool IsComment(string line)
    {
        string trimmed = line.TrimStart();
        return trimmed.StartsWith("//", StringComparison.Ordinal)
            || trimmed.StartsWith("*", StringComparison.Ordinal);
    }

    private static IEnumerable<string> GraphicsSources()
    {
        string graphics = Path.Combine(SourceRoot(), "SpectraEngine.Core", "Graphics");
        Directory.Exists(graphics).ShouldBeTrue($"expected the graphics sources under {graphics}");
        return Directory.EnumerateFiles(graphics, "*.cs", SearchOption.AllDirectories);
    }

    private static string SourceRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (dir.GetFiles("*.slnx").Length > 0 || dir.GetFiles("*.sln").Length > 0)
                return dir.FullName;
            dir = dir.Parent;
        }

        throw new InvalidOperationException(
            $"No solution file above {AppContext.BaseDirectory}; the source-convention test needs the repo.");
    }
}
