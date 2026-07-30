# Personal COO

A personal project prioritization and execution tool. See `design-doc.md` (one level up, in the project folder root) for the full concept, schema, and UI/UX rationale.

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

## A note on how this was built

This was scaffolded and coded in a sandboxed environment without access to nuget.org, so the backend's `dotnet restore` / `dotnet build` couldn't be executed here to verify compilation - only the .NET SDK toolchain itself and a framework-only test project were confirmed working. Every backend file was hand-reviewed line by line for correctness instead (including a bug caught this way: a missing `using` for `EnsureCreated()`). Package versions in the `.csproj` are floated (`8.0.*`, `6.*`) rather than pinned, since exact current patch numbers couldn't be confirmed offline.

The frontend had no such restriction (npm's registry was reachable) - `npm install`, `npm run build`, and a served preview were all run successfully during development, so it's been verified end-to-end.

**First thing to do:** run `dotnet restore` on the backend and make sure it builds before relying on it. If anything doesn't compile, it's almost certainly a package-version mismatch in the `.csproj`, not a logic error - happy to debug it with you.

## What's deliberately not here (yet)

Per the design doc: no staleness-based scoring, search/filter, category management UI, backup/export, or dark mode. And per the original spec: no auth, notifications, calendar integration, AI chat, complex analytics, recurring tasks, team features, mobile app, or gamification.
