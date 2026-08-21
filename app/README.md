# Personal COO

A personal project prioritization and execution tool. See the [root README](../README.md) for the live demo and full documentation, and [design-doc.md](../design-doc.md) for the original concept, schema, and UI/UX rationale.

## Structure

```
backend/ProjectManager.Api/    ASP.NET Core 8 Web API + EF Core + SQLite
frontend/project-manager-web/  Vite + React + TypeScript
```

## Running it

### One-click (Windows)

Double-click **`start.bat`** in this folder. It opens two windows (backend + frontend), waits for each to boot, and opens the app in your browser at `http://localhost:5174`. On the very first run, the backend will `dotnet restore` and the frontend will `npm install` automatically - that first launch takes a minute or two; every launch after that is fast.

To stop the app, just close the two windows it opened (or Ctrl+C in each).

Requires the .NET 8 SDK and Node 18+ to already be installed - `start.bat` will tell you plainly if either is missing, with a download link.

### Manual (any OS)

**Backend** (requires the .NET 8 SDK):

```
cd backend/ProjectManager.Api
dotnet restore
dotnet run
```

The API listens on `http://localhost:5071` (Swagger UI at `/swagger` in Development). On first run it creates `projectmanager.db` (SQLite) next to the project and seeds the default categories (Home, Career, Finance, Personal, Relationships, Hobbies).

**Frontend** (requires Node 18+):

```
cd frontend/project-manager-web
npm install
npm run dev
```

Opens at `http://localhost:5174`. The dev server proxies `/api/*` requests to the backend at `localhost:5071` (see `vite.config.ts`), so both must be running.

## Build status

Both halves build clean and the test suite passes. Verified 2026-08-20:

- `dotnet build` - succeeded, 0 warnings, 0 errors
- `dotnet test` - 144 passed, 0 failed
- `npm run build` - succeeded
- `npx oxlint` - clean
- `npm audit` - 0 vulnerabilities

(An earlier version of this file warned that the backend had never been compiled,
because it was scaffolded in a sandbox without access to nuget.org. That is no
longer true, and CI now checks it on every push - see the root
[README](../README.md) and [docs/TESTING.md](../docs/TESTING.md).)

## What's deliberately not here (yet)

Per the design doc: no staleness-based scoring, search/filter, category management UI, backup/export, or dark mode. And per the original spec: no auth, notifications, calendar integration, AI chat, complex analytics, recurring tasks, team features, mobile app, or gamification.
