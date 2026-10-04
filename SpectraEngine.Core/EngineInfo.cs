/*
 * SPECTRA ENGINE
 * 
 * The spectra engine is a high-performance, cross-platform game engine designed with ease of use and data-driven flexibility in mind.
 * Built on concepts from modern game engines and older ones, implementing both scene-graph and BSP tree structures for efficient rendering and spatial management.
 * Uses the SILK.NET library together with a custom-built shader language to allow writing one shader that can be compiled for multiple pipelines (Vulcan, DirectX, OpenGL, etc)
 * 
 * The goal of this engine is to merge the simplicity of easier to learn engines with the power and flexibility of more complex ones. It should be easy to pick up.
 * This is why the engine is designed to be as data-driven as possible, together with an editor merging HAMMER and roblox studio's ease of use and flexibility.
 * 
 * The engine uses/will use AoT compilation to achieve better performance and to comply with platform guidelines (eg consoles, android, etc)
 * When writing code for this engine, keep AoT in mind and try to think things through BEFORE using AI.
 * Review AI code before using it, and test it thoroughly!
 * 
 * Anyways, if you are a contributor in the (hopefully) future, have fun working on this!
 */

namespace SpectraEngine.Core
{
    public static class EngineInfo
    {
        public const int MajorVersion = 1;
        public const int MinorVersion = 0;
        public const int RevisionVersion = 0;
        public static readonly string VersionString = $"{MajorVersion}.{MinorVersion}.{RevisionVersion}";

        // Cooked formats need an exact version match, else recook.
        public const int ModelFormatVersion = 1;
        public const int TextureFormatVersion = 1;
        // Raise when the .saudio layout moves. The sample rate is per file.
        public const int AudioFormatVersion = 1;
        // 2: light kinds beyond directional and point.
        // 3: the entity payload.
        // A document only declares the version it needs, so a map without
        // either still says 1.
        public const int MapFormatVersion = 3;

        // The floor a new document gets. Maps are user data and must survive
        // older and newer engines, so this stays 1; a document that needs a
        // newer reader raises its own minimum.
        public const int MinimumReadableMapVersion = 1;

        // Declared by a document carrying a light shape or an entity payload.
        // An older editor would drop those on its next save.
        public const int LightShapeMapVersion = 2;
        public const int EntityMapVersion = 3;

        // Versioned like the map: authored by a person.
        public const int ProjectFormatVersion = 1;
        public const int MinimumReadableProjectVersion = 1;

        /// <summary>
        /// Version of compiled geometry: what the CSG compiler emits and what a
        /// vertex layout is made of. Bump by hand when either changes; a stale
        /// pack fails as a misread vertex buffer, not an exception.
        /// </summary>
        public const uint GeometryFormatVersion = 2;

        /// <summary>
        /// Version of the .scmap container, enforced on read as an exact match.
        /// Raise it when the container layout moves.
        /// </summary>
        public const ushort CompiledMapFormatVersion = 1;

        /// <summary>Version of the .spack container a cook writes.</summary>
        public const ushort PackFormatVersion = 1;

        /// <summary>
        /// The oldest reader that can open a pack this engine writes. A pack
        /// whose payloads mean something new raises its own floor.
        /// </summary>
        public const ushort MinimumReadablePackVersion = 1;

        // Must match CompiledShaderFile.FormatVersion to load.
        // 2: the pipeline blob carries vertex input reflection and the
        // generated instanced vertex stage.
        public const ushort ShaderFormatVersion = 2;
    }
}
