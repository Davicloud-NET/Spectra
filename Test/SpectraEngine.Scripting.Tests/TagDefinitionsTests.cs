using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The definitions file for luau-lsp: tags and attributes on every node,
/// and the service that lists a tag's nodes, with its overload in GetService.
/// </summary>
public sealed class TagDefinitionsTests
{
    private static readonly string File = LuauDefinitions.Write(LuauScriptApi.Description);

    [Fact]
    public void Every_node_sets_and_reads_attributes_and_hears_of_a_change()
    {
        string node = BlockOf("declare extern type Instance with");

        node.ShouldContain(
            "\n    function SetAttribute(self, attribute: string, " +
            "value: (string | boolean | number | Vector3 | CFrame | Color3)?): ()\n");
        node.ShouldContain("\n    function GetAttribute(self, attribute: string): any\n");
        node.ShouldContain("\n    function GetAttributes(self): { [string]: any }\n");
        node.ShouldContain("\n    read AttributeChanged: RBXScriptSignal<string>\n");
        node.ShouldContain("\n    function GetAttributeChangedSignal(self, attribute: string): RBXScriptSignal<()>\n");
    }

    [Fact]
    public void Every_node_takes_and_loses_a_tag()
    {
        string node = BlockOf("declare extern type Instance with");

        node.ShouldContain("\n    function AddTag(self, tag: string): ()\n");
        node.ShouldContain("\n    function RemoveTag(self, tag: string): ()\n");
        node.ShouldContain("\n    function HasTag(self, tag: string): boolean\n");
        node.ShouldContain("\n    function GetTags(self): {string}\n");
    }

    [Fact]
    public void The_service_lists_a_tags_nodes_and_hands_out_its_two_signals()
    {
        string service = BlockOf("declare extern type CollectionService with");

        service.ShouldContain("\n    function GetTagged(self, tag: string): {Instance}\n");
        service.ShouldContain("\n    function GetInstanceAddedSignal(self, tag: string): RBXScriptSignal<Instance>\n");
        service.ShouldContain("\n    function GetInstanceRemovedSignal(self, tag: string): RBXScriptSignal<Instance>\n");
        service.ShouldContain("\n    function AddTag(self, instance: Instance, tag: string): ()\n");
        service.ShouldContain("\n    function RemoveTag(self, instance: Instance, tag: string): ()\n");
        service.ShouldContain("\n    function HasTag(self, instance: Instance, tag: string): boolean\n");
        service.ShouldContain("\n    function GetTags(self, instance: Instance): {string}\n");
        service.ShouldNotContain("[string]");
    }

    [Fact]
    public void GetService_gives_the_service_its_own_type()
    {
        BlockOf("declare extern type DataModel with")
            .ShouldContain("\n    function GetService(self, service: \"CollectionService\"): CollectionService\n");
    }

    // A summary says what a Roblox developer would not guess.
    [Fact]
    public void The_words_of_SetAttribute_say_that_an_entity_never_sees_what_a_script_set()
    {
        ScriptFunctionDescription set = LuauScriptApi.Description.FindClass("Instance").ShouldNotBeNull()
            .Methods.Single(method => method.Name == "SetAttribute");

        set.Summary.ShouldContain("the entity never sees it");
        set.Summary.ShouldContain("Stop takes away what scripts set");
        File.ShouldContain(set.Summary);
    }

    [Fact]
    public void The_words_of_SetAttribute_say_the_rule_for_a_name_as_the_rule_itself_says_it()
    {
        ScriptFunctionDescription set = Method("Instance", "SetAttribute");

        set.Summary.ShouldContain(AttributeName.Rule.Replace("An attribute's name", "A name") + ".");
        File.ShouldContain(set.Summary);
    }

    [Fact]
    public void The_words_of_GetTags_say_that_the_tags_come_sorted()
    {
        ScriptFunctionDescription ofNode = Method("Instance", "GetTags");
        ScriptFunctionDescription ofService = Method("CollectionService", "GetTags");

        ofNode.Summary.ShouldContain("sorted by the bytes of their text");
        ofService.Summary.ShouldContain("sorted");
        File.ShouldContain(ofNode.Summary);
        File.ShouldContain(ofService.Summary);
    }

    [Fact]
    public void The_words_of_Destroy_say_that_the_node_keeps_its_tags_and_attributes()
    {
        ScriptFunctionDescription destroy = Method("Instance", "Destroy");

        destroy.Summary.ShouldContain("It keeps its tags and attributes, as in Roblox");
        File.ShouldContain(destroy.Summary);
    }

    private static ScriptFunctionDescription Method(string className, string name) =>
        LuauScriptApi.Description.FindClass(className).ShouldNotBeNull().Methods.Single(method => method.Name == name);

    // From the line that opens a declaration to the line that closes it.
    private static string BlockOf(string opening)
    {
        int at = File.IndexOf(opening + "\n", StringComparison.Ordinal);
        at.ShouldBeGreaterThanOrEqualTo(0, opening);

        const string Close = "\nend\n";
        return File[at..(File.IndexOf(Close, at, StringComparison.Ordinal) + Close.Length)];
    }
}
