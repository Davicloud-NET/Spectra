using System.Text.RegularExpressions;
using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Entities;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// What the site's script reference is written from, held to the script
/// that writes it: the format it reads, and the words of an entity class
/// it takes the class's group from. The script itself runs under Node,
/// which no test here starts.
/// </summary>
public sealed partial class ScriptReferenceInputTests
{
    private static readonly string Generator = File.ReadAllText(Path.Combine(
            BindingSourceTests.RepositoryRoot(), "site", "scripts", "script-api.mjs"))
        .Replace("\r\n", "\n", StringComparison.Ordinal);

    // One line of the generator's table: a group and the page that says what its classes do.
    [GeneratedRegex(@"^\t(?<group>\w+): '(?<page>/reference/[a-z-]+/)',$", RegexOptions.Multiline)]
    private static partial Regex GroupPage();

    // The words the generator reads a class's group out of.
    [GeneratedRegex(@"^.+\. An entity class of the group (?<group>.+)\.$")]
    private static partial Regex GroupWords();

    // The built-in classes as a project's schema file hands them over.
    private static ScriptApiDescription Api => LuauScriptApi.Description.WithEntityClasses(
        EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(BuiltinEntities.Schemas)).Schemas);

    private static Dictionary<string, string> Pages =>
        GroupPage().Matches(Generator).ToDictionary(match => match.Groups["group"].Value, match => match.Groups["page"].Value);

    [Fact]
    public void The_generator_reads_the_format_the_engine_writes()
    {
        Generator.ShouldContain(
            $"const FORMAT = {ScriptApiJson.Format};",
            Case.Sensitive,
            "ScriptApiJson.Format was raised. Read what changed in the JSON, bring site/scripts/script-api.mjs up " +
            "to it, and then raise its FORMAT.");
    }

    [Fact]
    public void The_generator_takes_a_group_out_of_the_words_an_entity_class_is_described_in()
    {
        Generator.ShouldContain("/An entity class of the group (.+)\\.$/");

        foreach (ScriptClassDescription described in Api.EntityClasses)
        {
            Match words = GroupWords().Match(described.Summary);
            words.Success.ShouldBeTrue($"{described.Name} is described as \"{described.Summary}\"");
            Pages.ShouldContainKey(
                words.Groups["group"].Value,
                $"{described.Name} is of a group the generator has no page for, so its page links to no reference");
        }
    }

    [Fact]
    public void The_page_a_group_is_sent_to_has_a_heading_for_each_class_of_the_group()
    {
        Pages.ShouldNotBeEmpty("the generator's table of groups was not found, or the pattern no longer matches it");

        foreach (ScriptClassDescription described in Api.EntityClasses)
        {
            string route = Pages[GroupWords().Match(described.Summary).Groups["group"].Value];
            SitePage page = SitePage.At(route.Trim('/') + ".md");

            page.Text.ShouldContain(
                $"\n## {described.Name}\n",
                Case.Sensitive,
                $"the reference of {described.Name} links to {route}#{described.Name}, and that page has no such heading");
        }
    }
}
