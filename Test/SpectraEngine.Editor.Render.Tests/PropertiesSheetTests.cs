using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Avalonia.VisualTree;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Core.Scene;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Rasterises the property panel's sections so a person can look at them: Now,
/// Sends and Receives for one entity, and the rows of a script.
/// </summary>
[Collection(RibbonSessionCollection.Name)]
public sealed class PropertiesSheetTests(RibbonSession session)
{
    [Fact]
    public void The_wiring_sections_rasterise_into_a_sheet()
    {
        session.On(() =>
        {
            var panelModel = new PropertyPanelModel(_ => { }, _ => { }, _ => { });
            var model = new ShellModel { Properties = panelModel };

            panelModel.Apply([], 1, new EntityPanelInfo
            {
                NodeId = Guid.NewGuid(),
                ClassName = "logic_relay",
                IsKnown = true,
                Outputs = ["OnTrigger", "OnSpawn"],
                Connections =
                [
                    new EntityConnectionInfo(
                        new EntityConnection("OnTrigger", "VaultDoor", "Open", "", 0f, 1), true),
                    new EntityConnectionInfo(
                        new EntityConnection("OnTrigger", "VaultDor", "Close", "", 0f, EntityConnection.Infinite), false),
                ],
                Targets = [new EntityTargetInfo("VaultDoor", "func_door")],
                Incoming =
                [
                    new EntityIncomingInfo(Guid.NewGuid(), "Presses", "OnHitMax", "Trigger"),
                    new EntityIncomingInfo(Guid.NewGuid(), "A button with a long name", "OnPressed", "Trigger"),
                ],
                State = [new("enabled", "1"), new("triggers", "1")],
            });

            var panel = new PropertiesPanel { DataContext = model };
            var window = new Window { Content = panel, Width = 308, Height = 760 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap? frame = window.GetLastRenderedFrame();

            frame.ShouldNotBeNull();
            frame.PixelSize.Width.ShouldBe(616);

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(Path.Combine(RibbonSheetTests.OutputDirectory, "properties-wiring@2x.png"), quality: null);

            window.Close();
        });
    }

    [Fact]
    public void A_wire_whose_parameter_its_input_cannot_take_says_so_under_the_wire()
    {
        session.On(() =>
        {
            var panelModel = new PropertyPanelModel(_ => { }, _ => { }, _ => { }) { Schemas = TypedClasses() };

            panelModel.Apply([], 1, new EntityPanelInfo
            {
                NodeId = Guid.NewGuid(),
                ClassName = "func_button",
                IsKnown = true,
                Outputs = ["OnPressed"],
                Connections =
                [
                    Wire("Score", "SetValue", "five"),
                    Wire("Score", "SetValue", "5"),
                    Wire("VaultDoor", "Open", "now"),
                    Wire("Score", "SetValue", ""),
                ],
                Targets = [new EntityTargetInfo("Score", "math_counter"), new EntityTargetInfo("VaultDoor", "func_door")],
            });

            var panel = new PropertiesPanel { DataContext = new ShellModel { Properties = panelModel } };
            var window = new Window { Content = panel, Width = 308, Height = 760 };
            window.SetRenderScaling(2.0);
            window.Show();
            Dispatcher.UIThread.RunJobs();

            // One line under each wire but the second, in the colour of the
            // line about a target nothing answers to.
            Shown<TextBlock>(panel)
                .Where(text => text.Classes.Contains("wirewarn"))
                .Select(text => text.Text)
                .ShouldBe(
                [
                    "SetValue takes a number, not 'five'",
                    "Open takes no parameter",
                    "SetValue needs a number",
                ]);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap frame = window.GetLastRenderedFrame().ShouldNotBeNull();

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(
                Path.Combine(RibbonSheetTests.OutputDirectory, "properties-wire-parameter@2x.png"),
                new PngBitmapEncoderOptions());

            window.Close();
        });
    }

    [Fact]
    public void A_module_script_at_the_inspectors_usual_width_rasterises_into_a_sheet()
    {
        session.On(() =>
        {
            const string File = "Scripts/Doors/vault_door.luau";
            var node = new SceneNode("Opener") { Script = new Script(File) { IsModule = true } };

            (Window window, PropertiesPanel panel, _) =
                PropertyLabelFixture.Open(PropertyLabelFixture.UsualWidth, node);

            // The kind word in the header, the file's name in a cell with Open
            // beside it, a checkbox for each switch. ScriptSheetTests has the rest.
            Shown<TextBlock>(panel).ShouldContain(text => text.Text == "Script" && text.FontSize < 12);
            Shown<TextBlock>(panel).ShouldContain(text => text.Text == "vault_door");
            Shown<TextBlock>(panel).ShouldContain(text => text.Text == "Open");
            Shown<CheckBox>(panel).Select(box => box.IsChecked).ShouldBe([true, true]);

            AvaloniaHeadlessPlatform.ForceRenderTimerTick(1);
            WriteableBitmap frame = window.GetLastRenderedFrame().ShouldNotBeNull();

            Directory.CreateDirectory(RibbonSheetTests.OutputDirectory);
            frame.Save(
                Path.Combine(RibbonSheetTests.OutputDirectory, "properties-script@2x.png"),
                new PngBitmapEncoderOptions());

            window.Close();
        });
    }

    // A button whose press sends nothing, a counter that takes a number and a
    // door that takes none.
    private static EntitySchemaCatalog TypedClasses() => EntitySchemaCatalog.LoadFromSentDef(SentDef.Write(
    [
        new EntitySchema("func_button", outputs: [new EntityPort("OnPressed", ParameterType.None)]),
        new EntitySchema(
            "math_counter", inputs: [new EntityPort("SetValue", ParameterType.Of(KeyvalueType.Float))]),
        new EntitySchema("func_door", inputs: [new EntityPort("Open", ParameterType.None)]),
    ]));

    private static EntityConnectionInfo Wire(string target, string input, string parameter) => new(
        new EntityConnection("OnPressed", target, input, parameter, 0f, EntityConnection.Infinite),
        TargetResolves: true);

    private static T[] Shown<T>(PropertiesPanel panel) where T : Control =>
        [.. panel.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];
}
