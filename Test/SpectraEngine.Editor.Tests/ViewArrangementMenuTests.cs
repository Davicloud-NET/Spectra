using SpectraEngine.Editor.Shell;

namespace SpectraEngine.Editor.Tests;

/// <summary>What the Logic view's menu items bind to.</summary>
public sealed class ViewArrangementMenuTests
{
    [Theory]
    [InlineData(ViewArrangement.Single, true, false, false)]
    [InlineData(ViewArrangement.LogicBelow, false, true, false)]
    [InlineData(ViewArrangement.LogicBeside, false, false, true)]
    public void One_item_is_checked_for_each_arrangement(
        ViewArrangement arrangement, bool hidden, bool below, bool beside)
    {
        var shell = new ShellModel { ViewArrangement = arrangement };

        shell.IsLogicHidden.ShouldBe(hidden);
        shell.IsLogicBelow.ShouldBe(below);
        shell.IsLogicBeside.ShouldBe(beside);
    }

    [Fact]
    public void A_new_arrangement_tells_every_item()
    {
        // A binding that hears nothing keeps its old checkmark.
        var shell = new ShellModel();
        var raised = new List<string?>();
        shell.PropertyChanged += (_, args) => raised.Add(args.PropertyName);

        shell.ViewArrangement = ViewArrangement.LogicBeside;

        raised.ShouldContain(nameof(ShellModel.IsLogicHidden));
        raised.ShouldContain(nameof(ShellModel.IsLogicBelow));
        raised.ShouldContain(nameof(ShellModel.IsLogicBeside));
    }
}
