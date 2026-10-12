using System.Text.Json;
using SpectraEngine.Core.Scripting.Api;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// A property an object sets through a method of its own, as the definitions
/// file and the JSON write it.
/// </summary>
public sealed class LuauDefinitionsSetByTests
{
    [Fact]
    public void A_property_an_object_sets_itself_is_not_read_only_and_the_json_says_so()
    {
        var api = new ScriptApiDescription
        {
            Classes =
            [
                new()
                {
                    Name = "Dial",
                    Properties =
                    [
                        new() { Name = "Level", Type = "number", CanSet = true },
                        new() { Name = "Turns", Type = "number" },
                    ],
                },
            ],
        };

        LuauDefinitions.Write(api).ShouldContain("\n    Level: number\n    read Turns: number\n");

        using JsonDocument json = JsonDocument.Parse(ScriptApiJson.Write(api));
        JsonElement properties = json.RootElement.GetProperty("classes")[0].GetProperty("properties");
        properties[0].GetProperty("canSet").GetBoolean().ShouldBeTrue();
        properties[1].GetProperty("canSet").GetBoolean().ShouldBeFalse();
    }
}
