using SpectraEngine.Core.Projects;
using System.Text;

namespace SpectraEngine.Bsp.Tests;

/// <summary>
/// A project names the language its own text is written in. One that names
/// none is in English, and its file does not change because of that.
/// </summary>
public sealed class ProjectLanguageTests
{
    private const string NamesNoLanguage = """
        {
          "spectraproject": 1,
          "minimumReadableVersion": 1,
          "engine": "1.0.0",
          "name": "MyGame",
          "id": "6f2b7c19-40ad-4b1e-9c0f-2e5d81a37b44",
          "startupMap": "Maps/Lobby.smap",
          "maps": [
            "Maps/Lobby.smap"
          ],
          "display": {"width":1280,"height":720,"vsync":true,"mode":"windowed"}
        }
        """;

    private const string NamesGerman = """
        {
          "spectraproject": 1,
          "minimumReadableVersion": 1,
          "engine": "1.0.0",
          "name": "MyGame",
          "id": "6f2b7c19-40ad-4b1e-9c0f-2e5d81a37b44",
          "language": "de",
          "startupMap": "Maps/Lobby.smap",
          "maps": [
            "Maps/Lobby.smap"
          ],
          "display": {"width":1280,"height":720,"vsync":true,"mode":"windowed"}
        }
        """;

    // 'language' between two members this engine only carries.
    private const string LanguageBetweenCarriedMembers = """
        {
          "spectraproject": 1,
          "minimumReadableVersion": 1,
          "engine": "1.0.0",
          "name": "MyGame",
          "id": "6f2b7c19-40ad-4b1e-9c0f-2e5d81a37b44",
          "publisher": "Nobody",
          "language": "pt-br",
          "input": {"jump":"Space"},
          "maps": [],
          "display": {"width":1280,"height":720,"vsync":true,"mode":"windowed"}
        }
        """;

    [Fact]
    public void A_project_that_names_no_language_is_in_English()
    {
        SpectraProject project = ProjectReader.Read(Utf8(NamesNoLanguage));

        project.Language.ShouldBeNull();
        project.LanguageOrDefault.ShouldBe("en");
        new SpectraProject().LanguageOrDefault.ShouldBe(LanguageTag.Default);
    }

    [Fact]
    public void A_project_that_names_no_language_keeps_its_bytes()
    {
        byte[] source = Utf8(NamesNoLanguage);

        byte[] written = ProjectWriter.Write(ProjectReader.Read(source));

        Encoding.UTF8.GetString(written).ShouldBe(Text(NamesNoLanguage));
        written.ShouldBe(source);
    }

    [Fact]
    public void A_language_is_read_back_as_it_was_written()
    {
        SpectraProject project = ProjectReader.Read(Utf8(NamesNoLanguage));
        project.Language = "de";

        byte[] written = ProjectWriter.Write(project);

        Encoding.UTF8.GetString(written).ShouldBe(Text(NamesGerman));
        SpectraProject reread = ProjectReader.Read(written);
        reread.Language.ShouldBe("de");
        reread.LanguageOrDefault.ShouldBe("de");
    }

    [Theory]
    [InlineData(nameof(NamesGerman))]
    [InlineData(nameof(LanguageBetweenCarriedMembers))]
    public void A_project_that_names_a_language_keeps_its_bytes(string which)
    {
        string text = which == nameof(NamesGerman) ? NamesGerman : LanguageBetweenCarriedMembers;

        byte[] written = ProjectWriter.Write(ProjectReader.Read(Utf8(text)));

        Encoding.UTF8.GetString(written).ShouldBe(Text(text));
    }

    [Fact]
    public void The_language_is_written_after_the_id_and_the_carried_members_keep_their_places()
    {
        SpectraProject project = ProjectReader.Read(Utf8(LanguageBetweenCarriedMembers));

        project.Language.ShouldBe("pt-br");
        project.Unknown.Select(member => member.Name).ShouldBe(["publisher", "input"]);
    }

    [Theory]
    [InlineData("\"English\"", "not 'English'")]
    [InlineData("\"EN\"", "not 'EN'")]
    [InlineData("\"\"", "not ''")]
    [InlineData("\"en_us\"", "not 'en_us'")]
    [InlineData("7", "must be a string")]
    public void A_language_that_is_not_a_tag_is_refused_at_the_file(string value, string says)
    {
        string text = NamesGerman.Replace("\"de\"", value);

        ProjectFormatException refusal = Should.Throw<ProjectFormatException>(() => ProjectReader.Read(Utf8(text)));

        refusal.Message.ShouldContain("'language'");
        refusal.Message.ShouldContain(says);
    }

    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    [InlineData("fil")]
    [InlineData("pt-br")]
    [InlineData("zh-hans")]
    [InlineData("es-419")]
    [InlineData("sr-latn-rs")]
    public void A_short_lowercase_tag_is_a_language(string tag)
    {
        LanguageTag.IsValid(tag).ShouldBeTrue();
    }

    [Theory]
    [InlineData("")]
    [InlineData("e")]
    [InlineData("EN")]
    [InlineData("english")]
    [InlineData("en_us")]
    [InlineData("en-")]
    [InlineData("-en")]
    [InlineData("en--us")]
    [InlineData("e1")]
    [InlineData("en us")]
    [InlineData("en/us")]
    [InlineData("en-abcdefghi")]
    [InlineData("en-abcdefgh-abcdefgh")]
    public void Anything_else_is_not_a_language(string tag)
    {
        LanguageTag.IsValid(tag).ShouldBeFalse();
    }

    [Theory]
    [InlineData("DE", "de")]
    [InlineData(" pt-BR ", "pt-br")]
    [InlineData("en", "en")]
    public void A_typed_tag_is_trimmed_and_lowered(string typed, string tag)
    {
        LanguageTag.TryNormalize(typed, out string? normalized).ShouldBeTrue();

        normalized.ShouldBe(tag);
    }

    [Theory]
    [InlineData("German")]
    [InlineData("")]
    [InlineData("a-very-long-language-tag")]
    public void A_typed_word_that_is_not_a_tag_is_refused(string typed)
    {
        LanguageTag.TryNormalize(typed, out string? normalized).ShouldBeFalse();

        normalized.ShouldBeNull();
    }

    // A canonical document has unix line endings and ends with one.
    private static string Text(string text) => text.ReplaceLineEndings("\n") + "\n";

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(Text(text));
}
