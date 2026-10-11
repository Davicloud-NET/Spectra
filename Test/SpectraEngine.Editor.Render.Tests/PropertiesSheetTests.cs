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

    private static T[] Shown<T>(PropertiesPanel panel) where T : Control =>
        [.. panel.GetVisualDescendants().OfType<T>().Where(control => control.IsEffectivelyVisible)];
}
