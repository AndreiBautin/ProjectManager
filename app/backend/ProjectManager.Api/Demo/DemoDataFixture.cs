using ProjectManager.Api.Models;

namespace ProjectManager.Api.Demo;

/// <summary>
/// The demo dataset, as code.
///
/// <para>
/// Barrier #1 of the demo-data guarantee: <b>generate, never capture.</b> Every
/// value below is written by hand in this file, which is checked into a public
/// repository and readable by anyone. There is no export step from a personal
/// device anywhere in this pipeline - not a script, not a fixture file, not a
/// database copy. Personal data cannot arrive here by accident because there is
/// no path for it to arrive by at all.
/// </para>
///
/// <para>
/// <b>All dates are offsets from the supplied <c>now</c>, never absolute.</b> A
/// fixture pinned to fixed timestamps rots: opened a year later it shows
/// everything overdue and three zeros on the Completed page's "last 30 days"
/// statistics. Offsets keep the dataset permanently plausible while staying
/// fully deterministic for any given <c>now</c>.
/// </para>
/// </summary>
public static class DemoDataFixture
{
    /// <summary>Blocker links, expressed by name because IDs do not exist until save.</summary>
    public sealed record BlockerLink(string BlockedProjectName, string BlockingProjectName);

    public sealed record Fixture(List<Project> Projects, List<BlockerLink> BlockerLinks);

    // Deliberately long values, to prove the layout holds. Not lorem ipsum - an
    // unreadable placeholder demonstrates nothing about how the real UI copes.
    private const string LongName =
        "Migrate the household paperwork archive from three overflowing filing boxes into a single labelled and indexed system";

    private const string LongDescription =
        "Every year this turns into a scramble because nothing is where it should be. The plan is to work through one box at a "
        + "time, shred anything past its retention period, scan what needs to survive but does not need to exist on paper, and "
        + "file the remainder into labelled folders with an index sheet at the front of the drawer. The point is not tidiness "
        + "for its own sake - it is that finding a single document currently takes upwards of an hour, and that cost gets paid "
        + "several times a year, every year, indefinitely.";

    /// <summary>
    /// Builds the full demo object graph. Pure: same <paramref name="now"/> in,
    /// same graph out. No database access, no ambient clock, no randomness.
    /// </summary>
    public static Fixture Build(DateTime now, IReadOnlyDictionary<string, int> categoryIdsByName)
    {
        int? Cat(string name) => categoryIdsByName.TryGetValue(name, out var id) ? id : null;

        var projects = new List<Project>
        {
            // The hero. Demonstrates exactly what the scoring formula is for: high
            // impact, urgent, and trivially small. Impact 10 x Urgency 10 / Effort 1
            // = 100, the maximum possible score, so it tops the ranking and becomes
            // the recommended next action on the Command Center.
            MakeProject("Call the insurance company about the open claim",
                "A five-minute phone call is the only thing standing between a filed claim and a reimbursed one.",
                Cat("Finance"), impact: 10, urgency: 10, effort: 1, ProjectStatus.Active,
                createdDaysAgo: 3, now: now,
                actions: new[]
                {
                    MakeAction("Call the claims line and confirm the receipt submission deadline", 1, now, createdDaysAgo: 3),
                    MakeAction("Scan the two receipts and attach them to the claim", 2, now, createdDaysAgo: 3),
                }),

            // GREEN "Moving Forward", plus a deadline inside the 14-day ramp window
            // so the deadline pill renders red and effective urgency is being pulled
            // upward. Two of four actions done gives a 50% progress bar.
            MakeProject("Ship the portfolio site",
                "Get the personal site off localhost and onto a real URL, with the writing actually finished rather than placeholdered.",
                Cat("Career"), impact: 9, urgency: 7, effort: 4, ProjectStatus.Active,
                createdDaysAgo: 26, now: now, deadlineInDays: 9,
                actions: new[]
                {
                    MakeAction("Pick a domain and point the nameservers", 1, now, createdDaysAgo: 26, doneDaysAgo: 20),
                    MakeAction("Write the three case study pages", 2, now, createdDaysAgo: 26, doneDaysAgo: 8),
                    MakeAction("Replace the placeholder headshot", 3, now, createdDaysAgo: 26),
                    MakeAction("Proofread everything once, out loud", 4, now, createdDaysAgo: 26),
                }),

            // Deadline already passed: urgency pins at 10 regardless of the manual
            // value, and the card reads "Overdue by 2d".
            MakeProject("File the quarterly estimated tax payment",
                "The kind of deadline that does not care how busy the week was.",
                Cat("Finance"), impact: 8, urgency: 4, effort: 3, ProjectStatus.Active,
                createdDaysAgo: 40, now: now, deadlineInDays: -2,
                actions: new[]
                {
                    MakeAction("Total up the quarter's invoices", 1, now, createdDaysAgo: 40, doneDaysAgo: 6),
                    MakeAction("Submit the payment through the tax portal", 2, now, createdDaysAgo: 40),
                }),

            // BLUE "Waiting until <date>". The next action is perfectly well defined,
            // it simply is not workable yet. The recommendation engine skips it and
            // moves on rather than surfacing something undoable today.
            MakeProject("Replace the kitchen faucet",
                "The slow drip has graduated to a fast drip.",
                Cat("Home"), impact: 6, urgency: 6, effort: 3, ProjectStatus.Active,
                createdDaysAgo: 11, now: now,
                actions: new[]
                {
                    MakeAction("Measure the existing mount and order the replacement", 1, now, createdDaysAgo: 11, doneDaysAgo: 2),
                    MakeAction("Fit the new faucet once the part is delivered", 2, now, createdDaysAgo: 11, availableInDays: 5),
                }),

            // AMBER "Blocked - actionable". Blocked by something that is NOT another
            // tracked project, and its own next action IS the unblock step - so the
            // engine still recommends it, because doing it releases the project.
            MakeProject("Renew the expiring passport",
                null,
                Cat("Personal"), impact: 8, urgency: 6, effort: 4, ProjectStatus.Blocked,
                createdDaysAgo: 33, now: now,
                isBlocked: true,
                blockReason: "Cannot submit the renewal form until the certified copy of the birth certificate arrives in the post.",
                actions: new[]
                {
                    MakeAction("Chase the records office about the certified copy", 1, now, createdDaysAgo: 33),
                    MakeAction("Get new passport photos taken", 2, now, createdDaysAgo: 33),
                    MakeAction("Post the completed renewal packet", 3, now, createdDaysAgo: 33),
                }),

            // PURPLE "Blocked - waiting on other projects". Linked below to the
            // passport project. Its own next action does not unblock it - finishing
            // the OTHER project does - so the engine skips it unconditionally.
            MakeProject("Book the anniversary trip",
                "Flights are refundable for another few weeks, so there is slack, but not unlimited slack.",
                Cat("Relationships"), impact: 9, urgency: 7, effort: 5, ProjectStatus.Blocked,
                createdDaysAgo: 18, now: now,
                actions: new[]
                {
                    MakeAction("Compare the two shortlisted itineraries and pick one", 1, now, createdDaysAgo: 18),
                    MakeAction("Book the flights and the hotel together", 2, now, createdDaysAgo: 18),
                }),

            // RED "Blocked - stuck". Blocked with no defined next action at all. This
            // is the state the app exists to make visible, because it is the one that
            // silently swallows projects for months.
            MakeProject("Learn to develop black-and-white film at home",
                "Stalled, and honestly it is not obvious what the next move even is.",
                Cat("Hobbies"), impact: 4, urgency: 2, effort: 6, ProjectStatus.Blocked,
                createdDaysAgo: 96, now: now,
                isBlocked: true,
                blockReason: "No darkroom space, and no clear idea what a realistic first step would be in a flat this size.",
                actions: Array.Empty<ActionItem>()),

            // GRAY "No next action" on an otherwise healthy Active project. Distinct
            // from blocked: nothing is stopping it, nobody has decided the next step.
            MakeProject("Set up a proper home network rack",
                "The switch and the modem currently live on the floor behind the sofa.",
                Cat("Home"), impact: 5, urgency: 3, effort: 6, ProjectStatus.Active,
                createdDaysAgo: 61, now: now,
                actions: Array.Empty<ActionItem>()),

            // GRAY "Paused". Excluded from ranking and from the recommendation, but
            // still visible on the Projects screen behind the "Show paused" toggle.
            MakeProject("Learn hand-cut dovetail joinery",
                "Genuinely want to do this. Genuinely not this quarter.",
                Cat("Hobbies"), impact: 3, urgency: 2, effort: 8, ProjectStatus.Paused,
                createdDaysAgo: 129, now: now,
                actions: new[]
                {
                    MakeAction("Watch the full joinery basics series", 1, now, createdDaysAgo: 129, doneDaysAgo: 120),
                    MakeAction("Buy a marking gauge and a dovetail saw", 2, now, createdDaysAgo: 129),
                }),

            // Long horizon, no deadline, steady progress. Three of four actions done.
            MakeProject("Build a six-month emergency fund",
                "Long horizon, steady progress, no deadline. The kind of project that needs a system rather than urgency.",
                Cat("Finance"), impact: 9, urgency: 5, effort: 7, ProjectStatus.Active,
                createdDaysAgo: 154, now: now,
                actions: new[]
                {
                    MakeAction("Work out the real monthly baseline spend", 1, now, createdDaysAgo: 154, doneDaysAgo: 140),
                    MakeAction("Open a separate high-yield savings account", 2, now, createdDaysAgo: 154, doneDaysAgo: 96),
                    MakeAction("Set up the automatic monthly transfer", 3, now, createdDaysAgo: 154, doneDaysAgo: 40),
                    MakeAction("Review the target amount once a quarter", 4, now, createdDaysAgo: 154),
                }),

            // EDGE CASE: the minimal record. Name only - no description, no category,
            // no deadline, no actions, every score left at its 5/5/5 default. This is
            // what frictionless capture actually produces, and the UI has to survive it.
            MakeProject("Return the library books",
                null, null, impact: 5, urgency: 5, effort: 5, ProjectStatus.Active,
                createdDaysAgo: 1, now: now,
                actions: Array.Empty<ActionItem>()),

            // EDGE CASE: very long values, in the name, the description AND an action.
            MakeProject(LongName, LongDescription,
                Cat("Home"), impact: 6, urgency: 4, effort: 9, ProjectStatus.Active,
                createdDaysAgo: 74, now: now,
                actions: new[]
                {
                    MakeAction(
                        "Sort the first box into keep, scan, and shred piles without stopping to read anything in detail, because reading is what turned this into a three-year project in the first place",
                        1, now, createdDaysAgo: 74),
                }),

            // EDGE CASE: the minimum possible priority score. Impact 1 x Urgency 1 /
            // Effort 10 rounds to 0, so this sorts dead last, which is exactly right.
            MakeProject("Alphabetise the spice rack",
                "Zero impact, zero urgency, real effort. Correctly ranked last.",
                Cat("Home"), impact: 1, urgency: 1, effort: 10, ProjectStatus.Active,
                createdDaysAgo: 47, now: now,
                actions: new[]
                {
                    MakeAction("Take everything out of the cupboard", 1, now, createdDaysAgo: 47),
                }),

            // Completed projects, spread so all three Completed-page counters are
            // non-zero: three inside 30 days, five inside 90, six all-time. Because
            // these are relative offsets, that stays true whenever the demo is opened.
            MakeCompleted("Renew the car insurance policy", Cat("Finance"), now, createdDaysAgo: 34, completedDaysAgo: 3),
            MakeCompleted("Fix the wobbling ceiling fan", Cat("Home"), now, createdDaysAgo: 30, completedDaysAgo: 12),
            MakeCompleted("Set up the new laptop and migrate everything across", Cat("Career"), now, createdDaysAgo: 52, completedDaysAgo: 25),
            MakeCompleted("Plan and host the housewarming dinner", Cat("Relationships"), now, createdDaysAgo: 71, completedDaysAgo: 45),
            MakeCompleted("Sell the old bicycle", Cat("Personal"), now, createdDaysAgo: 110, completedDaysAgo: 70),
            MakeCompleted("Replace every smoke alarm battery", Cat("Home"), now, createdDaysAgo: 240, completedDaysAgo: 205),
        };

        var links = new List<BlockerLink>
        {
            new("Book the anniversary trip", "Renew the expiring passport"),
        };

        return new Fixture(projects, links);
    }

    private static Project MakeProject(
        string name, string? description, int? categoryId,
        int impact, int urgency, int effort, ProjectStatus status,
        int createdDaysAgo, DateTime now,
        ActionItem[] actions,
        int? deadlineInDays = null,
        bool isBlocked = false,
        string? blockReason = null)
    {
        var created = now.AddDays(-createdDaysAgo);
        return new Project
        {
            Name = name,
            Description = description,
            CategoryId = categoryId,
            Impact = impact,
            Urgency = urgency,
            Effort = effort,
            Status = status,
            IsBlocked = isBlocked,
            BlockReason = blockReason,
            Deadline = deadlineInDays.HasValue ? now.Date.AddDays(deadlineInDays.Value) : null,
            CreatedDate = created,
            UpdatedDate = created.AddDays(Math.Min(2, createdDaysAgo)),
            Actions = actions.ToList(),
        };
    }

    private static Project MakeCompleted(
        string name, int? categoryId, DateTime now, int createdDaysAgo, int completedDaysAgo)
    {
        var created = now.AddDays(-createdDaysAgo);
        var completed = now.AddDays(-completedDaysAgo);
        return new Project
        {
            Name = name,
            CategoryId = categoryId,
            Impact = 6,
            Urgency = 5,
            Effort = 4,
            Status = ProjectStatus.Completed,
            CreatedDate = created,
            UpdatedDate = completed,
            CompletedDate = completed,
            Actions = new List<ActionItem>
            {
                new()
                {
                    Description = "Get it done",
                    Status = ActionStatus.Done,
                    Order = 1,
                    CreatedDate = created,
                    CompletedDate = completed,
                },
            },
        };
    }

    private static ActionItem MakeAction(
        string description, int order, DateTime now, int createdDaysAgo,
        int? doneDaysAgo = null, int? availableInDays = null)
        => new()
        {
            Description = description,
            Order = order,
            Status = doneDaysAgo.HasValue ? ActionStatus.Done : ActionStatus.Pending,
            CreatedDate = now.AddDays(-createdDaysAgo),
            CompletedDate = doneDaysAgo.HasValue ? now.AddDays(-doneDaysAgo.Value) : null,
            AvailableFrom = availableInDays.HasValue ? now.Date.AddDays(availableInDays.Value) : null,
        };
}
