# Interview guide

Written to be said out loud. Every claim here is true of the code in this
repository — if you are unsure whether something is accurate, check it before
saying it, because one false claim discovered live costs more than the whole
project earns.

---

## The 30-second version

> "It's a decision-support tool for personal projects. The problem it solves is
> that I'd have fifteen things I was vaguely supposed to be doing, and the one I
> actually picked was whichever I'd thought about most recently — not the one
> that mattered. So it scores every project on impact, urgency and effort, ranks
> them, and shows me one thing: the single next action I should take, and why.
>
> It's an ASP.NET Core API with EF Core and SQLite behind a React frontend. The
> part I'd point at is that almost nothing derived is stored — priority score,
> progress, blocked status, all of it is computed on read. That's what let the
> whole domain layer be a pure static class with no database in it, which is why
> it's the well-tested part."

## The three points to lead with

1. **Derived state is computed, never stored.** It is the decision everything
   else follows from.
2. **That made the domain logic pure**, which is why it is testable and why the
   tests are worth anything.
3. **Deploying it publicly created a real security problem** — an
   unauthenticated shared dataset — **which I solved by changing what gets
   deployed rather than by adding a fake login.**

If you only get to say three things, say those.

## Explaining the architecture

> "There are three layers and they're honest ones — I didn't add them for show.
>
> Controllers do HTTP and nothing else: route, validate, load, save. The domain
> logic lives in two services. `PriorityEngine` is a pure static class — scoring,
> ranking, deadline math, the recommendation algorithm. It doesn't know EF or
> HTTP exist, so I can test all of it with plain objects and no mocks.
> `BlockingService` is the other one, and it's separate specifically *because* it
> needs the database — it walks the project dependency graph.
>
> That split, between 'needs persistence' and 'doesn't', is the only architectural
> line I actually drew. There's no repository interface over the DbContext,
> because a DbContext already *is* a unit of work and a set of repositories.
> Wrapping it would have been a layer to navigate with nothing behind it."

## Request lifecycle

The one to walk through is ticking the last checkbox on a project, because one
click cascades through three separate rules.

> "Say I tick off the last action on a project.
>
> `ProjectDetail.tsx` calls `handleToggleAction`, which goes through
> `api/client.ts` — the only module in the frontend that knows `fetch` exists. It
> prefixes the API base URL from `config.ts`, which is `/api` locally through the
> Vite proxy and an absolute origin when deployed.
>
> Server side: exception middleware wraps everything, then CORS, then the rate
> limiter. `ActionsController.Update` loads the action, its project, that
> project's other actions and its blockers, and runs `RequestValidator` first.
>
> Then three rules fire in order. One: the action is marked done. Two: if every
> action on that project is now done, the project itself completes — nothing left
> to do means nothing left to recommend. Three: because its completed-ness
> changed, `BlockingService.RecomputeDependentsAsync` finds every project waiting
> on this one and re-derives their status, so anything that was only blocked by
> this project flips back to Active on its own. No manual unblock step.
>
> On the way out, `Mapping.ToDto` recomputes the score, the progress and the next
> action from `PriorityEngine`. The frontend refetches and `getStatusDisplay` maps
> the new state to a coloured pill — the finished project goes grey and the one
> that was waiting on it goes green."

That last sentence is the payoff. It is a visible, satisfying cascade from one
checkbox.

## Engineering decisions

Each is decision → alternatives → why → **trade-off**. The trade-off is what
makes it credible; a decision with no cost was not a decision.

### Compute derived state instead of storing it

- **Alternatives:** store `PriorityScore` and `Progress` as columns and update
  them on write; or denormalise with a trigger.
- **Why:** it eliminates an entire bug class — stale derived data. There is no
  "forgot to recalculate" path because there is nothing to recalculate.
- **Trade-off:** ranking happens in memory, so `GET /api/projects` materialises
  every non-completed project before sorting. Fine at hundreds. At a hundred
  thousand I would need the score in an indexed column, and then I would have to
  defend freshness with triggers or a job — I would be buying back the exact bug
  class I removed.

### Two services, split on whether they need the database

- **Alternatives:** one service; or push everything into controllers.
- **Why:** `PriorityEngine` needs no I/O, so making it pure and static costs
  nothing and makes it trivially testable. `BlockingService` genuinely needs
  `AppDbContext`, so it takes one.
- **Trade-off:** two places to look for "the logic". Worth it, because the line
  between them is meaningful rather than arbitrary.

### SQLite, not Postgres

- **Alternatives:** Postgres on Neon or Supabase, both with free tiers.
- **Why:** one user, one file. No server, no connection string to keep secret,
  and a demo instance that is disposable by construction.
- **Trade-off:** single-writer, no concurrent access, no managed backups. All
  irrelevant for one person on one machine, and all of it would matter
  immediately with a second user.

### No authentication

- **Alternatives:** ASP.NET Identity; or an OAuth provider.
- **Why:** it was a tool for one person on `localhost`. Adding accounts would
  have been building a user model with no users.
- **Trade-off:** **this is the one that bit me**, and it is the most interesting
  thing to talk about — see the security section below.

### Reject out-of-range input instead of clamping it

- **Alternatives:** keep clamping (`Math.Clamp(value, 1, 10)`), which is what it
  did.
- **Why:** clamping meant `Impact: 9999` became `10` and returned `200 OK`. The
  caller was told its value was stored as sent, and it was not.
- **Trade-off:** technically a breaking API change. Zero impact in practice — the
  UI is a slider that only produces 1–10 — but I would have needed to version it
  if anything else were consuming the API.

### Split hosting: Pages for the SPA, Render for the API

- **Alternatives:** serve the SPA from the .NET app as static files, one deploy,
  one origin, no CORS.
- **Why:** the free Render instance sleeps after 15 minutes. Co-hosted, a
  reviewer clicking my link gets a blank browser for about a minute. Split, the
  page paints instantly and can *say* the API is waking up.
- **Trade-off:** I took on a CORS allow-list and a base-path problem to avoid a
  bad first impression. Both are configuration and both are tested — but they
  are real complexity I would not have needed otherwise.

### `EnsureCreated()` plus hand-written schema patches, not EF migrations

- **Why:** it predates this work and it functions.
- **Trade-off:** **this is the weakest part of the codebase and I would say so
  unprompted.** It is a growing pile of manual `ALTER TABLE` statements with no
  way to verify that a clean-slate schema matches a patched one. It survives
  because the deployed database is recreated from scratch every cold start, so it
  never performs a migration in production — which is a reason it has not hurt
  yet, not a reason it is right.

## Security talking points

**Lead with the threat model. Always.** It is what turns a checklist into
judgement.

> "The honest answer is that before deploying, most of the web security checklist
> didn't apply. It bound to localhost, one user, no auth — the threat model was
> 'code already running as me', and if you're inside that you've already won.
>
> Deploying changed the model completely. A public URL with no auth means one
> shared dataset that anyone can read, edit or delete. And that's not something
> you fix with a header — bolting auth onto a single-user tool means inventing a
> user model that doesn't exist, to protect data that shouldn't be there.
>
> So I changed what gets deployed instead. Three barriers: the demo data is
> generated in code that's checked into the repo, so there's no export path from
> my machine for real data to travel down. It goes into a different database file,
> and in demo mode the path isn't configurable at all — so no env var typo can
> point the seeder at my real database. And seeding is two separately named
> methods, `SeedIfEmpty` and `ResetToDemo`, so a call site can't ask for the safe
> one and get the destructive one because an argument was wrong.
>
> On top of that, free-tier hosting has no persistent disk. I'd normally call
> that a limitation; here it's the feature. Nothing survives fifteen minutes of
> inactivity, so the exposure is bounded and the demo self-heals.
>
> What I'd say plainly is: the deployed API is intentionally unauthenticated and
> writable. That's an accepted risk, not a solved one. What makes it acceptable is
> that there's nothing valuable behind it and nothing lasts."

### Real findings, worth mentioning

- **The `.gitignore` gap.** `*.db` does not match
  `projectmanager.db.backup-20260820-110842`. A personal database with financial
  and employment records was one `git add -A` from a public repo. Now covered,
  verified with `git check-ignore`, and CI fails if any database file is ever
  tracked.
- **Asymmetric validation.** `POST` checked the project name; `PUT` called
  `.Trim()` unguarded. A project could be renamed to `""`, and a null name was a
  500 instead of a 400. Worse than no validation, because it teaches you the
  field is protected.

### What you deliberately did *not* add — say this, it lands well

> "I took a couple of things back out. I'd started adding a CSP header from the
> API, and then realised the API doesn't serve the pages — GitHub Pages does — so
> it would protect nothing while looking like hardening. Same with anti-CSRF
> tokens: there are no cookies and no ambient credentials, so there's nothing for
> a cross-site request to ride. A control that can't fire is worse than a
> documented gap, because it stops you looking."

## Database

**Four tables.** `Category`, `Project`, `ActionItem`, and `ProjectBlocker` — a
self-referencing join table where `ProjectId` is the stuck project and
`BlockingProjectId` is the one that must finish first.

**Relationships.** `Category 1—* Project` with `SetNull` on delete, so deleting a
category orphans rather than destroys. `Project 1—* ActionItem` with `Cascade` —
actions have no meaning without their project. `ProjectBlocker` cascades from
both sides.

**Indexes.** Unique on `Category.Name`. Non-unique on `Project.Status` and
`Project.CategoryId` — the two filters every list screen uses. Composite on
`(ProjectId, Order)` for actions, which is exactly how "the next action" is
looked up. Unique composite on `(ProjectId, BlockingProjectId)`, so the same
blocker cannot be linked twice.

**Enums are stored as strings**, not ints, so the database is readable and
reordering the enum cannot silently reinterpret existing rows.

**Migrations:** none — see the trade-off above. Own it rather than defending it.

**Access:** EF Core directly, no repository layer. `AsNoTracking()` on reads.

**What breaks at scale:** ranking is in-memory, so the projects endpoint loads
everything not completed. Cycle detection does a BFS with a query per level —
fine for tens of projects, quadratic-ish for thousands. And SQLite is
single-writer, so a second concurrent user is where this design ends.

## Deployment

> "Frontend's on GitHub Pages, API's on Render in Docker, both genuinely free
> with no card. Pages won because the repo was already on GitHub — it adds no
> account and no secret, the deploy authenticates with the workflow's built-in
> token. There is not a single stored secret in this project.
>
> CI runs build, tests, lint, an npm audit gated at high, and gitleaks over the
> full history rather than just the working tree — a credential that was
> committed and later deleted is still leaked.
>
> Two things I'd point out because they usually get skipped. First, CI builds
> *both* frontend configurations — the demo build sets a different base path and
> API origin, and those only take effect at build time, so a demo-only failure
> would otherwise first appear during a deploy. Second, the deploy smoke-tests
> the live URL afterwards: fetches it, checks the SPA mount point is there and
> that asset URLs carry the project base path, then fetches a deep link. A green
> deploy step only proves an upload succeeded."

If asked about gating: CI and deploy run in parallel, not gated. That is a
deliberate trade for a personal site, it is written down in
[DEPLOYMENT.md](DEPLOYMENT.md), and the one-line change to gate them is there too.
**An acknowledged trade-off reads as judgement; an unmentioned one reads as an
oversight.**

## Testing

> "144 tests, and no coverage target — deliberately. Chasing a percentage gets
> you tests written to raise the percentage, and those are exactly the ones that
> don't catch anything.
>
> I picked targets by one question: if this broke, would anything notice. That's
> the priority engine, the cycle detection, the trust boundary, and the
> properties the deployment now depends on — the demo fixture contains nothing
> personal, seeding can't overwrite data, config parsing can't crash or silently
> flip the wrong mode.
>
> The ones I like: there's a test that builds the demo fixture as if it were
> 2029 and 2040 and asserts the 'completed in the last 30 days' counters are
> still non-zero — because every date in the fixture is an offset from now, not
> an absolute timestamp, so it doesn't rot. And there's one asserting a
> *diamond* dependency is allowed, because cycle detection needs a test for what
> it must permit, not just what it must reject."

**Not tested, and why** — have this ready, it is a strength:
controllers over HTTP (the logic was extracted into things tested directly, and
the deployment smoke test covers the real pipeline better than an in-process
fake); React components (no runner configured; the pure display logic is where I
would start); browser end-to-end (too much infrastructure and flakiness for the
value here).

## Deliberate simplifications

Knowing where you did not build something is a stronger signal than a longer
feature list.

| Not built | Why | What it would take |
| --- | --- | --- |
| Authentication | One user, no second user to distinguish | ASP.NET Identity or an OAuth provider, plus per-user data scoping everywhere |
| EF migrations | Predates this work; the deployed DB never migrates | `dotnet ef migrations add`, plus reconciling existing hand-patched databases |
| Postgres | SQLite is right for one user | Provider swap, a connection-string secret, managed backups |
| Search / filter | 15–20 projects fit on a screen | Straightforward; not earned yet |
| Staleness scoring | `UpdatedDate` is already stored so it can be added without a migration | A term in `ComputeScore` |
| Soft delete / audit | Personal tool; delete means delete | `IsDeleted` plus query filters |
| React component tests | No runner configured | Vitest + Testing Library |
| Optimistic concurrency | Single user, so races are unreachable through the UI | A rowversion column, or wrapping the action-completion cascade in a transaction |

## Likely questions

**"Isn't this over-engineered for a personal to-do app?"**
> "It'd be a fair hit if I'd added layers for their own sake, so let me say what
> I *didn't* do: no repository interface, no CQRS, no MediatR, no separate
> domain and infrastructure projects. It's one API project with folders. The
> only real structural decision was pulling the scoring logic into a pure class,
> and that wasn't a pattern — it's what the problem already looked like, because
> prioritisation is arithmetic over in-memory state. The thing it bought me is
> that the whole ruleset is testable without a database."

**"What's the weakest part?"**
> "The persistence bootstrap. It uses `EnsureCreated()` plus hand-written
> `ALTER TABLE` patches instead of EF migrations, and every time I add a field
> that pile grows, with no way to verify a fresh schema matches a patched one.
> It hasn't hurt because the deployed database is recreated from scratch on
> every cold start — so it never actually migrates in production. That's a
> reason it hasn't bitten me, not a reason it's right.
>
> Second one: there's a race in action completion. Several concurrent PUTs can
> each read a stale snapshot, so the 'last action done, auto-complete the
> project' rule might not fire. I found it by firing three requests at once
> during verification. You can't hit it through the UI because it awaits each
> round-trip. I documented it instead of fixing it, because the fix is
> optimistic concurrency and that was outside the scope of this pass."

**"What would you do differently?"**
> "Migrations from day one — it's cheap at the start and expensive to retrofit.
> And I'd have written the priority engine test-first. It's pure, it's all
> arithmetic with edge cases, and it's the highest-value thing in the codebase —
> it should have been the first thing tested, not the last."

**"How do you know the demo has no personal data in it?"**
> "Three ways, and the first is the one that matters. The fixture is code in the
> repo — there's no export step from my machine, so there's no path for real data
> to travel down. Second, demo mode writes to a different database file, and the
> path isn't overridable in demo mode, so no config mistake can point it at my
> real data. Third, there's a test that scans every string in the fixture against
> patterns for emails, phone numbers, URLs, IDs and credentials, and it runs on
> every CI build."

**"Why not just use Todoist / Notion / Linear?"**
> "Those are execution tools — they assume you already know what to do. My
> problem was the decision, not the tracking. None of them will tell you 'do this
> one thing next, and here's why.' It's also 90% of why I kept the scope small:
> the moment this grows search and recurring tasks and notifications, it's a
> worse task manager instead of a decent decision tool."

**"Walk me through a bug you found."**
> "The `.gitignore` one, because the consequence was real. The pattern was
> `*.db`, which looks like it covers SQLite. But my backup file was
> `projectmanager.db.backup-20260820-110842`, and `*.db` doesn't match that — the
> glob needs the name to *end* in `.db`. So my actual database, with retirement
> account details in it, was untracked but not ignored, one `git add -A` from a
> public repo. I found it by running `git check-ignore -v` on the file instead of
> assuming. Fixed the patterns, verified with the same command, and added a CI
> job that fails if a database file is ever tracked — because I'd rather not rely
> on remembering."

**"How long did this take?"**
Answer honestly. Do not inflate it.

## Things not to say

| Don't say | Why it hurts | Say instead |
| --- | --- | --- |
| "It's production-ready" | Invites "for how many users? what's your RTO?" | "It's deployed, tested and documented. It's built for one user and I know exactly where that ends." |
| "It's secure" | An unauthenticated public write API is one question away | "The threat model's narrow and deliberate, and here's what's still open." |
| "Fully tested" / "high coverage" | Invites a hunt for the gap | "144 tests, targeting the rules that matter. Here's what I chose not to test and why." |
| "I used Clean Architecture" | You didn't, and it's checkable | "One project with folders, and one real seam — logic that needs the database versus logic that doesn't." |
| "It scales" | It's SQLite | "It's single-writer. A second concurrent user is where this design ends, and Postgres is the move." |
| "I built a recommendation engine" (implying ML) | Sets an expectation the code contradicts | "A scoring formula with a deadline ramp and eligibility rules. It's arithmetic, and that's why it's testable." |
| Anything about `EnsureCreated` being fine | It isn't | Volunteer it as the weakest part. Naming it first is worth more than defending it. |
