using SpectraEngine.Core.Scene;

namespace SpectraEngine.Scripting.Luau.Runtime;

// Asks the class of a node for a member: a part's class for a part, a spot
// light's for a spot light. A class hands a name it does not declare to the
// class it extends, so each of them ends at Instance. A member declared on
// one class is found on its nodes and on no other.
internal static class NodeMembers
{
    public static int Read(ScriptContext context, LuauStack stack, SceneNode node, int atom) =>
        NodeClass.KindOf(node) switch
        {
            NodeClassKind.Folder => FolderApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.Part => PartApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.MeshPart => MeshPartApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.PointLight => PointLightApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.SpotLight => SpotLightApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.DirectionalLight => DirectionalLightApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.SurfaceLight => SurfaceLightApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.DiscLight => DiscLightApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.Script => ScriptNodeApi.ScriptRead(context, stack, node, atom),
            NodeClassKind.ModuleScript => ModuleScriptApi.ScriptRead(context, stack, node, atom),
            _ => NodeApi.ScriptRead(context, stack, node, atom),
        };

    public static int Write(ScriptContext context, LuauStack stack, SceneNode node, int atom) =>
        NodeClass.KindOf(node) switch
        {
            NodeClassKind.Folder => FolderApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.Part => PartApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.MeshPart => MeshPartApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.PointLight => PointLightApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.SpotLight => SpotLightApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.DirectionalLight => DirectionalLightApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.SurfaceLight => SurfaceLightApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.DiscLight => DiscLightApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.Script => ScriptNodeApi.ScriptWrite(context, stack, node, atom),
            NodeClassKind.ModuleScript => ModuleScriptApi.ScriptWrite(context, stack, node, atom),
            _ => NodeApi.ScriptWrite(context, stack, node, atom),
        };

    public static int Invoke(ScriptContext context, LuauStack stack, SceneNode node, int atom) =>
        NodeClass.KindOf(node) switch
        {
            NodeClassKind.Folder => FolderApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.Part => PartApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.MeshPart => MeshPartApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.PointLight => PointLightApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.SpotLight => SpotLightApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.DirectionalLight => DirectionalLightApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.SurfaceLight => SurfaceLightApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.DiscLight => DiscLightApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.Script => ScriptNodeApi.ScriptInvoke(context, stack, node, atom),
            NodeClassKind.ModuleScript => ModuleScriptApi.ScriptInvoke(context, stack, node, atom),
            _ => NodeApi.ScriptInvoke(context, stack, node, atom),
        };
}
