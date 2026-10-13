using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scene;
using System;
using System.Collections.Generic;
using System.Numerics;

namespace SpectraEngine.Core.Scripting;

/// <summary>
/// What a script reads with <c>GetAttribute</c>: a keyvalue of the entity a
/// node carries, as a value of the type the entity's class gives it. It
/// reads what the level was built with, and nothing a running entity changed.
/// </summary>
public static class AuthoredAttributes
{
    /// <summary>
    /// Reads the keyvalue called <paramref name="name"/>. One the class
    /// declares is the value on the node when its type can read that, and
    /// the class's own value otherwise, which is what the entity runs with.
    /// One only the node has is its text.
    /// </summary>
    /// <param name="node">The node asked.</param>
    /// <param name="classes">The entity classes of the level, whose schemas type a keyvalue.</param>
    /// <param name="name">The keyvalue's name. Names are case sensitive.</param>
    /// <param name="type">What the value means. A colour is linear, and an angle is in degrees.</param>
    /// <param name="value">The value. None for a node reference that is empty.</param>
    /// <returns>
    /// False when the node carries no entity, or neither the node nor its
    /// class has the name.
    /// </returns>
    public static bool TryRead(
        SceneNode node, EntityCatalog classes, ReadOnlySpan<char> name, out KeyvalueType type, out ParameterValue value)
    {
        ArgumentNullException.ThrowIfNull(node);
        ArgumentNullException.ThrowIfNull(classes);

        type = KeyvalueType.String;
        value = ParameterValue.None;
        if (node.Entity is not { ClassName.Length: > 0 } entity)
            return false;

        string? authored = Authored(entity, name);
        if (classes.TryGetSchema(entity.ClassName, out EntitySchema? schema) &&
            TryFindDeclared(schema, name, out KeyvalueDescriptor declared))
        {
            type = declared.Type;
            return (authored is not null && TryParse(type, authored, out value))
                || TryParse(type, declared.Default, out value);
        }

        if (authored is null)
            return false;

        value = ParameterValue.Of(authored);
        return true;
    }

    // The first match, as EntityData.TryGetValue reads it.
    private static string? Authored(EntityData entity, ReadOnlySpan<char> name)
    {
        List<KeyValuePair<string, string>> keyvalues = entity.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            if (name.SequenceEqual(keyvalues[i].Key))
                return keyvalues[i].Value;
        }

        return null;
    }

    private static bool TryFindDeclared(EntitySchema schema, ReadOnlySpan<char> name, out KeyvalueDescriptor declared)
    {
        IReadOnlyList<KeyvalueDescriptor> keyvalues = schema.Keyvalues;
        for (int i = 0; i < keyvalues.Count; i++)
        {
            if (name.SequenceEqual(keyvalues[i].Name))
            {
                declared = keyvalues[i];
                return true;
            }
        }

        declared = default;
        return false;
    }

    private static bool TryParse(KeyvalueType type, string? text, out ParameterValue value)
    {
        value = ParameterValue.None;
        if (text is null)
            return false;

        switch (type)
        {
            case KeyvalueType.Bool when KeyvalueWire.TryParseBool(text, out bool flag):
                value = ParameterValue.Of(flag);
                return true;

            case KeyvalueType.Int when KeyvalueWire.TryParseInt(text, out int whole):
                value = ParameterValue.Of(whole);
                return true;

            case KeyvalueType.Flags when KeyvalueWire.TryParseFlags(text, out uint bits):
                value = ParameterValue.Of(bits);
                return true;

            // Through the wire's own reader, which gives the number the text
            // spells and not the float widened.
            case KeyvalueType.Float or KeyvalueType.Distance:
                return ParameterText.TryParse(ParameterType.Of(KeyvalueType.Float), text, out value);

            case KeyvalueType.Vec3 or KeyvalueType.Color or KeyvalueType.Angles
                when KeyvalueWire.TryParseVec3(text, out Vector3 three):
                value = ParameterValue.Of(three);
                return true;

            case KeyvalueType.NodeRef when text.Length == 0:
                return true;

            case KeyvalueType.NodeRef when KeyvalueWire.TryParseNodeRef(text, out Guid nodeId):
                value = ParameterValue.Of(nodeId);
                return true;

            default:
                return TryKeepText(type, text, out value);
        }
    }

    // A type a script has no value for reads as its text: a path, a choice,
    // a name, and two or four numbers.
    private static bool TryKeepText(KeyvalueType type, string text, out ParameterValue value)
    {
        bool isText = type is KeyvalueType.String or KeyvalueType.TargetName or KeyvalueType.Choices
            or KeyvalueType.AssetModel or KeyvalueType.AssetMaterial or KeyvalueType.AssetTexture
            or KeyvalueType.AssetSound or KeyvalueType.Vec2 or KeyvalueType.Vec4;

        value = isText && KeyvalueWire.IsWellFormed(type, text) ? ParameterValue.Of(text) : ParameterValue.None;
        return value.Kind != ParameterValueKind.None;
    }
}
