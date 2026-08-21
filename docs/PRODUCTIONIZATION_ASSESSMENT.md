# Productionization Assessment — Personal COO

**Date:** 2026-08-20
**Assessed commit:** `55e043b`
**Assessor's brief:** take a working single-user local app and make it deployable,
defensible, and explainable — without rewriting it.

---

## 1. Baseline (measured, not assumed)

Every number below came from running the command on 2026-08-20.

| Check | Command | Result |
| --- | --- | --- |
| Backend build | `dotnet build` | **Succeeded, 0 warnings, 0 errors** |
| Frontend typecheck + build | `npm run build` | **Succeeded** — 34 modules, 257.30 kB JS (80.12 kB gzip) |
| Frontend lint | `npx oxlint` | **Clean, exit 0** |
| Tests | — | **0 tests. No test project exists.** |
| Dependency audit | `npm audit` | **1 high** (`nanoid <3.3.18`, transitive via Vite) |
| Secrets in git history | `git log --all --name-only` | **None.** No `.db`, `.env`, key or credential file has ever been committed. |
| Running app | `GET /api/health` | **Live and healthy** at `localhost:5071` |

A note on the build: the first `dotnet build` failed with `MSB3027` — the output
`.exe` was locked by the user's *currently running* instance (PID 20348). That is an
environment condition, not a code defect. Rebuilt to a separate output directory to
confirm the code compiles clean without disturbing the running app.

The previous `app/README.md` warned that the backend had never been compiled because
it was scaffolded without NuGet access. **That warning is now stale — the backend
compiles clean.** It should be removed.

---

## 2. Current architecture

```
                    Browser (React 19 SPA, Vite 8)
                              │
                    fetch('/api/...')  ← relative URL
                              │
                    Vite dev server :5174  ── proxy ──┐
                              │                        │
                              ▼                        ▼
              ASP.NET Core 8 Web API :5071 ────────────┘
                              │
        ┌─────────────────────┼──────────────────────┐
        ▼                     ▼                      ▼
  Controllers/          Services/                Data/
  Projects              PriorityEngine (pure)    AppDbContext
  Actions               BlockingService (db)     DbSeeder
  Categories                  │
  Recommendation              ▼
        │              Dtos/Mapping.cs
        └──────────────────► ProjectDto
                              │
                              ▼
                         SQLite file
                     projectmanager.db
```

**Entities:** `Category`, `Project`, `ActionItem`, `ProjectBlocker` (self-referencing
join for project-to-project dependencies).

**Derived, never stored:** priority score, progress %, effective urgency,
`Blocked`/`Active` status, action eligibility. This is the app's best design decision
and it is deliberate — the design doc reasons about it explicitly.

---

## 3. Honest strengths

I want to be direct here, because an assessment that manufactures problems in order to
justify a rewrite is worthless. **This code is better than most personal projects, and
the parts that matter most are the parts that are best.**

1. **`PriorityEngine` is a pure static class.** Scoring, ranking, deadline ramping,
   progress, eligibility and recommendation selection are all pure functions over
   in-memory objects. No database, no clock injection needed for most of it. This is
   *ideal* for testing, and it means the interesting logic is already isolated from
   the framework. Very few personal projects get this right.
2. **A real seam already exists** between HTTP (Controllers), domain logic (Services)
   and persistence (EF Core `AppDbContext`). It was not imposed as ceremony — it grew
   because the logic actually needed somewhere to live.
3. **DTOs are separate from entities**, and mapping is centralised in one file
   (`Dtos/Mapping.cs`). Entities are never returned over the wire.
4. **Derived state is computed, not stored.** This eliminates an entire class of
   "forgot to recalculate" bugs, and the codebase is consistent about it.
5. **The comments explain *why*, not *what*.** `DropColumnIfPresent`,
   `ComputeEffectiveUrgency`, and `GetRecommendation` each carry a paragraph
   explaining the reasoning and the alternatives rejected. This is unusual and
   genuinely valuable.
6. **Cycle detection is correct.** `BlockingService.DependsOnAsync` does a proper BFS
   with a visited set before allowing a blocker link. Most people skip this.
7. **No secret has ever been committed.** History is clean.
8. **A written design doc exists** that reasons about spec flaws (divide-by-zero on
   Effort=0, dual-tracked status, undefined tie-breaking) and fixes them.

The consequence for this workflow: **there is no architectural rewrite to do.** The
gap between this app and a deployable one is almost entirely configuration, testing,
deployment and documentation — which is exactly the normal shape of that gap.

---

## 4. Weaknesses that matter

Ordered by impact. Imperfections that do not matter are deliberately omitted.

| # | Finding | Where | Impact | Why it matters |
| --- | --- | --- | --- | --- |
| 1 | **Zero automated tests** | whole repo | **High** | `PriorityEngine` is pure and therefore trivially testable, and it holds every rule the app exists to enforce. Untested, any refactor is a guess. |
| 2 | **Personal DB backup is not gitignored** | `.gitignore` | **High (privacy)** | `*.db` does **not** match `projectmanager.db.backup-20260820-110842`. One `git add -A` publishes real financial and employment data. Verified with `git check-ignore` — the file is currently untracked but *not* ignored. |
| 3 | **CORS origins hardcoded to `localhost:5174`** | `Program.cs:21-28` | **High** | Hard deployment blocker. No deployed frontend can call this API. |
| 4 | **No configuration layer at all** | `Program.cs`, `client.ts` | **High** | Connection string, API base URL, ports, CORS are all literals. Nothing distinguishes dev / prod / demo. Deployment is impossible without this. |
| 5 | **No CI** | — | **High** | Nothing verifies a commit. The stale "never compiled" README warning survived four commits precisely because nothing checked. |
| 6 | **`Update` does not validate project name** | `ProjectsController.cs:135` | **Medium (real bug)** | `Create` rejects a blank name; `Update` calls `request.Name.Trim()` with no check. A project can be renamed to `""`, and a `null` name throws `NullReferenceException` → 500. Asymmetric validation at a trust boundary. |
| 7 | **Out-of-range scores are silently coerced** | `ProjectsController.Clamp` | **Medium** | `Impact: 9999` becomes `10` and returns `200 OK`. The caller is told it succeeded as sent. A rejected input should not be indistinguishable from a valid one. |
| 8 | **`EnsureCreated()` + hand-rolled DDL patches** | `Data/DbSeeder.cs` | **Medium** | Works today and is honestly commented, but it is a growing pile of manual `ALTER TABLE` statements with no way to verify a clean-slate schema matches a patched one. |
| 9 | **No global exception handler** | `Program.cs` | **Medium** | Unhandled exceptions fall through to framework defaults. No consistent error shape for the client, no structured log record. |
| 10 | **No React error boundary** | `main.tsx` | **Medium** | Any render-time exception blanks the entire page with no recovery path. On a live portfolio link that reads as broken. |
| 11 | **`nanoid` high-severity advisory** | `package-lock.json` | **Medium** | Transitive via Vite. Fixable with a lockfile bump. |
| 12 | **No `.gitattributes`** | — | **Low-Medium** | Windows dev + Linux CI. Without `eol=lf`, any formatting gate passes in one place and fails in the other. |
| 13 | **Windows-only run scripts** | `start.bat`, `*.bat` | **Low** | `start.bat` is genuinely good, but a reviewer on macOS/Linux has no equivalent path. |
| 14 | **No root README** | — | **Low (high visibility)** | The README lives at `app/README.md`. GitHub shows the repo root. An employer lands on a file listing. |

### Explicitly *not* flagged

- Single API project instead of Domain/Infrastructure/Api layering — **correct for
  this app.** The design doc argues this and the argument is right.
- No repository interface over `AppDbContext` — `DbContext` is already a unit of work
  plus repository. Adding an interface layer here buys nothing and costs navigation.
- No DI container beyond the built-in one — the built-in one is sufficient.
- No CQRS, no MediatR, no event sourcing. Nothing in this domain earns them.

---

## 5. Security findings

### Threat model first

Naming what is *structurally absent* matters more than a checklist of N/A items.

**Today (before this work):** the app binds to `localhost` only, is reached solely
from the same machine, has no authentication, and holds one person's data. The
attacker set is "processes already running on the user's machine" — which is to say,
if you are in the threat model you have already won. Almost nothing in the standard
web checklist applies.

**After deployment, this changes completely.** A public URL with no auth means:

- Every endpoint is reachable by anyone.
- There is **one global dataset**, not one per visitor. Any visitor can read, edit and
  delete what any other visitor created.
- Write endpoints can be called in a loop by anyone.

That is the central security decision of this project, and it cannot be fixed by
adding a header. It is addressed in Phase 3 (demo data) and Phase 4 (config) by
changing *what is deployed* rather than by bolting auth onto a single-user app.

### Findings

| # | Finding | Severity | Status |
| --- | --- | --- | --- |
| S1 | Personal DB backup not covered by `.gitignore` | **High** | To fix — broaden the ignore pattern |
| S2 | Public deployment would expose an unauthenticated read/write API over a **shared global dataset** | **High** | To mitigate structurally: deploy demo-generated data only, into ephemeral storage, in a separate DB namespace |
| S3 | No rate limiting on write endpoints | **Medium** | To fix — built-in ASP.NET rate limiter |
| S4 | No global exception handler → inconsistent/verbose error surface | **Medium** | To fix |
| S5 | `Update` accepts `null`/blank project name → 500 or empty-named record | **Medium** | To fix |
| S6 | CORS must not become `AllowAnyOrigin` when made configurable | **Medium** | Design constraint on the fix for #3 above |

### Checked and found **not** exploitable

Stating these is more useful than marking them N/A, because each one was actually
looked at:

- **SQL injection.** EF Core parameterises everything. The one raw-SQL site is
  `DbSeeder.ApplySchemaPatches`, which interpolates table and column names — but every
  one of those values is a **compile-time string literal** in the same file. No user
  input reaches it. Not exploitable; not "fixed", because there is nothing to fix.
- **XSS.** React escapes by default and `grep` confirms **zero** uses of
  `dangerouslySetInnerHTML`, `innerHTML` or `eval` anywhere in `src/`.
- **CSRF.** Structurally absent: there are no cookies, no sessions and no ambient
  credentials. There is nothing for a cross-site request to ride on.
- **IDOR / missing ownership checks.** Structurally absent *because there are no
  owners*. There is exactly one dataset and no user concept. This is not a check that
  is missing — it is a concept the app does not have. Which is precisely why S2 above
  is the real finding.
- **Personal data in logs.** Default ASP.NET logging records method, path and status.
  Project names, descriptions and action text are never logged. Verified by reading
  every logging call site — there are none in application code.
- **Secrets in history.** `git log --all --name-only` over the full history returns no
  `.db`, `.env`, key, token or credential file.

### No security theater

Deliberately **not** adding: a CSP header that a static host would serve but that this
SPA's inline-free build does not need tightened; HSTS on a platform that already
terminates TLS and sets it; input sanitizers on data that React never renders as HTML.
Each of those would look like protection while protecting nothing.

---

## 6. Data & privacy concerns

This is the highest-consequence section.

1. The live SQLite DB contains **real personal data** — verified: retirement account
   details, employer names, financial task descriptions. It must never reach a
   deployed environment, and it must never reach git.
2. `.gitignore` does not currently cover `*.db.backup-*`. **This is the single most
   dangerous line in the repo** — see S1.
3. The deployed app must be seeded from a **generated fixture checked into the
   repository**, with no export path from the user's machine anywhere in the pipeline.
4. Demo and personal data must live in **different database files**, so they cannot
   collide even by accident.
5. Because the `Completed` page computes "last 30 days" / "last 90 days" statistics, a
   fixture pinned to absolute dates will show three zeros within a month of being
   written. The fixture **must** use offsets from seed time.

---

## 7. Deployment concerns

| Concern | Detail |
| --- | --- |
| Two processes | .NET API + static SPA. Either co-host or split. |
| Stateful backend | SQLite is a file. Free tiers rarely offer persistent disks. |
| No auth | A public URL is a public dataset. Drives the demo-data strategy. |
| Hardcoded CORS | Blocks any split deployment until made configurable. |
| Relative `/api` base | Works only behind the Vite proxy. Needs build-time config. |
| Repo is **private** | GitHub Pages on a free account requires a **public** repo. |
| Token scope | The local `gh` token has `repo` but **not** `workflow` — it cannot push `.github/workflows/*`. |

### Free-hosting research (verified against current docs, not memory)

| Provider | Card required? | Verdict |
| --- | --- | --- |
| **GitHub Pages** | No | **Selected for frontend.** Repo is already on GitHub; deploy authenticates with the workflow's built-in `GITHUB_TOKEN`. No new account, no new secret. Requires the repo to be public. |
| **Render (free web service)** | **No** — docs confirm free services are *suspended*, not billed, when limits are hit; payment method is optional | **Selected for backend.** 750 instance-hours/month, Docker support. Spins down after 15 min idle (~1 min cold start). **No persistent disk on free.** |
| Fly.io | Yes — card required for the current plan | Rejected |
| Railway | No real free tier remaining | Rejected |
| Azure App Service F1 | Azure signup requires a card | Rejected |
| Google Cloud Run | Requires a billing account | Rejected |

**The lack of a persistent disk is not a problem here — it is the right fit.** The
deployed instance is a demo. Ephemeral storage means every cold start reseeds a
pristine dataset, and anything a visitor types disappears within ~15 minutes of idle.
That converts finding S2 from an unbounded exposure into a bounded, self-cleaning one.

---

## 8. Recommended architecture

**No structural rewrite.** Targeted additions only:

```
  GitHub Pages  ──────────────────►  Render free web service
  (React SPA,                        (ASP.NET Core in Docker)
   instant, never sleeps)                     │
        │                                     ▼
        │  VITE_API_BASE_URL            ephemeral SQLite
        │  (build-time config)          demo.db  ← generated fixture,
        └──── CORS allow-list ────────►         seeded only when empty
```

Additions, in order:

1. **Config layer** — `AppOptions` parsed from environment, pure and total, unit
   tested. Bad input degrades to a documented default with a warning; it never crashes
   startup and never silently enables the wrong mode.
2. **Demo data subsystem** — generated fixture, separate DB file, two *named*
   operations (`SeedIfEmpty` / `ResetToDemo`), relative dates.
3. **Validation + error handling** — fix the `Update` name gap, make out-of-range
   scores an explicit `400`, add a global exception handler with a consistent shape.
4. **Rate limiting** — built-in ASP.NET limiter on writes.
5. **Test project** — xUnit, targeting `PriorityEngine`, `BlockingService`, config
   parsing, and the properties the deployment now depends on.
6. **CI** — build, test, lint, audit gated at `high`, full-history secret scan.
7. **Docs + root README + interview guide.**

## 9. Major risks

| Risk | Mitigation |
| --- | --- |
| Personal data reaches the public deploy | Three structural barriers, not care: generate-never-capture, separate DB namespace, seed-only-into-empty. Plus a test that scans the fixture for personal-data patterns. |
| Personal DB reaches git | Broaden `.gitignore`; add a full-history secret scan to CI. |
| Public unauthenticated API abused | Demo data only + ephemeral storage + rate limiting. Documented as an **accepted, bounded** risk — not claimed as solved. |
| Render cold start reads as "broken" | Frontend on Pages paints instantly and shows an explicit "waking the free-tier API" state rather than an unexplained spinner. |
| Repo must go public | **User decision — blocking.** Cannot be done unilaterally. |
| `gh` token lacks `workflow` scope | **User action — blocking for CI push.** |

## 10. Implementation order

1. Privacy first: fix `.gitignore`, add `.gitattributes`.
2. Config layer + tests for it.
3. Validation and error-handling fixes.
4. Demo data subsystem + purity test.
5. Test project — `PriorityEngine`, `BlockingService`, config, demo properties.
6. Deployment artifacts: Dockerfile, `render.yaml`, Pages base path + SPA fallback.
7. CI workflows.
8. Docs: `ARCHITECTURE`, `SECURITY`, `DEMO_DATA`, `DEPLOYMENT`, `TESTING`, root
   `README`, `INTERVIEW_GUIDE`.
9. Verify against the live URL.

---

*Assessment complete. Implementation proceeds without waiting for approval; anything
requiring a decision only the user can make is called out as blocking above and
repeated in the final report.*
