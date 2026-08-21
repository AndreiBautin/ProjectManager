# Architecture

A single-user decision-support tool: given a backlog of personal projects, it
answers *"what should I work on next, and what is the exact next step."*

The shape is deliberately small. One API project, one SPA, one SQLite file. The
interesting part is not the layering — it is that **almost nothing is stored
that can be derived**.

---

## The shape

```
┌──────────────────────────────────────────────────────────────┐
│  BROWSER — React 19 SPA (Vite 8, TypeScript)                 │
│                                                              │
│  pages/          screen-level state + data loading           │
│  components/     presentation (ProjectCard, Layout, …)       │
│  utils/status.ts pure display mapping (status → pill colour) │
│  api/client.ts   the ONLY module that knows fetch exists     │
│  config.ts       every build-time environment value          │
└───────────────────────────┬──────────────────────────────────┘
                            │ JSON over HTTP
             dev: /api  →  Vite proxy  →  :5071
             prod: https://<render-host>/api   (CORS allow-list)
                            │
┌───────────────────────────▼──────────────────────────────────┐
│  API — ASP.NET Core 8                                        │
│                                                              │
│  Program.cs        composition root: config, DI, middleware  │
│  Middleware/       exception handling → one error shape      │
│  Controllers/      HTTP only: parse, validate, delegate      │
│  Validation/       pure request validation (trust boundary)  │
│  Services/                                                   │
│    PriorityEngine  PURE domain logic — no DB, no framework   │
│    BlockingService dependency graph — needs the DB           │
│  Dtos/             wire contracts + Mapping.cs               │
│  Demo/             generated fixture + seeder (demo only)    │
│  Data/             AppDbContext, DbSeeder                    │
│  Models/           EF entities                               │
└───────────────────────────┬──────────────────────────────────┘
                            │ EF Core 8
┌───────────────────────────▼──────────────────────────────────┐
│  SQLite — projectmanager.db (personal) / demo.db (demo)      │
│  Category · Project · ActionItem · ProjectBlocker            │
└──────────────────────────────────────────────────────────────┘
```

## What each layer is responsible for

| Layer | Owns | Must not |
| --- | --- | --- |
| **Controllers** | HTTP concerns: routing, status codes, calling the validator, loading and saving through EF | Contain scoring, ranking or eligibility rules |
| **Validation** | Rejecting bad input at the trust boundary | Touch the database |
| **PriorityEngine** | Every rule about *what matters*: score, effective urgency, progress, eligibility, ranking, recommendation, status derivation | Know about EF, HTTP, or the clock beyond `DateTime.Now` |
| **BlockingService** | The project dependency graph: cycle detection, reconciling links, recomputing dependents | Decide priority |
| **Dtos / Mapping** | The wire contract, and computing derived fields onto it | Leak entities to clients |
| **Data** | Schema, indexes, cascades, bootstrap | Contain business rules |
| **Demo** | The generated fixture and how it is seeded | Ever be reachable when `DEMO_MODE` is off |

## The one idea worth understanding

**Derived state is computed on every read, never stored.**

`PriorityScore`, `Progress`, effective urgency, `Blocked` vs `Active`, and
whether an action is workable today are all recalculated from the underlying
facts each time a project is mapped to a DTO. None of them has a column.

That is why `PriorityEngine` is a **pure static class**: it is a set of
functions from project state to answers. It needs no database, no mocks and no
framework to test, which is why it carries the largest share of the test suite.

The cost is real and worth naming: ranking happens **in memory**, so the
`GET /api/projects` handler materialises every non-completed project before
sorting. At a few hundred projects that is irrelevant. At a hundred thousand it
would need the score denormalised into an indexed column, and then the whole
"never stale" property would have to be defended with triggers or a background
job instead. For this app, the trade is clearly right.

## Dependency flow

Everything points inward, and `Program.cs` is the only place anything is
constructed:

```
Controllers ──► Services ──► Data ──► SQLite
     │             │
     └──► Dtos ◄───┘
```

`PriorityEngine` depends on nothing but `Models`. `BlockingService` takes
`AppDbContext` through its constructor. Controllers take what they need through
theirs. There is no service locator and no static database access.

**There is deliberately no repository interface over `AppDbContext`.** A
`DbContext` is already a unit of work and a set of repositories; wrapping it
would add a layer to navigate and buy nothing, since the tests that need a
database use real in-memory SQLite rather than a mock.

## How a request flows, end to end

Ticking the last checkbox on a project — the most interesting path in the app,
because one click cascades through three separate rules.

**1. The click.** `pages/ProjectDetail.tsx:147` → `handleToggleAction` calls
`api.updateAction(id, { status: 'Done' })` and awaits it, then reloads.

**2. The client.** `api/client.ts` → `request()` prefixes `API_BASE_URL` (from
`config.ts` — `/api` locally, an absolute origin when deployed), starts the
slow-request timer that drives the "waking the API" banner, and issues
`PUT /api/actions/13`. A transport failure becomes an `ApiError` with
`isNetworkError: true`; an HTTP error status becomes one with the status code.

**3. Middleware.** `ExceptionHandlingMiddleware` wraps everything, then the CORS
policy (an explicit allow-list from `AppOptions`), then the rate limiter.

**4. The controller.** `Controllers/ActionsController.cs` → `Update` loads the
action with its project, that project's other actions, and its blockers.
`RequestValidator.ValidateUpdateAction` runs first; a present-but-blank
description is a `400`, not a silently ignored no-op.

**5. Rule one — the action.** `action.Status = Done`, `CompletedDate = now`.

**6. Rule two — auto-complete.** Still in `ActionsController.Update`: if every
action on the project is now `Done`, the project itself flips to `Completed`.
Nothing left to do means nothing left to recommend. (Un-ticking one reverses it,
via `PriorityEngine.DeriveStatus`.)

**7. Rule three — cascade.** The project's completed-ness changed, so
`BlockingService.RecomputeDependentsAsync` finds every project whose `Blockers`
list contains this one and re-derives each of their statuses. A project waiting
only on this one flips `Blocked → Active` with no manual unblock step.
`Paused` and `Completed` projects are skipped — those are explicit user choices.

**8. The response.** `Dtos/Mapping.cs` → `ToDto()` computes `PriorityScore`,
`Progress`, `IsBlockedByOpenProjects` and `CurrentNextAction` fresh from
`PriorityEngine`, and returns the DTO.

**9. Back in the browser.** `load()` refetches, and
`utils/status.ts:getStatusDisplay` maps the new state to a pill — the completed
project turns grey, and the project that was waiting on it turns green.

## Configuration and secrets

`AppOptionsParser` is the single place an environment variable becomes
behaviour. It is **pure and total**: it takes a dictionary, returns options plus
a list of warnings, and cannot throw. A typo degrades to a documented default
*and says so in the log* rather than crashing at boot or silently enabling the
wrong mode.

**There are no secrets.** No auth, no third-party API, no database password —
SQLite is a file. The only configuration that matters for safety is
`DEMO_MODE`, and the parser is built so that no configuration value can point
demo seeding at a real database. See [SECURITY.md](SECURITY.md) and
[DEMO_DATA.md](DEMO_DATA.md).

## Error handling

Three distinct layers, each catching what the one below cannot:

| Failure | Handled by | The user sees |
| --- | --- | --- |
| Invalid input | `RequestValidator` → `400` | The specific field and value that was refused |
| Unhandled server exception | `ExceptionHandlingMiddleware` → `500` | A correlation id; the detail only in development |
| API unreachable | `ApiError(isNetworkError)` | "The free-tier server may still be waking up" |
| Render-time exception | `ErrorBoundary` | A recovery screen, not a blank page |

## Auth

**There is none, deliberately.** This is a single-user tool that ran on one
machine; adding accounts would have been ceremony with no user. The deployed
instance therefore serves *one shared dataset that anyone can edit*. That is not
solved by a header — it is handled by changing what gets deployed: generated
demo data, in a separate database, on ephemeral storage. See
[SECURITY.md](SECURITY.md), which leads with the threat model rather than a
checklist.

## Why this shape

- **One API project, not Domain/Application/Infrastructure/Api.** Four projects
  to hold roughly a thousand lines of logic is navigation overhead with no
  payoff. Folders give the same separation without the ceremony.
- **A pure engine, because the domain is genuinely pure.** Prioritisation is
  arithmetic over in-memory state. Isolating it was not a pattern applied to
  the code; it is what the problem already looked like.
- **SQLite, because it is one user and one file.** No server to run, no
  connection string to keep secret, and it makes the demo deployment trivially
  disposable.
- **A separate `BlockingService`, because that logic needs the database.** The
  split between it and `PriorityEngine` is exactly the line between "needs
  persistence" and "does not".
