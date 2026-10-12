using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using SpectraEngine.Core.Scripting;
using SpectraEngine.Core.Scripting.Api;
using SpectraEngine.Scripting.Luau.Runtime;

namespace SpectraEngine.Scripting.Tests;

/// <summary>
/// The definitions file luau-lsp reads, and the JSON the docs site reads.
/// Both are written from the description and from nothing else.
/// </summary>
public sealed class LuauDefinitionsTests
{
    // One of every construct the writer has. Each line here is a line a
    // correction to the syntax would change.
    private const string SmallFile = """
        -- The script API of Spectra Engine, for luau-lsp.
        -- Written by the engine from its own description. Do not edit.

        --- A node.
        declare extern type Instance with
            --- Its name.
            Name: string
            [string]: Instance
        end

        --- Two numbers.
        declare extern type Point with
            --- The first.
            read X: number
            --- Both.
            function Split(self): (number, number)
            function __add(self, other: Point): Point
            function __unm(self): Point
        end

        --- A signal.
        export type Signal<T... = ...any> = {
            --- Calls the function.
            Connect: (self: Signal<T...>, callback: (T...) -> ()) -> Connection,
            --- Waits.
            Wait: (self: Signal<T...>) -> T...,
        }

        --- A door.
        declare extern type Door extends Instance with
            --- How fast it moves.
            read Speed: number
            --- Fires when it is open.
            read Opened: RBXScriptSignal<Instance?, number>
            read Closed: RBXScriptSignal<()>
            --- Opens it.
            function Open(self): ()
            --- Finds a part.
            function Find(self, name: "Hinge"): Instance
            function Find(self, name: string, ...: any): Instance?
            --- The same as Open.
            --- Deprecated: use Open.
            function open(self): ()
            [string]: Instance
        end

        --- Makes points.
        declare Point: {
            --- The origin.
            zero: Point,
            --- Makes one.
            new: (() -> Point) & ((x: number, y: number) -> Point),
            --- Runs it.
            spawn: (callback: (...any) -> (), ...any) -> (),
        }

        --- The root.
        declare workspace: Instance

        --- Waits.
        @[deprecated {use = "task.wait"}]
        declare function wait(seconds: number?): (number, number)

        --- Warns.
        declare function warn(...: any): ()

        --- Picks.
        declare pick: ((index: number) -> string) & ((name: string) -> number)

        --- Counts.
        --- Deprecated: use Point.new.
        declare count: (() -> number) & ((from: number) -> number)

        """;

    private const string SmallJson = """
        {
          "format": 1,
          "classes": [
            {
              "name": "Point",
              "extends": "",
              "isValue": true,
              "typeParameters": "",
              "childType": "",
              "summary": "Two numbers.",
              "properties": [
                {
                  "name": "X",
                  "type": "number",
                  "summary": "The first.",
                  "canSet": false,
                  "deprecated": ""
                }
              ],
              "methods": [
                {
                  "name": "Split",
                  "summary": "Both.",
                  "deprecated": "",
                  "signatures": [
                    {
                      "parameters": [
                        {
                          "name": "at",
                          "type": "number?"
                        }
                      ],
                      "returns": "(number, number)"
                    }
                  ]
                }
              ],
              "signals": [],
              "operators": [
                {
                  "operator": "+",
                  "right": "Point",
                  "returns": "Point"
                }
              ]
            }
          ],
          "libraries": [],
          "globals": [],
          "functions": []
        }

        """;

    private static ScriptApiDescription Api => LuauScriptApi.Description;

    [Fact]
    public void A_small_description_is_written_byte_for_byte()
    {
        string written = LuauDefinitions.Write(Small());

        written.ShouldBe(SmallFile.ReplaceLineEndings("\n"));
    }

    // Held against text written down here, so every run of the suite is
    // another process that has to write the same bytes.
    [Fact]
    public void The_json_of_a_small_description_is_written_byte_for_byte()
    {
        var api = new ScriptApiDescription
        {
            Classes =
            [
                new()
                {
                    Name = "Point",
                    IsValue = true,
                    Summary = "Two numbers.",
                    Properties = [new() { Name = "X", Type = "number", Summary = "The first." }],
                    Methods = [Function("Split", "Both.", new ScriptSignature([new("at", "number?")], "(number, number)"))],
                    Operators = [new("+", "Point", "Point")],
                },
            ],
        };

        Encoding.UTF8.GetString(ScriptApiJson.Write(api)).ShouldBe(SmallJson.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void A_deprecated_function_with_one_shape_carries_the_attribute_and_one_with_several_a_line_of_words()
    {
        string written = LuauDefinitions.Write(Small());

        written.ShouldContain("--- Waits.\n@[deprecated {use = \"task.wait\"}]\ndeclare function wait(");
        written.ShouldContain("--- Counts.\n--- Deprecated: use Point.new.\ndeclare count: ");
        written.ShouldNotContain("}]\ndeclare count");
    }

    [Fact]
    public void What_to_write_in_place_of_an_old_spelling_is_a_string_of_Luau()
    {
        var api = new ScriptApiDescription
        {
            Functions = [Function("old", "", new ScriptSignature([], "")) with { Deprecated = "the \"new\" \\ one" }],
        };

        LuauDefinitions.Write(api).ShouldContain("@[deprecated {use = \"the \\\"new\\\" \\\\ one\"}]\ndeclare function old(): ()\n");
    }

    [Fact]
    public void Words_of_several_lines_stay_inside_the_comment()
    {
        var api = new ScriptApiDescription
        {
            Globals = [new() { Name = "level", Type = "Instance", Summary = "The level.\r\nIt has every node." }],
        };

        LuauDefinitions.Write(api).ShouldContain("\n--- The level.\n--- It has every node.\ndeclare level: Instance\n");
    }

    [Fact]
    public void A_property_a_script_cannot_set_is_read_only_to_the_type_checker()
    {
        string written = LuauDefinitions.Write(Api);

        BlockOf(written, "declare extern type Instance ").ShouldContain("\n    Position: Vector3\n");
        BlockOf(written, "declare extern type Instance ").ShouldContain("\n    read ClassName: string\n");
        BlockOf(written, "declare extern type CFrame ").ShouldContain("\n    read X: number\n");
        BlockOf(written, "declare extern type RBXScriptConnection ").ShouldContain("\n    read Connected: boolean\n");
        BlockOf(written, "declare extern type Character ").ShouldContain("\n    WalkSpeed: number\n");
        BlockOf(written, "declare extern type Character ").ShouldContain("\n    read Position: Vector3\n");

        foreach (ScriptClassDescription described in Api.Classes.Where(described => described.TypeParameters.Length == 0))
        {
            string body = BlockOf(written, $"declare extern type {described.Name} ");
            foreach (ScriptPropertyDescription property in described.Properties)
                body.ShouldContain($"\n    {(property.CanSet ? "" : "read ")}{property.Name}: ", Case.Sensitive, described.Name);
        }
    }

    [Fact]
    public void A_class_with_type_parameters_keeps_its_signals_and_the_type_of_its_children()
    {
        var api = new ScriptApiDescription
        {
            Classes =
            [
                new()
                {
                    Name = "Folder",
                    TypeParameters = "T",
                    ChildType = "T",
                    Signals = [new() { Name = "Added", Summary = "Fires for a new child.", Parameters = [new("child", "T")] }],
                },
            ],
        };

        LuauDefinitions.Write(api).ShouldEndWith("""

            export type Folder<T> = {
                --- Fires for a new child.
                Added: RBXScriptSignal<T>,
                [string]: T,
            }

            """.ReplaceLineEndings("\n"));
    }

    // A table type takes neither, and leaving them out would promise a
    // script less than the binding gives.
    // Luau's checker finds a property along the classes one extends and an
    // indexer only on the class itself. A value typed Part would have no child.
    [Fact]
    public void A_class_says_the_type_of_its_children_itself_when_a_class_it_extends_has_one()
    {
        var api = new ScriptApiDescription
        {
            Classes =
            [
                new() { Name = "Hinge", Extends = "Part" },
                new() { Name = "Instance", ChildType = "Instance" },
                new() { Name = "Part", Extends = "Instance" },
                new() { Name = "Signal" },
            ],
        };

        LuauDefinitions.Write(api).ShouldEndWith("""

            declare extern type Instance with
                [string]: Instance
            end

            declare extern type Part extends Instance with
                [string]: Instance
            end

            declare extern type Signal with
            end

            declare extern type Hinge extends Part with
                [string]: Instance
            end

            """.ReplaceLineEndings("\n"));

        api.ChildTypeOf("Hinge").ShouldBe("Instance");
        api.ChildTypeOf("Signal").ShouldBe("");
        api.ChildTypeOf("Nobody").ShouldBe("");
    }

    [Fact]
    public void A_class_with_type_parameters_and_an_operator_or_a_base_is_refused_by_name()
    {
        ScriptClassDescription generic = new() { Name = "Folder", TypeParameters = "T" };
        var withOperator = new ScriptApiDescription { Classes = [generic with { Operators = [new("+", "T", "T")] }] };
        var withBase = new ScriptApiDescription { Classes = [generic with { Extends = "Instance" }] };

        Should.Throw<ArgumentException>(() => LuauDefinitions.Write(withOperator)).Message.ShouldContain("'Folder'");
        Should.Throw<ArgumentException>(() => LuauDefinitions.Write(withBase)).Message.ShouldContain("'Folder'");
    }

    [Fact]
    public void The_file_of_the_real_api_ends_every_line_with_a_line_feed_alone()
    {
        string written = LuauDefinitions.Write(Api);

        written.ShouldNotContain('\r');
        written.ShouldEndWith("\n");
    }

    [Fact]
    public void The_file_of_the_real_api_declares_every_class_with_every_member()
    {
        string written = LuauDefinitions.Write(Api);

        foreach (ScriptClassDescription described in Api.Classes)
        {
            string body = BlockOf(written, described.TypeParameters.Length > 0
                ? $"export type {described.Name}<"
                : $"declare extern type {described.Name} ");

            foreach (ScriptPropertyDescription property in described.Properties)
                body.ShouldMatch($@"\n    (read )?{property.Name}: {Regex.Escape(property.Type)}", described.Name);
            foreach (ScriptSignalDescription signal in described.Signals)
                body.ShouldContain($"\n    read {signal.Name}: RBXScriptSignal<", Case.Sensitive, described.Name);
            foreach (ScriptFunctionDescription method in described.Methods)
                body.ShouldMatch($@"\n    (function {method.Name}\(self|{method.Name}: \(self)", described.Name);
        }
    }

    [Fact]
    public void The_file_of_the_real_api_declares_every_library_and_every_global()
    {
        string written = LuauDefinitions.Write(Api);

        foreach (ScriptLibraryDescription library in Api.Libraries)
        {
            string body = BlockOf(written, $"declare {library.Name}: {{");
            foreach (ScriptPropertyDescription constant in library.Constants)
                body.ShouldContain($"\n    {constant.Name}: {constant.Type},", Case.Sensitive, library.Name);
            foreach (ScriptFunctionDescription function in library.Functions)
                body.ShouldContain($"\n    {function.Name}: (", Case.Sensitive, library.Name);
        }

        foreach (ScriptPropertyDescription global in Api.Globals)
            written.ShouldContain($"\ndeclare {global.Name}: {global.Type}\n");
        foreach (ScriptFunctionDescription function in Api.Functions.Where(function => function.Name != "require"))
            written.ShouldContain($"\ndeclare function {function.Name}(");
    }

    // luau-lsp follows a path to its file through its own require. One
    // declared here would take its place, and every module would be any.
    [Fact]
    public void The_file_leaves_require_to_the_type_checker_and_the_reference_still_has_it()
    {
        ScriptFunctionDescription require = Api.Functions.Single(function => function.Name == "require");

        ScriptSignature shape = require.Signatures.ShouldHaveSingleItem();
        shape.Parameters.ShouldBe([new ScriptParameter("module", "string | Instance")]);
        shape.Returns.ShouldBe("any");
        require.Summary.ShouldContain("nil when it was asked for by path");
        LuauDefinitions.Write(Api).ShouldNotContain("require");

        using JsonDocument json = JsonDocument.Parse(ScriptApiJson.Write(Api));
        NamesIn(json.RootElement, "functions").ShouldContain("require");
    }

    [Fact]
    public void The_file_of_the_real_api_marks_the_old_spellings_as_deprecated()
    {
        string written = LuauDefinitions.Write(Api);

        written.ShouldContain("@[deprecated {use = \"task.wait\"}]\ndeclare function wait(seconds: number?): (number, number)\n");
        written.ShouldContain("@[deprecated {use = \"task.spawn\"}]\ndeclare function spawn(");
        written.ShouldContain("@[deprecated {use = \"task.delay\"}]\ndeclare function delay(");
    }

    [Fact]
    public void A_signal_is_typed_by_what_it_hands_its_listeners()
    {
        string written = LuauDefinitions.Write(Api);

        written.ShouldContain("\n    read Heartbeat: RBXScriptSignal<number>\n");
        written.ShouldContain("\n    read Stepped: RBXScriptSignal<number, number>\n");
        written.ShouldContain("Connect: (self: RBXScriptSignal<T...>, callback: (T...) -> ()) -> RBXScriptConnection,");
        written.ShouldContain("Wait: (self: RBXScriptSignal<T...>) -> T...,");
    }

    [Fact]
    public void The_json_of_the_real_api_holds_every_class_library_and_global()
    {
        using JsonDocument json = JsonDocument.Parse(ScriptApiJson.Write(Api));
        JsonElement root = json.RootElement;

        root.GetProperty("format").GetInt32().ShouldBe(ScriptApiJson.Format);
        NamesIn(root, "classes").ShouldBe(Api.Classes.Select(described => described.Name));
        NamesIn(root, "libraries").ShouldBe(Api.Libraries.Select(library => library.Name));
        NamesIn(root, "globals").ShouldBe(Api.Globals.Select(global => global.Name));
        NamesIn(root, "functions").ShouldBe(Api.Functions.Select(function => function.Name));

        JsonElement instance = root.GetProperty("classes").EnumerateArray()
            .Single(described => described.GetProperty("name").GetString() == "Instance");
        JsonElement position = instance.GetProperty("properties").EnumerateArray()
            .Single(property => property.GetProperty("name").GetString() == "Position");
        position.GetProperty("type").GetString().ShouldBe("Vector3");
        position.GetProperty("canSet").GetBoolean().ShouldBeTrue();
        position.GetProperty("summary").GetString().ShouldBe("Where the node is in the world.");
    }

    [Fact]
    public void An_operator_Luau_does_not_have_is_refused_by_name()
    {
        var wrong = new ScriptApiDescription
        {
            Classes = [new() { Name = "Point", Operators = [new("**", "Point", "Point")] }],
        };

        Should.Throw<ArgumentException>(() => LuauDefinitions.Write(wrong)).Message.ShouldContain("'**'");
    }

    private static string[] NamesIn(JsonElement root, string member) =>
        [.. root.GetProperty(member).EnumerateArray().Select(item => item.GetProperty("name").GetString() ?? "")];

    // The text of one declaration: from its first line to the line that closes it.
    private static string BlockOf(string written, string opening)
    {
        int start = written.IndexOf("\n" + opening, StringComparison.Ordinal);
        start.ShouldBeGreaterThanOrEqualTo(0, $"the file has no line that begins with '{opening}'");

        int end = written.IndexOf(opening.EndsWith('{') || opening.EndsWith('<') ? "\n}\n" : "\nend\n", start, StringComparison.Ordinal);
        end.ShouldBeGreaterThan(start, $"the declaration that begins with '{opening}' is never closed");
        return written[start..end];
    }

    private static ScriptApiDescription Small() => new()
    {
        Classes =
        [
            new()
            {
                Name = "Door",
                Extends = "Instance",
                Summary = "A door.",
                Properties = [new() { Name = "Speed", Type = "number", Summary = "How fast it moves." }],
                Signals =
                [
                    new()
                    {
                        Name = "Opened",
                        Summary = "Fires when it is open.",
                        Parameters = [new("by", "Instance?"), new("seconds", "number")],
                    },
                    new() { Name = "Closed" },
                ],
                Methods =
                [
                    Function("Open", "Opens it.", new ScriptSignature([], "")),
                    Function(
                        "Find",
                        "Finds a part.",
                        new ScriptSignature([new("name", "\"Hinge\"")], "Instance"),
                        new ScriptSignature([new("name", "string"), new("...", "any")], "Instance?")),
                    Function("open", "The same as Open.", new ScriptSignature([], "")) with { Deprecated = "Open" },
                ],
            },
            new()
            {
                Name = "Instance",
                Summary = "A node.",
                ChildType = "Instance",
                Properties =
                [
                    new() { Name = "Name", Type = "string", Summary = "Its name.", Sets = SettableMemberId.Name },
                ],
            },
            new()
            {
                Name = "Point",
                IsValue = true,
                Summary = "Two numbers.",
                Properties = [new() { Name = "X", Type = "number", Summary = "The first." }],
                Methods = [Function("Split", "Both.", new ScriptSignature([], "(number, number)"))],
                Operators = [new("+", "Point", "Point"), new("unm", "", "Point")],
            },
            new()
            {
                Name = "Signal",
                TypeParameters = "T... = ...any",
                Summary = "A signal.",
                Methods =
                [
                    Function(
                        "Connect",
                        "Calls the function.",
                        new ScriptSignature([new("callback", "(T...) -> ()")], "Connection")),
                    Function("Wait", "Waits.", new ScriptSignature([], "T...")),
                ],
            },
        ],
        Libraries =
        [
            new()
            {
                Name = "Point",
                Summary = "Makes points.",
                Constants = [new() { Name = "zero", Type = "Point", Summary = "The origin." }],
                Functions =
                [
                    Function(
                        "new",
                        "Makes one.",
                        new ScriptSignature([], "Point"),
                        new ScriptSignature([new("x", "number"), new("y", "number")], "Point")),
                    Function(
                        "spawn",
                        "Runs it.",
                        new ScriptSignature([new("callback", "(...any) -> ()"), new("...", "any")], "")),
                ],
            },
        ],
        Globals = [new() { Name = "workspace", Type = "Instance", Summary = "The root." }],
        Functions =
        [
            Function("wait", "Waits.", new ScriptSignature([new("seconds", "number?")], "(number, number)"))
                with { Deprecated = "task.wait" },
            Function("warn", "Warns.", new ScriptSignature([new("...", "any")], "")),
            Function(
                "pick",
                "Picks.",
                new ScriptSignature([new("index", "number")], "string"),
                new ScriptSignature([new("name", "string")], "number")),
            Function(
                "count",
                "Counts.",
                new ScriptSignature([], "number"),
                new ScriptSignature([new("from", "number")], "number")) with { Deprecated = "Point.new" },
        ],
    };

    private static ScriptFunctionDescription Function(string name, string summary, params ScriptSignature[] signatures) =>
        new() { Name = name, Summary = summary, Signatures = signatures };
}
