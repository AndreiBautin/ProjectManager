# Demo data

The live demo must never contain personal data. This is the highest-consequence
part of the project, so the guarantee is built **structurally** — three barriers
that make the bad outcome impossible — rather than by being careful.

---

## The three barriers

### 1. Generate, never capture

`Demo/DemoDataFixture.cs` is **code**, checked into a public repository,
readable by anyone. Every value in it was written by hand.

There is no export step from a personal device anywhere in this pipeline — no
script, no dump file, no "sanitise the real database" pass. Personal data cannot
arrive in the demo by accident, because **there is no path for it to arrive by
at all**. A sanitising exporter would be a path, and paths can be got wrong;
this design has none to get wrong.

*Enforced by:* `DemoDataTests.Fixture_containsNothingMatchingPersonalDataPatterns`,
which scans every string in the fixture against patterns for email addresses,
phone numbers, URLs, government IDs, long digit runs, credential assignments and
private key blocks. It runs on every CI build.

### 2. Separate namespaces

Demo data and personal data live in **different SQLite files**:

| Mode | File |
| --- | --- |
| `DEMO_MODE=false` | `projectmanager.db` |
| `DEMO_MODE=true` | `demo.db` |

They cannot collide, because they are not the same file.

The database path is chosen by mode inside `AppOptionsParser`, and in demo mode
the `DATABASE_PATH` environment variable is **ignored outright** — not merely
defaulted. Setting `DATABASE_PATH=projectmanager.db` alongside `DEMO_MODE=true`
produces `demo.db` and a warning saying the override was dropped.

The reasoning: a value that is "defaulted" can be overridden by a copy-pasted
deploy config or a typo. A value that is not read at all cannot.

*Enforced by:*
`AppOptionsParserTests.DatabasePath_overrideIsIgnoredInDemoMode_soDemoSeedingCanNeverTargetRealData`.

### 3. Seed only into empty storage

`DemoDataSeeder` exposes **two separately named operations**, never one function
with a boolean:

```csharp
DemoDataSeeder.SeedIfEmpty(db, now);   // fills an empty database; never deletes
DemoDataSeeder.ResetToDemo(db, now);   // DESTRUCTIVE: wipes, then re-seeds
```

`SeedIfEmpty` returns `SkippedNotEmpty` and writes nothing at all if a single
project already exists. It contains **no code path that deletes**.

Application startup calls `SeedIfEmpty` and only `SeedIfEmpty`. Because the
destructive operation is a different method with a different name, a call site
cannot ask for the safe one and get the dangerous one because an argument was
wrong, defaulted, or read in the wrong order. That failure mode is designed out
rather than guarded against.

*Enforced by:* `SeedIfEmpty_writesNothing_whenAnyProjectAlreadyExists` and
`SeedIfEmpty_neverDestroysExistingData_evenWhenCalledRepeatedly`.

---

## What is in the dataset, and why

19 projects. Every status, every category, every UI state, and deliberate edge
cases. An empty app demonstrates nothing, and a bland one demonstrates almost
nothing — a reviewer should understand what this tool is for within about ten
seconds of landing on it.

### The hero

**"Call the insurance company about the open claim"** — Impact 10, Urgency 10,
Effort 1, scoring **100**, the maximum possible.

It is first on purpose. It is the clearest possible illustration of what the
formula `(Impact × Urgency) / Effort` is *for*: a five-minute task that unlocks
something valuable should beat a big important project, and this is what that
looks like.

### Every UI state is represented

The status pill colours in `utils/status.ts` are the app's core visual language,
so each one has a project demonstrating it:

| Colour | State | Project |
| --- | --- | --- |
| Green | Moving Forward | Ship the portfolio site |
| Amber | Blocked, but its next action *is* the unblock step | Renew the expiring passport |
| Blue | Waiting until a date | Replace the kitchen faucet |
| Purple | Waiting on another tracked project | Book the anniversary trip |
| Red | Blocked and stuck — no defined next action | Learn to develop film at home |
| Grey | No next action defined | Set up a home network rack |
| Grey | Paused | Learn hand-cut dovetail joinery |
| Grey | Completed | Six completed projects |

Plus both deadline states: one project overdue (urgency pinned at 10) and one
inside the 14-day ramp window (urgency actively being pulled up).

### Deliberate edge cases

| Edge case | Project | Proves |
| --- | --- | --- |
| Minimal record | "Return the library books" — name only, no description, no category, no actions, all scores at default | The UI survives what frictionless capture actually produces |
| Very long values | "Migrate the household paperwork archive…" — 117-char name, 450-char description, 185-char action | Layout holds without overflowing |
| Maximum score | The insurance call — 10/10/1 = **100** | Top of the range |
| Minimum score | "Alphabetise the spice rack" — 1/1/10 = **0** | Bottom of the range; correctly ranked last forever |
| Empty state | The "Blocked - stuck" project has zero actions | The state the app exists to make visible |

The long values are real sentences, not lorem ipsum. Placeholder gibberish
proves the box is wide enough and nothing else.

### Relative dates — the fixture does not rot

**Every date is an offset from a supplied `now`.** Nothing is an absolute
timestamp.

This matters concretely. The Completed screen shows "last 30 days" / "last 90
days" / "all time" counters. A fixture pinned to fixed dates would show `0 / 0 /
6` within a few months of being written, and the demo would look abandoned. With
offsets, the counters are always **3 / 5 / 6**, whenever anyone opens it.

`DemoDataFixture.Build(now, categories)` is a pure function — same `now` in,
identical graph out — so it is deterministic and testable while still staying
alive.

*Enforced by:* `Fixture_doesNotRot_whicheverYearItIsOpenedIn`, which builds the
fixture as if it were 2026, 2029 and 2040 and asserts all three counters are
still non-zero.

---

## How seeding works

On startup, when `DEMO_MODE=true`:

1. `DbSeeder.Seed(db)` creates the schema and the six default categories.
2. `DemoDataSeeder.SeedIfEmpty(db, DateTime.UtcNow)` runs.
3. If the database already holds any project, nothing happens.
4. Otherwise the fixture is built against the current time and inserted.
5. Blocker links — expressed by project *name* in the fixture, because IDs do
   not exist until after the insert — are resolved and written.

On the deployed instance this happens on **every cold start**, because free-tier
hosting has no persistent disk. That is deliberate: the demo self-heals. Whatever
visitors did yesterday is gone, and the dataset is pristine again.

## How to reset

**On the deployment:** trigger a manual deploy or restart from the Render
dashboard. The container filesystem is discarded and the next start reseeds.

**Locally:**

```bash
rm demo.db && DEMO_MODE=true dotnet run
```

`ResetToDemo` exists for the wipe-and-replace case and is used by the tests. It
is deliberately not wired to an HTTP endpoint — a public unauthenticated
"delete everything" button would be a poor idea.

## Demo credentials

**There are none, because the app has no authentication.** Nothing to log into,
nothing to hand out. The README says this outright rather than leaving a
reviewer hunting for a login.

No real credential was reused, adapted or referenced anywhere in this project.

## Verification

Run the guarantees yourself:

```bash
dotnet test app/backend/ProjectManager.Tests/ProjectManager.Tests.csproj
```

The relevant tests live in `DemoDataTests.cs`. The ones that matter most:

- `Fixture_containsNothingMatchingPersonalDataPatterns` — barrier 1
- `DatabasePath_overrideIsIgnoredInDemoMode…` (in `AppOptionsParserTests`) — barrier 2
- `SeedIfEmpty_writesNothing_whenAnyProjectAlreadyExists` — barrier 3
- `Fixture_doesNotRot_whicheverYearItIsOpenedIn` — the dataset stays alive
- `TheDemoFixtureItselfPassesValidation` (in `RequestValidatorTests`) — the demo
  never shows something the API would refuse to accept
