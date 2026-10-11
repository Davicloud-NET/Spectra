namespace SpectraEngine.Bsp.Tests;

/// <summary>Reading the CFrame probe's output as it comes out of Roblox Studio.</summary>
public sealed class StudioProbeAnswersTests
{
    [Fact]
    public void A_json_document_gives_each_case_its_numbers()
    {
        const string text = """
            {
            "identity":[0,0,0,1,0,0,0,1,0,0,0,1],
            "look":[-0,-0,-1]
            }
            """;

        Dictionary<string, float[]> answers = StudioProbeAnswers.Parse(text);

        answers.Count.ShouldBe(2);
        answers["identity"].ShouldBe(new float[] { 0, 0, 0, 1, 0, 0, 0, 1, 0, 0, 0, 1 });
        answers["look"].ShouldBe(new float[] { 0, 0, -1 });
    }

    [Fact]
    public void Numbers_in_exponent_form_are_read()
    {
        Dictionary<string, float[]> answers = StudioProbeAnswers.Parse("\"v\":[-4.37113883e-08,1.5e+10, 0.25]");

        answers["v"].ShouldBe(new[] { -4.37113883e-08f, 1.5e+10f, 0.25f });
    }

    [Fact]
    public void A_null_is_read_as_a_NaN()
    {
        Dictionary<string, float[]> answers = StudioProbeAnswers.Parse("\"v\":[1,null,3]");

        answers["v"].Length.ShouldBe(3);
        float.IsNaN(answers["v"][1]).ShouldBeTrue();
    }

    [Fact]
    public void A_case_Roblox_could_not_work_out_has_no_numbers()
    {
        StudioProbeAnswers.Parse("\"v\":[]")["v"].ShouldBeEmpty();
    }

    [Fact]
    public void A_time_in_front_of_each_line_is_passed_over()
    {
        const string text = """
            12:01:07.412  {  -  Edit
            12:01:07.412  "a":[1,2,3],  -  Edit
            12:01:07.412  "b":[4,5,6]  -  Edit
            12:01:07.412  }  -  Edit
            """;

        Dictionary<string, float[]> answers = StudioProbeAnswers.Parse(text);

        answers["a"].ShouldBe(new float[] { 1, 2, 3 });
        answers["b"].ShouldBe(new float[] { 4, 5, 6 });
    }
}
