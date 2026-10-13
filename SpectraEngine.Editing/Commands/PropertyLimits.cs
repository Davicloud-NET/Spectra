using SpectraEngine.Core.Assets;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scripting;
using System;

namespace SpectraEngine.Editing.Commands;

/// <summary>
/// What a property accepts, shared by the editor's refusal and the panel's
/// message.
/// </summary>
// The Light setters and Script's path throw on these values. Strings complete
// "Not applied: Range must be ...".
public static class PropertyLimits
{
    /// <summary>Why <paramref name="value"/> would be refused, or null when it is fine.</summary>
    public static string? Refusal(PropertyId id, float value) => id switch
    {
        PropertyId.LightRange when !float.IsFinite(value) || value <= 0f => "greater than 0",

        PropertyId.LightIntensity when !float.IsFinite(value) || value < 0f => "0 or more",

        _ => null,
    };

    /// <summary>Why <paramref name="text"/> would be refused, or null when it is fine.</summary>
    public static string? Refusal(PropertyId id, string text) => id switch
    {
        PropertyId.ScriptFile when !IsContentPath(text) => "a path inside this project's Assets folder",

        _ => null,
    };

    /// <summary>Why a node cannot be built with this tag, as a sentence, or null when it can.</summary>
    // The rule's own words count bytes, which suits a file and a script. A
    // builder cannot count them, so the panel gets words of its own.
    public static string? TagRefusal(string tag)
    {
        if (TagName.IsValid(tag))
            return null;

        return tag.Length == 0 ? "A tag cannot be empty." : TextRefusal("tag", "A tag", tag, TagLimits.MaxBytes);
    }

    /// <summary>Why an attribute cannot have this name, as a sentence, or null when it can.</summary>
    public static string? AttributeNameRefusal(string name) =>
        AttributeName.IsValid(name) ? null : AttributeName.Refusal(name) + ".";

    /// <summary>Why an attribute cannot hold this value, as a sentence, or null when it can.</summary>
    public static string? AttributeValueRefusal(in MemberValue value)
    {
        if (AttributeValueRule.Refusal(in value) is not { } whyNot)
            return null;

        return value.Kind == MemberValueKind.Text
            ? TextRefusal("text", "An attribute", value.Text, AttributeLimits.MaxTextBytes)
            : whyNot;
    }

    // Text is refused for half a surrogate pair, or else for its length.
    private static string TextRefusal(string what, string holder, string text, int maxBytes) =>
        TextRule.IsWellFormed(text)
            ? $"This {what} is too long. {holder} has room for {maxBytes} letters, or fewer with accents and symbols."
            : $"This {what} has a character that cannot be saved.";

    /// <summary>What to say when a node has no room for one more tag.</summary>
    public static string FullOfTags => $"A node has at most {TagLimits.MaxPerNode} tags.";

    /// <summary>What to say when a node has no room for one more attribute.</summary>
    public static string FullOfAttributes => $"A node holds at most {AttributeLimits.MaxPerNode} attributes.";

    /// <summary>
    /// What to say of a name the entity on a node answers to already. A
    /// script reads that setting as an attribute, so a second value under
    /// the name would be one the entity never sees.
    /// </summary>
    public static string TakenByEntity(string name) =>
        $"'{name}' is a setting of the entity already, and scripts read it as an attribute. Pick another name.";

    /// <summary>
    /// What to say when the level's root is selected. A map saves nothing of
    /// the root, so a tag or an attribute on it would be gone after a save.
    /// </summary>
    public static string RootKeepsNone =>
        "The level's root is not saved with the level, so it keeps no tags and no attributes. " +
        "Mark the nodes under it.";

    /// <summary>
    /// Why a new value of an attribute stays off some of the selected nodes,
    /// one sentence for each reason. Empty when nothing stands in the way.
    /// </summary>
    public static string LeftOut(AttributeRoom why, string name)
    {
        string said = string.Empty;
        if ((why & AttributeRoom.Root) != 0)
            said = "The level's root keeps no attributes.";

        if ((why & AttributeRoom.Full) != 0)
            said = Joined(said, FullOfAttributes);

        if ((why & AttributeRoom.Entity) != 0)
            said = Joined(said, $"An entity among them has a setting called '{name}' already.");

        return said;
    }

    private static string Joined(string said, string more) => said.Length == 0 ? more : $"{said} {more}";

    /// <summary>What this cell accepts, for the message shown when input was rejected.</summary>
    public static string Expected(PropertyId id, PropertyKind kind)
    {
        if (kind == PropertyKind.Color) return "#RRGGBB";

        return id switch
        {
            PropertyId.LightRange => "a number greater than 0",
            PropertyId.LightIntensity => "a number of 0 or more",
            _ => "a number",
        };
    }

    private static bool IsContentPath(string text)
    {
        try
        {
            ContentRoot.NormalizeRelativePath(text);
            return true;
        }
        catch (ArgumentException)
        {
            // Empty, rooted, or it leaves the content root.
            return false;
        }
    }
}
