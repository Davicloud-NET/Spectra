using SpectraEngine.Editing.Commands;

namespace SpectraEngine.Editing.Tests;

/// <summary>
/// The name a new script's file gets from the node it goes under, and the
/// file that is written under that name.
/// </summary>
public sealed class ScriptFileNameTests : IDisposable
{
    private readonly string _root =
        Path.Combine(Path.GetTempPath(), "spectra-script-names-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
            // A locked temp folder is not a test failure.
        }
    }

    private string ScriptsFolder => Path.Combine(_root, "Scripts");

    private string Make(string? parent, string text = "print(1)\n")
    {
        ScriptFileMaker.TryMake(_root, parent, text, out string path, out string failure)
            .ShouldBeTrue(failure);
        return path;
    }

    [Theory]
    [InlineData("Part", "Part")]
    [InlineData("SpinningCoin", "SpinningCoin")]
    [InlineData("door_01", "door_01")]
    [InlineData("left-hinge", "left-hinge")]
    [InlineData("Tür", "Tür")]
    public void An_ordinary_name_is_kept_as_it_is(string parent, string expected)
    {
        ScriptFileName.StemFor(parent).ShouldBe(expected);
    }

    [Theory]
    [InlineData("Front Door", "Front_Door")]
    [InlineData("  Front   Door  ", "Front_Door")]
    [InlineData("Door (2)", "Door_2")]
    [InlineData("a/b\\c", "a_b_c")]
    [InlineData("what?*<>|\":name", "what_name")]
    [InlineData("tab\there", "tab_here")]
    [InlineData("ends.with.dots...", "ends_with_dots")]
    public void What_a_file_name_cannot_hold_becomes_one_underscore(string parent, string expected)
    {
        ScriptFileName.StemFor(parent).ShouldBe(expected);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("???")]
    [InlineData("..")]
    [InlineData("_-_")]
    public void A_name_with_nothing_usable_in_it_becomes_script(string? parent)
    {
        ScriptFileName.StemFor(parent).ShouldBe("Script");
    }

    [Theory]
    [InlineData("CON", "CON_")]
    [InlineData("nul", "nul_")]
    [InlineData("Com1", "Com1_")]
    [InlineData("LPT9", "LPT9_")]
    [InlineData("Console", "Console")]
    public void A_name_windows_keeps_for_a_device_is_stepped_around(string parent, string expected)
    {
        ScriptFileName.StemFor(parent).ShouldBe(expected);
    }

    [Fact]
    public void A_very_long_name_is_cut_to_a_length_a_path_can_carry()
    {
        string stem = ScriptFileName.StemFor(new string('a', 300));

        stem.Length.ShouldBe(48);
    }

    [Fact]
    public void A_free_name_is_taken_as_it_is()
    {
        ScriptFileName.FirstFree("Part", _ => false).ShouldBe("Part.luau");
    }

    [Fact]
    public void A_taken_name_gets_the_first_number_that_is_free()
    {
        string[] taken = ["Part.luau", "Part2.luau", "Part3.luau"];

        ScriptFileName.FirstFree("Part", taken.Contains).ShouldBe("Part4.luau");
    }

    [Fact]
    public void A_name_that_ends_in_a_digit_keeps_its_number_apart()
    {
        // Door1 and a 2 would read as Door12.
        ScriptFileName.FirstFree("Door1", name => name == "Door1.luau").ShouldBe("Door1_2.luau");
    }

    [Fact]
    public void When_every_number_is_taken_there_is_no_name()
    {
        ScriptFileName.FirstFree("Part", _ => true, limit: 5).ShouldBeNull();
    }

    [Fact]
    public void The_file_is_made_in_the_scripts_folder_under_the_parents_name()
    {
        string path = Make("Part", "print(\"hi\")\n");

        path.ShouldBe("Scripts/Part.luau");
        File.ReadAllText(Path.Combine(ScriptsFolder, "Part.luau")).ShouldBe("print(\"hi\")\n");
    }

    [Fact]
    public void A_scripts_folder_that_is_not_there_is_made()
    {
        Directory.Exists(_root).ShouldBeFalse();

        Make("Part");

        Directory.Exists(ScriptsFolder).ShouldBeTrue();
    }

    [Fact]
    public void The_file_carries_no_byte_order_mark()
    {
        Make("Part", "print(1)\n");

        File.ReadAllBytes(Path.Combine(ScriptsFolder, "Part.luau"))[0].ShouldBe((byte)'p');
    }

    [Fact]
    public void The_same_name_twice_gives_two_files_and_the_first_is_not_touched()
    {
        Make("Part", "first\n").ShouldBe("Scripts/Part.luau");
        Make("Part", "second\n").ShouldBe("Scripts/Part2.luau");
        Make("Part", "third\n").ShouldBe("Scripts/Part3.luau");

        File.ReadAllText(Path.Combine(ScriptsFolder, "Part.luau")).ShouldBe("first\n");
        File.ReadAllText(Path.Combine(ScriptsFolder, "Part2.luau")).ShouldBe("second\n");
    }

    [Fact]
    public void A_file_somebody_wrote_is_never_written_over()
    {
        Directory.CreateDirectory(ScriptsFolder);
        string mine = Path.Combine(ScriptsFolder, "Part.luau");
        File.WriteAllText(mine, "-- an afternoon's work\n");

        Make("Part").ShouldBe("Scripts/Part2.luau");

        File.ReadAllText(mine).ShouldBe("-- an afternoon's work\n");
    }

    [Fact]
    public void A_name_taken_in_another_case_counts_as_taken()
    {
        // Windows and a cooked pack compare names without regard to case.
        Directory.CreateDirectory(ScriptsFolder);
        File.WriteAllText(Path.Combine(ScriptsFolder, "PART.luau"), string.Empty);

        Make("Part").ShouldBe("Scripts/Part2.luau");
    }

    [Fact]
    public void A_folder_of_that_name_counts_as_taken_too()
    {
        Directory.CreateDirectory(Path.Combine(ScriptsFolder, "Part.luau"));

        Make("Part").ShouldBe("Scripts/Part2.luau");
    }

    [Theory]
    [InlineData("", "Scripts/Script.luau")]
    [InlineData("?:*", "Scripts/Script.luau")]
    [InlineData("Front Door", "Scripts/Front_Door.luau")]
    [InlineData("NUL", "Scripts/NUL_.luau")]
    public void An_empty_or_illegal_parent_name_still_gives_a_file(string parent, string expected)
    {
        Make(parent).ShouldBe(expected);

        File.Exists(Path.Combine(_root, expected.Replace('/', Path.DirectorySeparatorChar))).ShouldBeTrue();
    }

    [Fact]
    public void A_write_that_fails_once_the_file_is_made_is_reported_and_leaves_no_file()
    {
        // A full disk or a share that dropped. The name was not taken, so
        // the next number is not the answer.
        int tries = 0;

        ScriptFileMaker.TryMake(
            _root, "Part",
            _ =>
            {
                tries++;
                throw new IOException("There is not enough space on the disk.");
            },
            out string path, out string failure).ShouldBeFalse();

        tries.ShouldBe(1);
        path.ShouldBeEmpty();
        failure.ShouldBe("Assets/Scripts/Part.luau could not be written. There is not enough space on the disk.");
        Directory.GetFiles(ScriptsFolder).ShouldBeEmpty();
    }

    [Fact]
    public void A_scripts_folder_that_cannot_be_made_is_a_sentence_and_not_an_exception()
    {
        // A file where the folder should be: nothing can be written under it.
        Directory.CreateDirectory(_root);
        File.WriteAllText(ScriptsFolder, "in the way");

        ScriptFileMaker.TryMake(_root, "Part", "print(1)\n", out string path, out string failure)
            .ShouldBeFalse();

        path.ShouldBeEmpty();
        failure.ShouldStartWith("Assets/Scripts/Part.luau could not be written. ");
        File.ReadAllText(ScriptsFolder).ShouldBe("in the way");
    }
}
