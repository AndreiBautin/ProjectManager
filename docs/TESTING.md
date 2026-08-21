# Testing

**144 tests. Before this pass, there were none.**

```bash
dotnet test app/backend/ProjectManager.Tests/ProjectManager.Tests.csproj
```

```
Passed!  -  Failed: 0, Passed: 144, Skipped: 0, Total: 144, Duration: 2 s
```

---

## Strategy

There is no coverage target, deliberately. Chasing a number produces tests
written to raise the number, and those are precisely the tests that do not catch
bugs — asserting that a getter returns what was set, that a mapper maps, that a
constructor constructs.

What is tested instead is chosen by one question: **if this broke, would
anything notice?**

That yields four categories:

1. **The rules the app exists to enforce** — everything in `PriorityEngine`.
2. **Logic that is hard to verify by reading** — cycle detection in the
   dependency graph.
3. **The trust boundary** — what happens to input that arrives from outside.
4. **The properties the deployment now depends on** — the demo-data barriers and
   configuration parsing.

## Breakdown

| Suite | Tests | Covers |
| --- | ---: | --- |
| `PriorityEngineTests` | 35 | Scoring, deadline ramping, progress, eligibility, ranking, recommendation, status derivation |
| `AppOptionsParserTests` | 41 | Configuration totality, demo-mode flag, database namespace separation, CORS parsing |
| `DemoDataTests` | 27 | The three demo-data barriers, fixture quality, non-rotting dates |
| `RequestValidatorTests` | 28 | Trust-boundary validation, including the two bugs this pass fixed |
| `BlockingServiceTests` | 13 | Cycle detection, link reconciliation, cascade unblocking |

### `PriorityEngineTests` — the core

`PriorityEngine` is a pure static class over in-memory objects: no database, no
HTTP, no mocks. That makes it simultaneously the highest-value and the cheapest
thing in the repository to test, which is why it is covered first and hardest.

Cases worth calling out:

- **`ComputeScore_doesNotDivideByZero_whenEffortIsZero`** — the design document
  identifies `Effort = 0` as a flaw in the original spec. The engine floors
  effort at 1, so even a row written directly to the database cannot crash it.
- **`EffectiveUrgency_onlyEverIncreases_neverDecreases`** — loops the deadline
  across the whole ramp window and asserts a manually-set urgency of 10 is never
  dragged *down* by the ramp arithmetic. That is a property, not an example.
- **`Recommendation_skipsAProjectBlockedByAnotherOpenProject`** — the subtlest
  rule in the app. A project blocked by *another project* must be skipped even
  though it has a perfectly good next action, because doing that action does not
  release it. Finishing the other project does.
- **`Ranking_breaksScoreTiesWithUrgencyThenOldestFirst`** — three projects that
  all score 10, asserting the full tiebreaker chain so nothing rots at the
  bottom of the list.

### `DemoDataTests` — the highest consequence

If these fail, personal data could reach a public URL.

- **`Fixture_containsNothingMatchingPersonalDataPatterns`** scans every string
  in the fixture against patterns for email addresses, phone numbers, URLs,
  government IDs, long digit runs, credential assignments and private key
  blocks.
- **`SeedIfEmpty_neverDestroysExistingData_evenWhenCalledRepeatedly`** calls the
  startup seeder five times against a database holding one precious record and
  asserts it is still there and unmodified.
- **`Fixture_doesNotRot_whicheverYearItIsOpenedIn`** builds the fixture as if it
  were 2026, 2029 and 2040, and asserts the Completed page's three counters are
  non-zero in every case. A fixture pinned to absolute dates would fail this.
- **`Fixture_coversEveryUiStatePane`** asserts a project exists for every status
  pill colour — so no UI state ships undemonstrated.
- **`SeededDemo_producesARecommendation_soTheLandingPageIsNeverEmpty`** seeds a
  real in-memory database and runs the actual recommendation engine over it. The
  Command Center hero is the first thing a reviewer sees; an empty state there
  makes the whole app look broken.

### `AppOptionsParserTests` — configuration cannot crash or lie

Two properties, asserted directly:

- **Total.** Null input, empty input, whitespace, `!@#$%^&*()` and
  `true;DROP TABLE Projects` all produce valid options rather than an exception.
  A typo in an environment variable must never take the process down at boot.
- **Never silently wrong.** `DemoMode_failsClosedAndWarns_onATypo` runs
  realistic typos (`ture`, `treu`, `enabled`, `y`) and asserts each yields
  `false` **and** a warning. Failing closed matters in this direction
  specifically: an unrecognised value must not switch demo mode on for a
  personal instance.

And the barrier that protects real data:
`DatabasePath_overrideIsIgnoredInDemoMode_soDemoSeedingCanNeverTargetRealData`.

### `RequestValidatorTests` — the trust boundary

These encode the two defects this pass fixed, so they cannot come back:

- **`Update_rejectsABlankProjectName`** and
  **`Update_rejectsANullProjectName_ratherThanThrowing`** — `PUT` used to call
  `.Trim()` unguarded, so a project could be renamed to `""` and a null name
  produced a 500.
- **`Create_andUpdate_agreeOnWhatAValidNameIs`** — a property test over both
  paths. Asymmetric validation is worse than none, because it teaches you the
  field is protected.
- **`OutOfRangeScoresAreRejected_notQuietlyClamped`** — `Impact: 9999` used to
  become `10` and return `200 OK`.
- **`UpdateAction_rejectsAPresentButBlankDescription`** — a rejected input must
  never be indistinguishable from a valid-but-empty one.
- **`TheDemoFixtureItselfPassesValidation`** — a nice cross-check: the demo
  dataset must never display something the API would refuse to accept.

### `BlockingServiceTests` — real SQLite, not a fake

These use **in-memory SQLite**, not the EF in-memory provider, so cascade
deletes, unique indexes and foreign keys behave as they will in production. The
EF in-memory provider enforces none of those, which makes it good at passing
tests and bad at catching bugs.

`Validate_allowsADiamondWhichIsNotACycle` is the one that matters most: two
projects both waiting on a third is a legitimate shape, and an over-eager cycle
check would reject it. Cycle detection needs a test for what it must *allow*,
not only for what it must reject.

## Deliberately not tested, and why

This section is the point. Knowing where coverage stops is more useful than a
percentage.

| Not tested | Why |
| --- | --- |
| **Controllers over HTTP** (`WebApplicationFactory`) | The logic worth asserting has been pulled into `PriorityEngine`, `RequestValidator` and `BlockingService`, all covered directly. What remains in controllers is EF wiring and status codes. An in-process HTTP suite would also need the global process environment mutated per test, which is shared state and a flakiness source. The deployment smoke test covers the real pipeline against the real deployed URL, which is stronger evidence than an in-process fake. |
| **React components** | No test runner is configured, and adding one is a real dependency and maintenance cost. The display logic that has genuine branching — `getStatusDisplay`, `getDeadlineDisplay` — is pure and would be the right place to start if that changes. The components around it are markup. Verified manually against a running instance instead. |
| **End-to-end browser tests** | Playwright against a two-service deployment, one of which sleeps, is a large amount of infrastructure and flakiness for a personal project. The workflows were verified by hand against a locally-running demo instance. |
| **`DbSeeder`'s schema patches** | They exist to migrate databases that already have data in a particular historical shape. Testing them properly means constructing those historical shapes, which is more fixture than the code is worth. They are idempotent and guarded so a failure cannot block startup. |
| **The rate limiter** | It is framework configuration, not application logic. Testing it would be testing ASP.NET. |
| **`Program.cs` wiring** | If it were wrong the app would not start, which every other check would notice immediately. |
| **The concurrency race in action completion** | Known and documented in [SECURITY.md](SECURITY.md#remaining-risks-stated-plainly). Not tested because it is not fixed — a test would encode current behaviour as correct. |

## Test helpers

Kept minimal and local to each suite rather than shared through a base class:

- **`PriorityEngineTests.P(...)` / `A(...)`** — named-argument builders for
  projects and actions, so each test states only the fields it cares about.
- **`BlockingServiceTests` / `DemoDataTests` constructors** — open a single
  `SqliteConnection("Data Source=:memory:")` and hold it for the fixture's
  lifetime. In-memory SQLite drops the database when the last connection closes,
  so the connection *is* the fixture.
- **`DemoDataTests.AllFixtureText()`** — flattens every user-visible string in
  the fixture into one sequence, so a new pattern check is one `[InlineData]`
  line rather than a new traversal.

## In CI

Every push and pull request runs:

- `dotnet build -warnaserror` — the baseline was zero warnings; a gate that
  tolerates "just a few" stops being a gate within a week
- `dotnet test` (Release)
- `npm ci` — frozen lockfile, so drift **fails** instead of silently resolving
  something else
- `oxlint`, `tsc -b`
- **Both** frontend build configurations — default and demo
- An assertion that `dist/404.html` was emitted, because without it every route
  but `/` 404s in production while the build still reports success

## Before and after

| | Before | After |
| --- | ---: | ---: |
| Tests | **0** | **144** |
| Test projects | 0 | 1 |
| Backend build warnings | 0 | 0 |
| npm audit (high+) | 1 | **0** |
| CI | none | 4 jobs |
