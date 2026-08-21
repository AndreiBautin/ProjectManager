using ProjectManager.Api.Dtos;
using ProjectManager.Api.Validation;

namespace ProjectManager.Tests;

/// <summary>
/// Validation at the trust boundary. The cases that matter most here are the
/// ones that used to pass: a blank name on update, and an out-of-range score
/// that was silently rewritten and answered 200 OK.
/// </summary>
public class RequestValidatorTests
{
    private static CreateProjectRequest Create(
        string name = "Valid name", int impact = 5, int urgency = 5, int effort = 5,
        string? description = null, string? blockReason = null,
        string? newCategoryName = null, string? firstAction = null)
        => new(name, description, null, newCategoryName, impact, urgency, effort,
               false, blockReason, null, firstAction, null);

    private static UpdateProjectRequest Update(
        string name = "Valid name", int impact = 5, int urgency = 5, int effort = 5,
        string? description = null, string? blockReason = null)
        => new(name, description, null, impact, urgency, effort, "Active", false, blockReason, null, null);

    // ---- The gap that used to exist ---------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("\t\n")]
    public void Update_rejectsABlankProjectName(string name)
    {
        // Create rejected this and Update did not, which meant a project could
        // be renamed to nothing through a perfectly ordinary PUT.
        Assert.NotEmpty(RequestValidator.ValidateUpdateProject(Update(name: name)));
    }

    [Fact]
    public void Update_rejectsANullProjectName_ratherThanThrowing()
    {
        // The old code path called request.Name.Trim() unguarded, so this was a
        // NullReferenceException and a 500 rather than a 400.
        var errors = RequestValidator.ValidateUpdateProject(Update(name: null!));
        Assert.NotEmpty(errors);
    }

    [Fact]
    public void Create_andUpdate_agreeOnWhatAValidNameIs()
    {
        // Asymmetric validation is worse than none, because it teaches you the
        // field is protected. This asserts the two stay in step.
        foreach (var name in new[] { "", "   ", new string('x', 5000) })
        {
            var createRejected = RequestValidator.ValidateCreateProject(Create(name: name)).Count > 0;
            var updateRejected = RequestValidator.ValidateUpdateProject(Update(name: name)).Count > 0;
            Assert.Equal(createRejected, updateRejected);
        }
    }

    // ---- Scores are rejected, not silently coerced -------------------------

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(11)]
    [InlineData(9999)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void OutOfRangeScoresAreRejected_notQuietlyClamped(int score)
    {
        // Previously Impact: 9999 became 10 and returned 200 OK, so the caller
        // was told its value was stored as sent when it was not.
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(impact: score)));
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(urgency: score)));
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(effort: score)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(10)]
    public void InRangeScoresAreAccepted_includingBothBoundaries(int score)
    {
        Assert.Empty(RequestValidator.ValidateCreateProject(Create(impact: score, urgency: score, effort: score)));
    }

    [Fact]
    public void EveryOutOfRangeScoreIsReported_notJustTheFirst()
    {
        var errors = RequestValidator.ValidateCreateProject(Create(impact: 0, urgency: 99, effort: -3));
        Assert.Equal(3, errors.Count);
    }

    [Fact]
    public void TheErrorMessageNamesTheFieldAndTheOffendingValue()
    {
        var error = Assert.Single(RequestValidator.ValidateCreateProject(Create(impact: 42)));
        Assert.Contains("Impact", error);
        Assert.Contains("42", error);
    }

    // ---- Length limits ------------------------------------------------------

    [Fact]
    public void OverlongValuesAreRejected()
    {
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(name: new string('x', RequestValidator.MaxNameLength + 1))));
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(description: new string('x', RequestValidator.MaxDescriptionLength + 1))));
        Assert.NotEmpty(RequestValidator.ValidateCreateProject(Create(blockReason: new string('x', RequestValidator.MaxBlockReasonLength + 1))));
    }

    [Fact]
    public void ValuesExactlyAtTheLimitAreAccepted()
    {
        Assert.Empty(RequestValidator.ValidateCreateProject(Create(name: new string('x', RequestValidator.MaxNameLength))));
    }

    [Fact]
    public void TheDemoFixtureItselfPassesValidation()
    {
        // The demo dataset deliberately includes very long values as an edge
        // case. If those ever exceeded the limits the app enforces, the demo
        // would be showing something the API would refuse to accept.
        var fixture = ProjectManager.Api.Demo.DemoDataFixture.Build(
            new DateTime(2026, 6, 15), new Dictionary<string, int>());

        foreach (var project in fixture.Projects)
        {
            var errors = RequestValidator.ValidateCreateProject(Create(
                name: project.Name,
                impact: project.Impact,
                urgency: project.Urgency,
                effort: project.Effort,
                description: project.Description,
                blockReason: project.BlockReason));

            Assert.True(errors.Count == 0,
                $"Demo project '{project.Name}' would be rejected by the API: {string.Join(" ", errors)}");

            foreach (var action in project.Actions)
            {
                Assert.Empty(RequestValidator.ValidateCreateAction(new CreateActionRequest(action.Description, null)));
            }
        }
    }

    // ---- Actions -------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CreateAction_requiresADescription(string? description)
        => Assert.NotEmpty(RequestValidator.ValidateCreateAction(new CreateActionRequest(description!, null)));

    [Fact]
    public void UpdateAction_treatsANullDescriptionAsLeaveItAlone()
        => Assert.Empty(RequestValidator.ValidateUpdateAction(new UpdateActionRequest(null, "Done", null)));

    [Fact]
    public void UpdateAction_rejectsAPresentButBlankDescription()
    {
        // A blank value is a mistake, not a no-op. Accepting it silently would
        // mean the caller is told the edit succeeded when it was discarded -
        // a rejected input must never look like a valid-but-empty one.
        Assert.NotEmpty(RequestValidator.ValidateUpdateAction(new UpdateActionRequest("   ", null, null)));
    }

    // ---- Categories ------------------------------------------------------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    public void CategoryName_isRequired(string? name)
        => Assert.NotEmpty(RequestValidator.ValidateCategoryName(name));

    [Fact]
    public void CategoryName_acceptsAnOrdinaryName()
        => Assert.Empty(RequestValidator.ValidateCategoryName("Home"));
}
