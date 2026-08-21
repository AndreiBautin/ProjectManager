# Security

## Threat model first

Which controls matter is decided by the threat model, not by a checklist. Naming
what is **structurally absent** is more useful than a list of items marked N/A,
so this document leads with the model and then says what was actually checked.

### Before deployment

The app bound to `localhost`, had no authentication, and held one person's data
on their own machine. The attacker set was "code already running as that user."
If you are inside that model, you have already won — almost nothing in the
standard web checklist applies.

### After deployment — this is the part that matters

A public URL with no authentication means three things, and they are the real
security story of this project:

1. **Every endpoint is reachable by anyone.** There is no login to get past.
2. **There is one global dataset, not one per visitor.** Anyone can read, edit
   and delete what anyone else created.
3. **Write endpoints can be called in a loop.**

**This cannot be fixed with a header.** Bolting authentication onto a
single-user tool would be inventing a user model the app does not have, purely
to protect data that should not be there in the first place.

So the exposure is addressed by changing **what is deployed**, not by adding
guards around it:

| Barrier | Effect |
| --- | --- |
| The deployed instance runs `DEMO_MODE=true` and serves a **generated fixture** | There is no real data to expose. See [DEMO_DATA.md](DEMO_DATA.md). |
| The demo database file is **pinned to `demo.db`** and not overridable | No configuration mistake can point demo seeding at a personal database. |
| Free-tier hosting has **no persistent disk** | Storage is ephemeral. Anything a visitor writes is gone within ~15 minutes of inactivity, and every cold start reseeds a pristine dataset. |
| CORS is an **explicit allow-list**; a literal `*` is dropped by the parser | A config typo cannot open the API to every origin. |
| Fixed-window **rate limiting** per IP | Bounds how fast one client can churn the dataset. |

**Stated plainly: the deployed API is intentionally unauthenticated and
writable. That is an accepted, bounded risk, not a solved problem.** The bound
is that there is nothing valuable behind it and nothing survives for long.

---

## Findings and fixes

| # | Finding | Severity | Resolution |
| --- | --- | --- | --- |
| S1 | `.gitignore` used `*.db`, which does **not** match `projectmanager.db.backup-20260820-110842`. The personal database — containing real financial and employment records — was one `git add -A` away from a public repository. | **High** | **Fixed.** Ignore patterns broadened to `*.db.*`, `*.sqlite*`, `*.bak`, `*backup*`. Verified with `git check-ignore`. CI additionally fails if any database or backup file is ever tracked. |
| S2 | A public deployment exposes an unauthenticated read/write API over a shared dataset. | **High** | **Mitigated structurally**, not fixed — see the threat model above. |
| S3 | No rate limiting on write endpoints. | Medium | **Fixed.** Built-in ASP.NET fixed-window limiter, 120 requests/minute per IP, `429` on rejection, surfaced to the user as a readable message. |
| S4 | No global exception handler; unhandled errors fell through to framework defaults with no consistent shape and no log record. | Medium | **Fixed.** `ExceptionHandlingMiddleware` returns one JSON shape with a correlation id. Detail is included in development only; production returns the id alone. |
| S5 | `PUT /api/projects/{id}` never validated the name. It called `request.Name.Trim()` unguarded, so a project could be renamed to `""`, and a null name produced a `500` instead of a `400`. `POST` validated it; `PUT` did not. | Medium | **Fixed.** `RequestValidator` covers both, and a test asserts create and update agree on what a valid name is. |
| S6 | Out-of-range scores were silently clamped: `Impact: 9999` became `10` and returned `200 OK`, telling the caller its value was stored as sent when it was not. | Medium | **Fixed.** Out-of-range values are now a `400` naming the field and the offending value. The UI only ever sends 1–10, so nothing changes for the app — only for other callers. |
| S7 | `nanoid <3.3.18`, a high-severity advisory reached transitively through Vite. | Medium | **Fixed** via lockfile update. `npm audit` now reports **0 vulnerabilities**. The gate was not lowered. |
| S8 | EF Core logged every SQL statement at `Information`. Parameter values were redacted — verified, not assumed — but the channel existed. | Low | **Fixed.** EF command logging filtered to `Warning`, removing a place a future `EnableSensitiveDataLogging` could start leaking row content. |

## Checked, and found not exploitable

Each of these was actually looked at. Saying so is more useful than marking them
N/A, and more honest than "fixing" something that was never broken.

- **SQL injection.** EF Core parameterises everything. The one raw-SQL site is
  `DbSeeder.ApplySchemaPatches`, which interpolates table and column names — but
  every one of those values is a compile-time string literal in the same file.
  No user input reaches it. Nothing to fix.
- **XSS.** React escapes by default. `grep` confirms **zero** uses of
  `dangerouslySetInnerHTML`, `innerHTML` or `eval` anywhere in `src/`.
- **CSRF.** Structurally absent. No cookies, no sessions, no ambient
  credentials — there is nothing for a cross-site request to ride on. Adding an
  anti-CSRF token here would be theater.
- **IDOR / missing ownership checks.** Structurally absent *because there are no
  owners*. There is one dataset and no user concept. This is not a missing
  check; it is a concept the app does not have — which is precisely why S2 is
  the real finding.
- **Path traversal / file upload.** No endpoint accepts a path or a file.
- **Personal data in logs.** Application code contains no logging of row
  content. Verified empirically: with the demo dataset seeded and the app
  exercised, `grep` over the full startup and request log found **zero**
  occurrences of any project name, description or action text.
- **Secrets in git history.** `git log --all --name-only` over the full history
  shows no `.db`, `.env`, key, token or credential file has ever been committed.
  There are no secrets to rotate.

## What was deliberately *not* added

Security theater is worse than a documented gap, because it looks like
protection:

- **No CSP / HSTS headers from the API.** The frontend is served by GitHub
  Pages, which sets its own transport headers; a header emitted by the API would
  not govern the pages a browser renders. Adding one would look like hardening
  and change nothing.
- **No anti-CSRF tokens.** See above — there is no ambient credential.
- **No input sanitisers.** Data is never rendered as HTML. A sanitiser would
  corrupt legitimate text (an apostrophe in a project name) to prevent an attack
  that cannot occur.
- **No third-party error reporting or analytics.** That would mean shipping this
  app's contents to a vendor for information a personal project does not need.

## Authentication and authorization

There is none, by design, and the README says so plainly rather than inventing a
login for the demo. See the threat model for why that is a deliberate decision
rather than an omission, and what bounds the consequences.

## Input validation

All validation happens at the trust boundary — `Validation/RequestValidator.cs`,
called from every controller action that accepts a body. It is a pure static
class returning a list of problems, so it is unit tested without the HTTP stack.

- Names required and length-capped (200)
- Descriptions (2000), block reasons (1000), action descriptions (500)
- Impact / Urgency / Effort constrained to 1–10 and **rejected**, not clamped
- Enum values (`Status`) parsed explicitly, with `400` on failure
- Blocker links validated for self-reference, unknown ids, and **cycles** via
  BFS in `BlockingService.ValidateBlockersAsync`

A rejected input is never mistaken for a valid-but-empty one: a present but
blank action description is a `400`, not a silently discarded no-op.

## Data protection

- The personal database never leaves the machine and is not in git.
- The deployed database contains only generated fixture data.
- Storage on the free tier is ephemeral by design.
- Logs contain event names and scalars only — never user content.

## Secrets management

There are none. No auth provider, no third-party API, no database credential.

`.env.example` is committed and documents every variable; `.env` is gitignored.
The file carries an explicit warning that Vite's `VITE_` prefix makes a value
**public by definition** — it is compiled into a bundle any visitor can read —
and must never hold a credential.

The GitHub Pages deployment authenticates with the workflow's built-in
`GITHUB_TOKEN`. **No secret was created, stored or rotated for this project.**

## CI scanning

| Gate | Tool | Threshold |
| --- | --- | --- |
| Node dependencies | `npm audit` | Fails at **high** |
| NuGet dependencies | `dotnet list package --vulnerable` | Fails at **high/critical** |
| Secrets | `gitleaks` over **full history** (`fetch-depth: 0`) | Any finding |
| Tracked databases | `git ls-files` pattern check | Any match |
| Backend warnings | `dotnet build -warnaserror` | Any warning |

The audit gate sits at `high` on purpose. Low and moderate findings in
build-time transitive dependencies are constant background noise, and a gate
that fires constantly is one people learn to click past.

Scanning history rather than the working tree matters: a credential that was
committed and later deleted is still a leaked credential.

## Remaining risks, stated plainly

1. **The deployed API is unauthenticated and writable.** Anyone can create,
   edit and delete demo records. Accepted; bounded by generated data and
   ephemeral storage.
2. **The demo dataset is shared between all visitors.** Two people using the
   demo at once will see each other's edits.
3. **Rate limiting is per-IP and in-memory.** It does not survive a restart and
   does not stop a distributed caller. It bounds accidental hammering, not a
   determined attacker.
4. **A concurrency race exists in action completion.** Several `PUT /api/actions/{id}`
   requests arriving simultaneously can each read a stale snapshot, so the
   "last action done → auto-complete the project" rule may not fire. Reproduced
   deliberately by firing three requests at once; not reachable through the UI,
   which awaits each round-trip. Unfixed — the fix is optimistic concurrency or
   a transaction, which is scope beyond this pass.
5. **Deep links return HTTP 404 with the correct page body.** GitHub Pages has
   no rewrite rules; `404.html` is the SPA shell. Invisible to a person, visible
   to a crawler.
6. **No backup of the personal database.** Out of scope here, but worth saying
   out loud: it exists in exactly one place.
