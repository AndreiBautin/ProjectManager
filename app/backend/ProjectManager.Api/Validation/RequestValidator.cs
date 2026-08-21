using ProjectManager.Api.Dtos;

namespace ProjectManager.Api.Validation;

/// <summary>
/// Validation at the trust boundary - the point where data arrives from outside
/// the process. Pure static functions returning a list of problems, so they are
/// directly unit testable without spinning up the HTTP stack.
///
/// <para>
/// Two behaviours here are deliberate corrections to how the app used to work:
/// </para>
/// <list type="number">
/// <item>
/// <b>Update now checks the project name.</b> Create rejected a blank name;
/// Update did not, and called <c>.Trim()</c> on it unguarded - so a project
/// could be renamed to the empty string, and a null name produced a 500 rather
/// than a 400. Validation that is present on create and absent on update is
/// worse than none, because it teaches you the field is protected.
/// </item>
/// <item>
/// <b>Out-of-range scores are rejected rather than silently clamped.</b>
/// Previously <c>Impact: 9999</c> was quietly rewritten to 10 and answered
/// <c>200 OK</c>, so the caller was told its input was stored as sent when it
/// was not. A rejected input must never be indistinguishable from an accepted
/// one. The UI only ever sends 1-10, so this changes nothing for the app - it
/// changes what happens when something else calls the API.
/// </item>
/// </list>
/// </summary>
public static class RequestValidator
{
    public const int MaxNameLength = 200;
    public const int MaxDescriptionLength = 2000;
    public const int MaxBlockReasonLength = 1000;
    public const int MaxActionDescriptionLength = 500;
    public const int MinScore = 1;
    public const int MaxScore = 10;

    public static IReadOnlyList<string> ValidateCreateProject(CreateProjectRequest r)
    {
        var errors = new List<string>();
        ValidateName(r.Name, errors);
        ValidateScores(r.Impact, r.Urgency, r.Effort, errors);
        ValidateLength(r.Description, MaxDescriptionLength, "Description", errors);
        ValidateLength(r.BlockReason, MaxBlockReasonLength, "Block reason", errors);
        ValidateLength(r.NewCategoryName, MaxNameLength, "Category name", errors);
        ValidateLength(r.FirstActionDescription, MaxActionDescriptionLength, "Action description", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateUpdateProject(UpdateProjectRequest r)
    {
        var errors = new List<string>();
        ValidateName(r.Name, errors);
        ValidateScores(r.Impact, r.Urgency, r.Effort, errors);
        ValidateLength(r.Description, MaxDescriptionLength, "Description", errors);
        ValidateLength(r.BlockReason, MaxBlockReasonLength, "Block reason", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateCreateAction(CreateActionRequest r)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Description))
            errors.Add("Action description is required.");
        else
            ValidateLength(r.Description, MaxActionDescriptionLength, "Action description", errors);
        return errors;
    }

    public static IReadOnlyList<string> ValidateUpdateAction(UpdateActionRequest r)
    {
        var errors = new List<string>();

        // Description is optional on update (null means "leave it alone"), but a
        // value that is present and blank is a mistake, not a no-op: it would
        // otherwise be silently ignored and the caller told it succeeded.
        if (r.Description != null)
        {
            if (string.IsNullOrWhiteSpace(r.Description))
                errors.Add("Action description cannot be blank.");
            else
                ValidateLength(r.Description, MaxActionDescriptionLength, "Action description", errors);
        }

        return errors;
    }

    public static IReadOnlyList<string> ValidateCategoryName(string? name)
    {
        var errors = new List<string>();
        if (string.IsNullOrWhiteSpace(name))
            errors.Add("Category name is required.");
        else
            ValidateLength(name, MaxNameLength, "Category name", errors);
        return errors;
    }

    private static void ValidateName(string? name, List<string> errors)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            errors.Add("Project name is required.");
            return;
        }

        ValidateLength(name, MaxNameLength, "Project name", errors);
    }

    private static void ValidateScores(int impact, int urgency, int effort, List<string> errors)
    {
        ValidateScore(impact, "Impact", errors);
        ValidateScore(urgency, "Urgency", errors);
        ValidateScore(effort, "Effort", errors);
    }

    private static void ValidateScore(int value, string field, List<string> errors)
    {
        if (value < MinScore || value > MaxScore)
            errors.Add($"{field} must be between {MinScore} and {MaxScore} (got {value}).");
    }

    private static void ValidateLength(string? value, int max, string field, List<string> errors)
    {
        if (value != null && value.Trim().Length > max)
            errors.Add($"{field} must be {max} characters or fewer (got {value.Trim().Length}).");
    }
}
