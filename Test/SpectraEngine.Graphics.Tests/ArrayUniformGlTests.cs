using System.Numerics;
using Silk.NET.OpenGL;
using SpectraEngine.Core.Graphics;
using SpectraEngine.Core.Graphics.OpenGL;

namespace SpectraEngine.Graphics.Tests;

// A wrong location, count or stride raises nothing on any backend, so these
// read a pixel back. GL only; the D3D upload half is untested.
[Collection(GlRendererCollection.Name)]
public sealed class ArrayUniformGlTests
{
    private readonly GlRendererFixture _fixture;

    public ArrayUniformGlTests(GlRendererFixture fixture)
    {
        _fixture = fixture;
    }

    // Writes out one array element. The index is a uniform so the compiler
    // cannot fold it.
    private const string PickSource = """
        struct VertexInput {
            [Location(0)] vec3 position;
            [Location(1)] vec3 normal;
            [Location(2)] vec2 uv;
        }

        struct VertexOutput {
            [Position] vec4 position;
            vec2 uv;
        }

        struct FragmentInput {
            vec2 uv;
        }

        shader Pick {
            [Binding(0)] cbuffer Args {
                vec4[4] uValues;
                int uIndex;
            }

            [Vertex]
            VertexOutput VertexMain(VertexInput input) {
                var output = new VertexOutput();
                output.position = vec4(input.position, 1.0);
                output.uv = input.uv;
                return output;
            }

            [Fragment] [Target(0)]
            vec4 FragmentMain(FragmentInput input) {
                return uValues[uIndex];
            }
        }
        """;

    [Fact]
    public void Every_element_of_a_vec4_array_arrives_where_the_shader_expects_it()
    {
        OpenGLRenderer renderer = _fixture.Renderer;
        ShaderProgram shader = renderer.CreateShaderFromSource(PickSource);
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        // Distinct in red only, so a stride error reads a clearly different number.
        Vector4[] values =
        [
            new(0.2f, 0f, 0f, 1f),
            new(0.4f, 0f, 0f, 1f),
            new(0.6f, 0f, 0f, 1f),
            new(0.8f, 0f, 0f, 1f),
        ];

        try
        {
            for (int i = 0; i < values.Length; i++)
            {
                // Clear, not Keep: the clear initialises depth.
                renderer.BeginPass(output, PassClear.To(new Vector4(0f, 0f, 0f, 1f)));
                shader.Use();
                shader.SetUniform("uValues", values);
                shader.SetUniform("uIndex", i);
                renderer.EnsureFullscreenTriangleForTest().Draw();
                renderer.EndPass();

                int expected = (int)System.MathF.Round(values[i].X * 255f);
                int actual = ReadRed(output);
                actual.ShouldBeInRange(expected - 2, expected + 2,
                    $"element {i} should have arrived intact; a stride error would " +
                    "read a neighbouring element instead");
            }
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            shader.Dispose();
        }
    }

    [Fact]
    public void A_matrix_array_arrives_untransposed()
    {
        // A transposed upload would move the translation and push the
        // triangle off screen.
        const string Source = """
            struct VertexInput {
                [Location(0)] vec3 position;
                [Location(1)] vec3 normal;
                [Location(2)] vec2 uv;
            }
            struct VertexOutput { [Position] vec4 position; vec2 uv; }
            struct FragmentInput { vec2 uv; }

            shader MatPick {
                [Binding(0)] cbuffer Args {
                    mat4[2] uMatrices;
                }

                [Vertex]
                VertexOutput VertexMain(VertexInput input) {
                    var output = new VertexOutput();
                    output.position = uMatrices[1] * vec4(input.position, 1.0);
                    output.uv = input.uv;
                    return output;
                }

                [Fragment] [Target(0)]
                vec4 FragmentMain(FragmentInput input) { return vec4(1.0, 0.0, 0.0, 1.0); }
            }
            """;

        OpenGLRenderer renderer = _fixture.Renderer;
        ShaderProgram shader = renderer.CreateShaderFromSource(Source);
        RenderTarget output = renderer.CreateRenderTarget(new RenderTargetDesc(4, 4));

        Matrix4x4[] matrices =
        [
            // Decoy: reading element 0 pushes the triangle off screen.
            Matrix4x4.CreateTranslation(10f, 10f, 0f),
            Matrix4x4.Identity,
        ];

        try
        {
            renderer.BeginPass(output, PassClear.To(new Vector4(0f, 0f, 0f, 1f)));
            shader.Use();
            shader.SetUniform("uMatrices", matrices);
            renderer.EnsureFullscreenTriangleForTest().Draw();
            renderer.EndPass();

            ReadRed(output).ShouldBeGreaterThan(200,
                "the identity in element 1 should leave the triangle covering the target");
        }
        finally
        {
            renderer.DestroyRenderTarget(output);
            shader.Dispose();
        }
    }

    private unsafe int ReadRed(RenderTarget target)
    {
        GL gl = _fixture.Gl;
        uint fbo = gl.GenFramebuffer();
        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, fbo);
        gl.FramebufferTexture2D(
            FramebufferTarget.ReadFramebuffer, FramebufferAttachment.ColorAttachment0,
            TextureTarget.Texture2D, ((OpenGLTexture)target.ColorTexture!).Handle, 0);

        var pixel = new byte[4];
        fixed (byte* p = pixel)
            gl.ReadPixels(0, 0, 1, 1, PixelFormat.Rgba, PixelType.UnsignedByte, p);

        gl.BindFramebuffer(FramebufferTarget.ReadFramebuffer, 0);
        gl.DeleteFramebuffer(fbo);
        return pixel[0];
    }
}
