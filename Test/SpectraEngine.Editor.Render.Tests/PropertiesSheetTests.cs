using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Media.Imaging;
using Avalonia.Threading;

using SpectraEngine.Core.Entities;
using SpectraEngine.Core.Inspection;
using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Render.Tests;

/// <summary>
/// Rasterises the property panel's wiring sections so a person can look at
/// them: Now, Sends and Receives for one entity.
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
}
