using System.Numerics;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editing.Input;

namespace SpectraEngine.Editing.Viewport;

// What the cursor is over: a face of something solid, or the icon of a light
// or a point entity where it is drawn.
internal static class CursorPick
{
    // Finds the node under the cursor, and the plane of the face when it is a
    // brush. Solid things further than the reach are not found.
    public static bool TryPick(
        Scene scene, SceneIcons icons, in EditorInputFrame frame, float reach,
        out SceneNode? node, out int planeIndex)
    {
        node = null;
        planeIndex = -1;
        if (frame.ViewportSize.X <= 0f || frame.ViewportSize.Y <= 0f)
            return false;

        Ray3 ray = scene.Camera.ScreenPointToRay(frame.CursorPosition, frame.ViewportSize);

        // Editor picking ignores CanQuery: anything visible must be selectable.
        bool geometry = scene.Raycast(in ray, out SceneRaycastHit hit, SceneQueryFilter.EditorPicking, reach);

        // Lights and point entities are not in the spatial index (they would
        // become collidable), so their icons are picked separately.
        if (!icons.TryPick(scene, scene.Camera, in ray, frame.ViewportSize, out SceneNode? icon, out float iconDistance))
        {
            node = geometry ? hit.Node : null;
            planeIndex = geometry ? hit.PlaneIndex : -1;
            return geometry;
        }

        // The icon wins unless geometry is nearer by more than the icon's
        // radius. It draws on top, and a lamp flush against a ceiling would
        // otherwise be unclickable.
        if (geometry && icon is not null)
        {
            float slack = LightPicking.WorldRadius(scene.Camera, frame.ViewportSize, icon.WorldPosition);
            bool isBehind = iconDistance > hit.Distance + slack;

            // An icon that was moved along a wall can stand behind it while
            // its node is in front. It is drawn all the same, so the wall
            // wins only when it hides the node too.
            if (isBehind && icons.TryFindMoved(scene, icon, out _))
                isBehind = IsHidden(scene, icon.WorldPosition, in ray, slack);

            if (isBehind)
            {
                node = hit.Node;
                planeIndex = hit.PlaneIndex;
                return true;
            }
        }

        node = icon;
        return true;
    }

    // Whether geometry stands between the eye and this point, nearer than
    // the point by more than the slack. The cursor's ray says which way a
    // flat view looks.
    private static bool IsHidden(Scene scene, Vector3 point, in Ray3 cursor, float slack)
    {
        Camera camera = scene.Camera;
        Vector3 from = camera.ProjectionKind == CameraProjectionKind.Orthographic
            ? point - (cursor.Direction * Vector3.Dot(point - cursor.Origin, cursor.Direction))
            : camera.Position;

        float distance = Vector3.Distance(from, point);
        if (distance <= 0f)
            return false;

        var sight = new Ray3(from, (point - from) / distance);
        return scene.Raycast(in sight, out SceneRaycastHit block, SceneQueryFilter.EditorPicking, distance) &&
               block.Distance + slack < distance;
    }
}
