using SpectraEngine.Core.Scene;
using SpectraEngine.Core.Scripting;

namespace SpectraEngine.Scripting.Tests;

// Changes to a level's tree, drawn from a seed: a node made, a clone, a
// node destroyed, a made node moved. Each names any node of the level or
// any node made so far, so many are refused. It counts those that took.
internal sealed class RandomTree(RandomWrites random, TreeWrites tree, SceneNode[] level)
{
    private static readonly string[] Classes =
        ["Part", "Folder", "PointLight", "SpotLight", "SurfaceLight", "DirectionalLight", "DiscLight"];

    private readonly List<SceneNode> _made = [];
    private readonly List<NodeCopy> _copies = [];
    private readonly List<SceneNode> _destroyed = [];

    public int Made { get; private set; }

    public int Cloned { get; private set; }

    public int Destroyed { get; private set; }

    public int Moved { get; private set; }

    // A node of the level as it was built, or one made since.
    public SceneNode Any()
    {
        int at = random.Pick(level.Length + _made.Count);
        return at < level.Length ? level[at] : _made[at - level.Length];
    }

    public void Step()
    {
        switch (random.Pick(8))
        {
            case 0 or 1:
                if (tree.TryMake(Classes[random.Pick(Classes.Length)], out SceneNode? made, out _))
                {
                    Made++;
                    Keep(made);
                }

                break;

            case 2 or 3:
                if (tree.TryClone(Any(), _copies, out SceneNode? copy, out _))
                {
                    Cloned++;
                    Keep(copy);
                }

                break;

            case 4:
                if (tree.TryDestroy(Any(), _destroyed, out _) && _destroyed.Count > 0)
                    Destroyed++;

                break;

            default:
                if (_made.Count > 0 && Move(_made[random.Pick(_made.Count)]))
                    Moved++;

                break;
        }
    }

    // A new node gets a parent at once, when it takes the one that is drawn.
    private void Keep(SceneNode node)
    {
        _made.Add(node);
        Move(node);
    }

    // Under any node, or one time in six out of every tree.
    private bool Move(SceneNode node)
    {
        SceneNode? parent = random.Pick(6) == 0 ? null : Any();
        return !ReferenceEquals(node.Parent, parent) && tree.TrySetParent(node, parent, out _);
    }
}
