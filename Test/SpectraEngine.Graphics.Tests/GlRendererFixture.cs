using System;
using Microsoft.Extensions.Logging.Abstractions;
using Silk.NET.Maths;
using Silk.NET.OpenGL;
using Silk.NET.Windowing;
using SpectraEngine.Core;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;
using SpectraShade.Compiler;

namespace SpectraEngine.Graphics.Tests;

/// <summary>
/// Groups every test class that needs the one real GL context.
/// </summary>
// A collection fixture, not a class fixture: GLFW registers a process-global
// Win32 window class, so a second GlRendererFixture fails with "class already
// exists". Sharing one also keeps two classes off the context at once.
[CollectionDefinition(Name)]
public sealed class GlRendererCollection : ICollectionFixture<GlRendererFixture>
{
    /// <summary>Name to put on each participating class's <c>[Collection]</c>.</summary>
    public const string Name = "OpenGL renderer";
}

/// <summary>
/// A hidden OpenGL window and an initialized <see cref="OpenGLRenderer"/> for
/// tests that need a real GL context.
/// </summary>
public sealed class GlRendererFixture : IDisposable
{
    private readonly IWindow _window;

    public OpenGLRenderer Renderer { get; }

    /// <summary>
    /// The only real window this process may own. A test that borrows it
    /// must put its geometry back.
    /// </summary>
    public IWindow HostWindow => _window;

    /// <summary>
    /// A GL function table over the fixture's context, for asking the driver
    /// what it did.
    /// </summary>
    public GL Gl { get; }

    public GlRendererFixture()
    {
        SilkPlatform.EnsureRegistered();

        var options = WindowOptions.Default with
        {
            IsVisible = false,
            Size = new Vector2D<int>(64, 64),
            Title = "spectra-shader-tests",
            VSync = false,
        };

        _window = Window.Create(options);
        _window.Initialize();

        Renderer = new OpenGLRenderer(
            NullLogger<Renderer>.Instance,
            new SpectraShadeCompiler());
        Renderer.Initialize(new WindowRenderSurface(_window));

        // After Initialize, so the context is current on this thread.
        Gl = _window.CreateOpenGL();

        // Targets sized to the window treat a zero size as minimised and
        // draw nothing, so seed it as the engine does.
        Renderer.SetFramebufferSize(options.Size);
    }

    public void Dispose()
    {
        Renderer.Shutdown();
        _window.Dispose();
    }
}
