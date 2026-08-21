# Deployment

Two free services, no credit card, no secret to store.

```
   git push origin main
          │
          ├──────────────────────────┬───────────────────────────
          ▼                          ▼
  GitHub Actions                GitHub Actions
  ci.yml                        deploy-pages.yml
  build · test · lint           build SPA with base path
  audit · secret scan           publish → GitHub Pages
                                smoke-test the live URL
                                        │
                                        ▼
                          https://andreibautin.github.io/ProjectManager/
                                        │
                                        │  fetch (CORS allow-list)
                                        ▼
                          https://personal-coo-api.onrender.com/api
                          Render free web service · Docker · ASP.NET Core 8
                                        │
                                        ▼
                          ephemeral SQLite · demo.db · reseeded on cold start
```

---

## Why this split

The frontend and the backend are hosted separately, and that is a deliberate
choice rather than an accident of tooling.

**GitHub Pages for the SPA.** The repository is already on GitHub, so this adds
**no new account and no new secret** — the deploy authenticates with the
workflow's built-in `GITHUB_TOKEN`. Pages never sleeps, so the page paints
instantly regardless of the backend's state.

**Render for the API.** It is the one provider verified to run a Docker
container on a genuinely free plan without requiring a card up front.

**Why not co-host both on Render?** Serving the SPA from the .NET app would be
simpler — one origin, no CORS, no base path. But the free instance sleeps after
15 minutes, so a reviewer clicking the link would get a blank browser for
roughly a minute before anything appeared. Splitting means the UI is instant and
the app can *say* the API is waking up, which reads as a deliberate design
rather than a broken site. The cost is a CORS allow-list and a base path, both
of which are handled in configuration and covered by tests.

### Alternatives considered and rejected

| Option | Rejected because |
| --- | --- |
| **Fly.io** | Requires a payment method on the current plan. |
| **Railway** | No real free tier remaining — trial credits then paid. |
| **Azure App Service (F1)** | The F1 plan is free, but creating an Azure subscription requires a card. |
| **Google Cloud Run** | Requires an active billing account. |
| **Vercel / Netlify** | Would work for the SPA, but each adds an account and a build integration for something GitHub Pages already does with a token that exists. |
| **Co-hosting on Render** | Cold-start blank page — see above. |
| **Neon / Supabase Postgres** | Would add a real database and a **connection-string secret** to manage, to replace a file that works. The demo does not need durability; it benefits from not having it. |

### On "free"

Render's documentation states that when a free service exceeds its limits,
Render **suspends** the service rather than billing for it, and describes adding
a payment method as optional (*"If you haven't added a payment method, Render
instead suspends all of your Free services"*). Free web services get 750
instance-hours per month.

Provider terms change. If a card is requested at signup, stop — the backend is
optional, and the frontend deploys and runs on GitHub Pages regardless.

---

## What you must do manually

Two steps require a human. Everything else is automated.

### 1. Make the repository public *(required for GitHub Pages)*

GitHub Pages needs a public repository on a free account. The repository is
currently **private**.

Before doing this, note what becomes public: all source, the design document,
and these docs. **No personal data is in git** — verified: no database file has
ever been committed, and `.gitignore` now covers `*.db.*`, `*.sqlite*`, `*.bak`
and `*backup*`.

```bash
gh repo edit AndreiBautin/ProjectManager --visibility public --accept-visibility-change-consequences
```

Then enable Pages with GitHub Actions as the source:
**Settings → Pages → Build and deployment → Source: GitHub Actions.**

### 2. Create the Render service

1. Sign up at [render.com](https://render.com) with the GitHub account. Do not
   enter a card.
2. **New → Blueprint**, and point it at this repository. It reads
   [`render.yaml`](../render.yaml) and configures the service.
3. Deploy. The first build takes a few minutes.
4. Note the assigned URL, e.g. `https://personal-coo-api.onrender.com`.

If the hostname differs from the one in `render.yaml`, set a repository variable
so the Pages build points at the right place:

```bash
gh variable set API_BASE_URL --body "https://<your-service>.onrender.com/api"
```

And update `CORS_ALLOWED_ORIGINS` in `render.yaml` if the Pages URL differs from
`https://andreibautin.github.io`.

### 3. A note on pushing workflows

The local `gh` token has scopes `gist, read:org, repo` — but **not `workflow`**.
GitHub rejects a push that adds or edits `.github/workflows/*` without it:

```bash
gh auth refresh -h github.com -s workflow
```

---

## Environment variables

Full reference in [`.env.example`](../.env.example). What the deployment sets:

### Render (backend) — set by `render.yaml`

| Variable | Value | Why |
| --- | --- | --- |
| `DEMO_MODE` | `true` | Serves the generated fixture, pins the database to `demo.db` |
| `CORS_ALLOWED_ORIGINS` | `https://andreibautin.github.io` | Origin only — no path, no trailing slash |
| `ENABLE_SWAGGER` | `false` | Swagger is a live writable UI onto a public API |
| `LOG_LEVEL` | `Information` | Safe to leave on — logs carry scalars only |
| `BUILD_COMMIT` | *(from Render)* | Ties `/api/health` to a commit |
| `PORT` | *(from Render)* | Read at container runtime, not build time |

### GitHub Pages (frontend) — set by `deploy-pages.yml`

| Variable | Value |
| --- | --- |
| `VITE_BASE_PATH` | `/${{ repository.name }}/` |
| `VITE_API_BASE_URL` | repo variable `API_BASE_URL`, or the `render.yaml` default |
| `VITE_DEMO_MODE` | `true` |
| `VITE_BUILD_COMMIT` | `github.sha` |

**Every `VITE_`-prefixed value is compiled into the public bundle.** None of
these is a secret, and none ever should be.

## Database and migrations

There are no migration files. The app uses `EnsureCreated()` plus idempotent
hand-written schema patches in `Data/DbSeeder.cs`, applied on every startup.

That is a real limitation and is named as such in the
[assessment](PRODUCTIONIZATION_ASSESSMENT.md): it works, it is honestly
commented, but it is a growing pile of manual DDL with no way to verify that a
clean-slate schema matches a patched one. It was left alone because rewriting a
working persistence bootstrap was not the job here, and because the deployed
database is recreated from scratch on every cold start — where it has no
migrations to perform at all.

**Seeding** is automatic. See [DEMO_DATA.md](DEMO_DATA.md).

## How deploys trigger

| Event | Runs |
| --- | --- |
| Push to `main` | `ci.yml` and `deploy-pages.yml` **in parallel**; Render auto-deploys |
| Pull request | `ci.yml` only |
| Manual | Both workflows have `workflow_dispatch` |

### CI and deploy are not gated — deliberately, for now

They run **concurrently**, so a commit that fails tests can still reach Pages.
That is an acceptable trade for a personal portfolio site — the deploy's own
smoke test catches anything that would make the page unusable, and gating adds a
few minutes to every publish.

If that trade stops being right, it is one line. In `deploy-pages.yml`, change:

```yaml
  build:
    name: Build
    runs-on: ubuntu-latest
```

to:

```yaml
  build:
    name: Build
    needs: [backend, frontend]   # requires ci.yml jobs to be in this workflow
    runs-on: ubuntu-latest
```

— or, keeping them as separate files, add `workflow_run` so the deploy triggers
only on a successful CI run rather than on push.

## Verifying a deploy

The Pages workflow smoke-tests itself: it fetches the live URL and asserts the
response contains the SPA mount point, the expected title, and asset URLs
carrying the project base path — then fetches a deep link and asserts the shell
comes back. **A green deploy step means an upload succeeded; a green smoke test
means the site answered.**

Manually:

```bash
curl -s https://personal-coo-api.onrender.com/api/health
```

```bash
curl -sI https://andreibautin.github.io/ProjectManager/
```

The footer of the running app shows both the web and API build ids, so you can
confirm which commit is live without leaving the page.

## Updating

Push to `main`. Pages redeploys and Render auto-deploys.

## Resetting the demo data

Restart or redeploy the Render service. The container filesystem is discarded,
and the next start reseeds a pristine fixture into an empty database.

## Troubleshooting

| Symptom | Cause | Fix |
| --- | --- | --- |
| Page loads blank, console shows 404s for `/assets/*.js` | `VITE_BASE_PATH` missing, so assets are requested from the domain root | The Pages workflow sets it from the repo name; check it was not overridden |
| Page renders, every route except `/` 404s | `dist/404.html` missing | CI asserts it exists; check `scripts/spa-fallback.mjs` ran after `vite build` |
| Data never loads; console shows a CORS error; `/api/health` is fine | `CORS_ALLOWED_ORIGINS` does not match the Pages origin exactly | Origin only — `https://user.github.io`, no path, no trailing slash. A path is stripped with a warning in the startup log |
| First load hangs ~60s, then works | Render free instance was asleep | Expected. The app shows a "waking the API" banner after 2.5s |
| API returns 429 | Rate limiter — 120 requests/min per IP | Wait a minute |
| Render deploy fails on `USER $APP_UID` | Base image predates `.NET 8` | Pin `mcr.microsoft.com/dotnet/aspnet:8.0` |
| API starts then immediately exits | Port binding — `PORT` resolved at build time instead of runtime | The Dockerfile reads `$PORT` in its `ENTRYPOINT` shell; do not move it to `ENV` |
| Demo shows no data | `DEMO_MODE` not `true`, or a typo | The startup log prints `Demo mode is ON/OFF` and warns on an unparseable value |
| `git push` rejected mentioning `workflow` scope | Token lacks `workflow` | `gh auth refresh -h github.com -s workflow` |
| Pages deploy fails: "Pages not enabled" | Source not set to GitHub Actions | Settings → Pages → Source: GitHub Actions |

## Free-tier limits and actual headroom

| Resource | Limit | Realistic usage |
| --- | --- | --- |
| Render instance hours | 750/month | A sleeping service consumes none. Even continuous traffic on one service fits inside 750h. |
| Render RAM | 512 MB | The container idles well under this; SQLite adds almost nothing. |
| Render disk | **None on free** | By design — see [DEMO_DATA.md](DEMO_DATA.md). |
| GitHub Pages storage | 1 GB | The build is ~270 KB. |
| GitHub Pages bandwidth | 100 GB/month soft | ~80 KB gzipped per visit — roughly a million visits. |
| GitHub Actions | 2,000 min/month private, **unlimited public** | Public repo, so free. |

Nothing here is close to a limit. Render suspends rather than bills if one is
ever hit.
