# Personal COO

A decision-support tool for a personal project backlog. It answers one question:
**what should I work on next, and what is the exact next step?**

Not a task manager and not a calendar. There is no scheduling and no capacity
math — just an always-current priority order, and one specific, doable thing at
the top of it.

### ▶ [Live demo](https://andreibautin.github.io/ProjectManager/)

**No login — there is no authentication at all.** Everything you see is
generated sample data, shared by everyone, and reset whenever the free-tier
server restarts. Edit and delete freely; nothing there is real.

> The API runs on a free instance that sleeps after 15 minutes of inactivity. If
> the page loads but the data does not, the backend is waking up — it takes about
> a minute, and the app tells you that is what is happening.
>
> **Status:** the frontend is deployed. The API is not yet — the Render service
> still needs to be created (see [DEPLOYMENT.md](docs/DEPLOYMENT.md#2-create-the-render-service)),
> so right now the app loads and reports that it cannot reach the API. Delete
> this note once the backend is up.

---

## What it does

- **One recommended next action.** Not a list — a single thing, chosen by the
  engine, with the reason it was chosen.
- **Priority scoring.** `(Impact × Urgency) / Effort`, computed on every read so
  it can never be stale.
- **Deadlines that ramp.** An optional deadline pulls effective urgency toward
  10 over the final 14 days, and pins it there once due. It only ever pushes
  urgency up — a task that feels minor day to day still reaches the top when a
  real external cutoff arrives.
- **Project dependencies.** Mark a project as blocked by *another project*, with
  cycle detection. When the blocker completes, everything waiting on it
  un-blocks automatically.
- **Date-gated actions.** A step that cannot start until a certain date stays
  visible but is skipped by the recommendation engine until then.
- **"Blocked and stuck" is a visible state.** A blocked project with no defined
  next step is the one that silently disappears for months, so it gets its own
  colour.
- **Derived progress.** Percentage of actions completed. Finishing the last one
  closes the project out.

## Architecture

```
React 19 SPA  ──fetch──►  ASP.NET Core 8 API  ──EF Core──►  SQLite
(GitHub Pages)            (Render, Docker)                  (one file)
```

**The one idea that makes it click: almost nothing is stored that can be
derived.** Priority score, progress, effective urgency, blocked-vs-active and
action eligibility have no columns — they are recomputed from the underlying
facts on every read. That is why the domain logic lives in a **pure static
class** with no database and no framework anywhere near it, and why it carries
most of the test suite.

→ [Full architecture, with a request traced end to end](docs/ARCHITECTURE.md)

## Tech stack, and why

| Choice | Why this one |
| --- | --- |
| **ASP.NET Core 8** | Strong typing and a genuinely good ORM for a data-shaped domain, with a built-in DI container, rate limiter and configuration system — no framework shopping. |
| **EF Core + SQLite** | One user, one file. No server to run, no connection string to keep secret, and a demo instance that is trivially disposable. |
| **React 19 + TypeScript** | The UI is a handful of screens over shared state. The API's DTOs are mirrored as TypeScript types, so a backend contract change surfaces as a compile error. |
| **Vite 8** | Sub-second builds, and a dev proxy that removes CORS from local development entirely. |
| **oxlint** | Fast enough to never be the reason a commit is slow. |
| **xUnit** | Standard for .NET; theories make property-style tests concise. |
| **GitHub Pages + Render** | Both genuinely free with no card. Pages adds no account and no secret — the deploy authenticates with the workflow's built-in token. |

## Security

No authentication — deliberately, for a single-user tool. Which means the
deployed instance is a public, writable API over a shared dataset, and that is
the central security decision of this project. It is handled by changing *what
gets deployed* — generated demo data, in a separate database, on ephemeral
storage — rather than by bolting on a user model the app does not have.

Also fixed here: a `.gitignore` pattern that would have let a personal database
into a public repo, asymmetric validation between create and update, and
silently-clamped out-of-range input.

→ [Threat model, findings, and the risks that remain](docs/SECURITY.md)

## Testing

**144 tests**, up from zero. No coverage target — the suite targets the rules
the app exists to enforce, the trust boundary, and the properties the deployment
now depends on (the demo fixture contains nothing personal; seeding cannot
overwrite data; configuration cannot crash or silently enable the wrong mode).

→ [Strategy, and what is deliberately not tested](docs/TESTING.md)

## Deployment

Frontend on GitHub Pages, API on Render, both free with no credit card. CI runs
build, tests, lint, a dependency audit gated at `high`, and a secret scan over
the full history. The deploy smoke-tests the live URL afterwards — a green
deploy proves an upload succeeded, a green smoke test proves the site answered.

→ [Setup, alternatives rejected, and troubleshooting](docs/DEPLOYMENT.md)

## Running it locally

Requires the **.NET 8 SDK** and **Node 18+**.

**Windows — one click:** double-click [`app/start.bat`](app/start.bat). It starts
both halves, waits for each, detects port conflicts, and opens the browser.

**Any OS — manually:**

```bash
cd app/backend/ProjectManager.Api && dotnet run
```

```bash
cd app/frontend/project-manager-web && npm install && npm run dev
```

The API listens on `:5071`, the app on `:5174`, and the Vite dev server proxies
`/api` to the backend — so there is no CORS in local development. On first run
the API creates `projectmanager.db` and seeds the default categories.

**To run the demo dataset locally** instead of an empty personal database:

```bash
cd app/backend/ProjectManager.Api && DEMO_MODE=true dotnet run
```

That writes to `demo.db`, never to `projectmanager.db` — the path is not
overridable in demo mode, on purpose.

**Tests:**

```bash
dotnet test app/backend/ProjectManager.Tests/ProjectManager.Tests.csproj
```

Every environment variable is documented in [`.env.example`](.env.example).

## Documentation

| | |
| --- | --- |
| [Architecture](docs/ARCHITECTURE.md) | Layers, dependency flow, a request traced end to end |
| [Security](docs/SECURITY.md) | Threat model, findings, remaining risks |
| [Demo data](docs/DEMO_DATA.md) | The three barriers keeping personal data out |
| [Deployment](docs/DEPLOYMENT.md) | Hosting, CI/CD, troubleshooting |
| [Testing](docs/TESTING.md) | Strategy, and what is deliberately untested |
| [Design document](design-doc.md) | Original concept, schema, and UX rationale |
| [Productionization assessment](docs/PRODUCTIONIZATION_ASSESSMENT.md) | Honest review of the code before this pass |

## Scope

Deliberately absent: authentication, notifications, calendar integration, search
and filtering, recurring tasks, team features, analytics, staleness-based
scoring, and a mobile app. Each was considered and left out — a personal
decision-support tool gets worse as it acquires the features of a task manager.
