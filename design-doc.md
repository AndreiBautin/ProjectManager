# Personal COO — Design Document

## 1. Refined Concept

Purpose: answer "what should I work on next, and what's the exact next step" for a personal backlog of one-off projects. It is a decision-support tool, not a task manager, calendar, or planning system. No scheduling, no capacity math — just an always-current priority order.

Hierarchy: **Life Backlog → Projects → Actions**. Projects are the unit of value; actions exist only to move a project forward. A project's "current next action" is always a single, specific, doable thing — not a vague milestone.

Two things change from the original spec, both to keep the data model honest:

- **Status is derived, not dual-tracked.** The spec lists `IsBlocked` as a field and also shows "Blocked" as a `Status` value — that's two sources of truth for the same fact. Fix: `Status` is computed as `Blocked` whenever `IsBlocked = true`, overriding whatever else it'd be. Manually-set statuses are just `Active`, `Paused` (Someday/Not Now), and `Completed`.
- **`Paused` status added.** Without it, "things I might do someday" either clutter the active ranked list or get deleted. Paused projects are excluded from the priority ranking and the recommendation engine, but stay visible on the Projects screen. This is the only addition to the original 5-screen, 3-entity scope.

## 2. Flaws / Gaps in the Original Spec (and fixes)

1. **Effort = 0 breaks the formula.** `(Impact × Urgency) / Effort` divides by zero. Fix: Effort is constrained to 1–10 in the UI (slider, no zero), and the engine floors it at 1 regardless.
2. **No distinct "unblock action" field was proposed, but the blocker logic needs one.** Adding a new field would bloat the schema. Fix: for a blocked project, its existing `CurrentNextAction` *is* the unblock action by definition — there's no separate concept needed. If a blocked project has no pending action defined yet, it's "stuck with no defined path" and the engine skips it (see algorithm below).
3. **Tie-breaking is undefined.** Two projects can land on the same score. Fix: sort by Priority Score desc → Urgency desc → CreatedDate asc (older items win ties, so nothing quietly rots at the bottom).
4. **Category as a rigid enum conflicts with "frictionless capture."** Fix: Category is a simple lookup table, not a hardcoded enum — the Add Project screen lets you pick existing or type a new one inline.
5. **No "last touched" signal.** A project can sit at a middling score forever and never surface. Not solving this in MVP (per "don't overcomplicate"), but the schema includes `UpdatedDate` now so a staleness bonus can be added later without a migration.
6. **Progress % — derived from completed actions.** Computed as the share of a project's actions that are Done (a Completed project always reads 100%), not a hand-managed field. The earlier manual approach was dropped because keeping a separate 0–100 slider in sync with the checklist was busywork; the checklist is the honest signal of how far along a project is.
7. **The spec conflates two different "next action" concepts** — each project's own next action, and the single global recommended action. The Command Center needs to surface both: a hero "recommended next action" (one, system-selected) plus each project's own next action in its card (informational, not necessarily the recommendation).

## 3. MVP Architecture

Single-user, local-only, no auth, no hosting concerns — optimized for low ceremony.

```
project-manager/
  backend/
    ProjectManager.Api/          (single ASP.NET Core Web API project, .NET 8)
      Controllers/               ProjectsController, ActionsController, CategoriesController, RecommendationController
      Models/                    Project.cs, Action.cs, Category.cs, enums
      Data/                      AppDbContext.cs, Migrations/
      Services/                  PriorityEngine.cs (scoring + recommendation logic)
      Program.cs
    projectmanager.db            (SQLite file, created on first run)
  frontend/
    (Vite + React + TypeScript)
    src/
      pages/                     CommandCenter.tsx, Projects.tsx, AddProject.tsx, Blocked.tsx, Completed.tsx
      components/                ProjectCard.tsx, RecommendationHero.tsx, ActionList.tsx, PriorityBadge.tsx, StatusPill.tsx
      api/                       thin fetch client for the API
```

Why one API project instead of Domain/Infra/Api layering: this is a personal tool for one user, not a team codebase — extra projects and interfaces add navigation overhead with no payoff. Folders inside one project give the same separation of concerns without the ceremony.

Priority Score is **computed, not stored** — it's derived from Impact/Urgency/Effort on every read. This guarantees it's never stale and removes a whole class of "forgot to recalculate" bugs.

Run locally: `dotnet run` for the API (e.g. `localhost:5080`), `npm run dev` for the frontend (Vite dev server proxies `/api` to the backend). No deployment target for MVP — it's a tool you run on your machine.

## 4. Database Schema

**Category**
| Field | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | string, unique | e.g. Home, Career, Finance, Personal, Relationships, Hobbies |

**Project**
| Field | Type | Notes |
|---|---|---|
| Id | int PK | |
| Name | string, required | |
| Description | string, nullable | |
| CategoryId | int FK, nullable | |
| Impact | int, 1–10, default 5 | |
| Urgency | int, 1–10, default 5 | |
| Effort | int, 1–10, default 5 | |
| Status | enum: Active, Blocked, Paused, Completed | `Blocked` is set automatically when IsBlocked=true; otherwise user sets Active/Paused/Completed |
| Progress | int, 0–100 | derived, not stored — % of actions Done (see PriorityEngine.ComputeProgress); Completed = 100 |
| IsBlocked | bool, default false | |
| BlockReason | string, nullable | required (in UI) when IsBlocked=true |
| Deadline | datetime, nullable | optional, project-level only (see Effective Urgency below) |
| CreatedDate | datetime | default now |
| UpdatedDate | datetime | bumped on any edit; not used by MVP scoring, reserved for future staleness logic |
| CompletedDate | datetime, nullable | set when Status→Completed |

**Action**
| Field | Type | Notes |
|---|---|---|
| Id | int PK | |
| ProjectId | int FK, cascade delete | |
| Description | string | |
| Status | enum: Pending, Done | |
| Order | int | ascending = execution sequence |
| AvailableFrom | datetime, nullable | null = doable anytime (ASAP). Set = not eligible to be worked on/recommended until this date (e.g. a scheduled appointment). Gates eligibility only — always visible either way. |
| CreatedDate | datetime | |
| CompletedDate | datetime, nullable | |

A project's **current next action** = its lowest-`Order` Action with `Status = Pending`. When that action is marked Done, the next-lowest Pending action automatically becomes "current" — no separate pointer field needed.

**Eligibility, and why there's no separate "Waiting" status.** An action is *eligible* if `AvailableFrom` is null or has already arrived (compared against local machine time, since this always runs on the person's own computer). This deliberately isn't a manual project status — a "Waiting" toggle would need to be remembered and un-remembered by hand, which fights the whole point. Instead it's derived automatically from the date on the current next action, every time it's read: if the next action is defined but not yet eligible, the project displays "Waiting until [date]" instead of "Moving Forward" or "Blocked," and flips back on its own the day it's eligible. It still ranks and appears in the active list — it's just skipped by the recommendation engine (see below) until then, the same way an action-less project already gets skipped.

Note this is not a scheduling or calendar system: there's no reminder, no notification, no day-by-day view. It's a single boolean gate (eligible / not yet) used only to keep the recommendation honest.

**Effective Urgency, and why Deadline doesn't touch the Urgency field.** Most backlog items genuinely have no deadline, and shouldn't need one — this field is optional and inert unless set. When a project *does* have a real external cutoff (a free trial expiring, a fee increase, a permit window), manually remembering to crank Urgency up as it approaches is exactly the kind of upkeep this app exists to avoid. So instead, `PriorityScore` is computed from an **effective urgency** derived at read time: with no Deadline, effective urgency is just the manual Urgency value, unchanged. With a Deadline, it ramps linearly from the manual value up to 10 over the last 14 days before it, pinned at 10 once due or overdue, and left untouched outside that window regardless of whether the deadline was originally 2 weeks or 6 months out. Crucially this only ever pushes urgency *up*, never down — a manually-set 10 is unaffected, and a task that feels minor day-to-day (clearing phone photos) still gets correctly forced up the list as a hard external deadline (a trial expiring) actually arrives. The stored `Urgency` value is never overwritten by this — it's a pure display/ranking-time calculation.

Deadlines deliberately live on the **project**, not the action — a deadline is about the whole project needing to land by a date, not one specific step. A specific action needing to happen by an earlier date than the project's own deadline (e.g., "gather 1099s by Jan 31" inside a "file taxes by Apr 15" project) is a real scenario, but was deliberately deferred: the workaround is to just set the project's Deadline to whichever real external date is currently binding, and move it forward once that sub-deadline passes. If that turns out to be frequent enough to feel like busywork, that's the signal to add a proper per-action deadline later.

Indexes: `Project.Status`, `Project.CategoryId`, `Action.ProjectId, Order`.

## 5. Recommendation Algorithm

```
candidates = Projects where Status in (Active, Blocked)   // excludes Paused, Completed
             order by PriorityScore desc, Urgency desc, CreatedDate asc

for project in candidates:
    nextAction = project.CurrentNextAction
    if nextAction is null or not eligible yet (AvailableFrom in the future):
        continue   // nothing definable, or nothing available yet — skip

    reason = project.Status == Blocked ? "Unblocks a high-priority project" : "Highest priority active project"
    return Recommend(project, nextAction, reason)

// nothing recommendable
return NoRecommendation("Add a next action to a blocked project, add a new project, or check back once a waiting item's date arrives.")
```

PriorityScore = `(Impact × EffectiveUrgency) / max(Effort, 1)`, rounded to nearest integer for display. EffectiveUrgency equals Urgency exactly when there's no Deadline (i.e. almost always) — see Effective Urgency above.

This is a straight implementation of the spec's blocker logic — it just reuses `CurrentNextAction` as the unblock action rather than inventing a new field. The eligibility check folds in the same way: a date-gated action is treated exactly like "no action defined" for recommendation purposes, just skip and move down the ranked list — the only difference is *why* it's not actionable today.

## 6. UI/UX Per Screen

**Command Center (`/`)** — the daily-open screen.
- Hero card at top: the single system-recommended action — project name, action text, one-line reason, a one-click "Mark Done" button.
- Below: ranked list of Active + Blocked projects as cards — rank, name, category tag, progress bar, current next action, status pill (green = Moving Forward, amber = Blocked-but-actionable, blue = Waiting until a date, red = Blocked-stuck, i.e. no defined next action). Priority score shown small/secondary — it drives order, it's not the headline.
- Click a card → opens that project in the Projects screen for editing.

**Projects** — full list/edit surface.
- Sortable table or card list (score, name, category), all non-Paused and non-Completed by default, with a toggle to include Paused.
- Click a project → detail panel: edit Impact/Urgency/Effort/Status/Blocked+Reason, manage its Actions (add, reorder, mark done); progress shows read-only, derived from the actions. "Mark Completed" button.

**Add Project** — fast capture, optimized for brain-dump.
- Fields: Name (required, only hard requirement), Category (dropdown + inline "add new"), Description (optional), Impact/Urgency/Effort sliders (default 5), Deadline (optional), Steps (optional), Blocked toggle + reason (optional).
- Steps default to a single blank input — the common case stays one-line frictionless. A collapsed "+ Add another step" link lets you stack up as many ordered steps as you already have in mind (e.g. call company A, call company B, compare quotes, schedule install) without leaving the form or reopening the project afterward. First step rides along with project creation; any further ones are queued right after via the same action-adding endpoint the detail page uses.
- "Save & Add Another" button to keep dumping items without returning to the list each time.

**Blocked** — dedicated visibility for stuck items.
- List of Blocked projects: name, block reason, current next action (= unblock action) if defined, priority score, and whether it's the current top recommendation.

**Completed** — history and momentum.
- Reverse-chronological list with CompletedDate. Simple counts only (this month / this quarter / all-time) — no charts, per the "no complex analytics" constraint.

## 7. Phased Implementation Plan

1. **Scaffold** — ASP.NET Core Web API + EF Core + SQLite, initial migration, seed default categories. Vite React TS app scaffolded separately.
2. **Backend core** — CRUD endpoints for Category/Project/Action; PriorityEngine service; `/api/recommendation` endpoint; Project list endpoint returns computed PriorityScore and CurrentNextAction inline.
3. **Command Center** — wire the hero recommendation + ranked list to the API.
4. **Projects screen** — list, detail edit, action management (add/reorder/complete).
5. **Add Project** — fast-capture form with save-and-add-another.
6. **Blocked + Completed screens.**
7. **Polish** — empty states, color coding, one-click "mark done" from Command Center, basic styling pass.

Explicitly deferred (not MVP): staleness-based scoring, search/filter, category management UI, backup/export, dark mode, any of the originally-excluded items (auth, notifications, calendar, AI chat, analytics, recurring tasks, team features, mobile, gamification).
