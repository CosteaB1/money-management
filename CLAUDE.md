# Project Conventions for AI Assistants

This file documents conventions AI coding assistants (Claude Code, Cursor, Codex, etc.) should follow when working in this repo. Human-facing docs live in [WIKI.md](./WIKI.md), [BACKEND.md](./BACKEND.md), [FRONTEND.md](./FRONTEND.md).

## Delegate to specialized agents

For non-trivial code work, route to the right agent rather than hand-editing files inline:

- **Backend (`src/MoneyManagement.*`, `tests/MoneyManagement.*.Tests`)** → `c-sharp-pro` agent.
- **Frontend (`web/`)** → `frontend-developer` agent.

The main thread owns: planning, asking clarifying questions, updating the `WIKI.md` / `BACKEND.md` / `FRONTEND.md` docs, verifying agent reports against the actual diff, and conversation with the user.

When the work spans both stacks, fire both agents in parallel from a single message.

Reserve inline edits for trivial / single-file tweaks, doc updates, and synthesis. Never delegate understanding — brief each agent with concrete files, line numbers, and the contract they need to honor.

## Update the docs with every meaningful change

Any change that affects behavior or architecture must be reflected in `WIKI.md` (product-level), `BACKEND.md` (architecture/data model), and/or `FRONTEND.md` (UI/component stack), in the same change. The docs lead the code, not trail it.

## Commits and pushes — only on explicit request

Never run `git commit` or `git push` unless the user explicitly asks for it in that turn. Finishing a change, being mid-task, or "I'm testing now" are **not** authorization. One explicit "commit"/"push" authorizes that single action — it does not carry forward to later changes. When work is ready, summarize it, offer to commit, and wait. If the user asks to commit but not push (or vice versa), do only what was asked.

## Never write to the real database without explicit approval

The real database is `money_management`; the disposable QA/test database is `money_management_test` (the `qa` launch profile). Never run a write — `UPDATE` / `INSERT` / `DELETE` / DDL or any other data-mutating statement — against **`money_management`** unless the user explicitly approves that specific change in that turn. This holds **even when the change matches a goal the user stated** (e.g. "fix this account's type"): surface the exact statement, offer the SQL or a UI path, and wait for an explicit go-ahead.

- **Read-only `SELECT` against the real DB is fine** (use the read-only `mcp__postgres-real__query` or a `SELECT` via `psql`).
- **The test DB (`money_management_test`) is fine to write to freely** — that's what it's for.
- Treat real financial data as hard-to-reverse: prefer giving the user the statement to run themselves, or doing it only after explicit approval.

## Pre-commit smoke test — MANDATORY before every commit

Before committing **any** change (feature or bugfix), run a Playwright smoke test that drives the touched flow through the real UI → backend → database, and confirm nothing regressed. A green automated suite (xUnit/Vitest) is necessary but **not** sufficient — this catches wiring/integration breaks the unit tests can't. The full protocol, environment setup, and the living A→Z test matrix live in [QA.md](./QA.md).

Minimum per commit:
1. App running on the **dedicated QA ports** — API on **`:5180`** (the **`qa`** profile, against the **test** DB `money_management_test`) and web on **`:3001`** (`NEXT_PUBLIC_API_BASE_URL=http://localhost:5180 … -p 3001`), kept separate from the real dev app on `:5179`/`:3000`. The **web instance** (not the backend port) decides which API/DB is hit, so a second backend alone is not isolation: you MUST run the `:3001` QA web and **drive only `:3001` — never `:3000`/`:5179`**. Guards before any mutating action: (a) confirm the qa API bound `:5180` and its log says `Using database 'money_management_test'`; (b) check `browser_network_requests` shows API calls going to `:5180`. A failed start then fails loudly on `:5180` instead of silently hitting the real app. See QA.md > Setup + "Real vs. test database".
2. Drive the affected flow(s) via the Playwright MCP; after each mutating action, cross-check the value in Postgres (UI ↔ DB must agree). Use `mcp__postgres-test__query` for the test DB (`mcp__postgres-real__query` is the read-only window into the real DB); native `psql` works too (see QA.md).
3. Check `browser_console_messages` — no new errors/warnings.
4. Run QA.md's "core regression path" regardless of what changed.
5. Update the relevant row(s) in QA.md's matrix (date + result), then commit only when green.

## EF Core migrations — MANDATORY CLI workflow

The `c-sharp-pro` agent (and anyone touching migrations) MUST use the EF Core CLI to generate migrations. **Hand-writing the `.cs` and `.Designer.cs` files is forbidden** — it produces drift the snapshot check can't catch (default-value bugs, provider-specific SQL nuances, untested `Down`).

Workflow:

1. **Ask the user to stop the running API process first.** Visual Studio "Stop debugging" or Ctrl+C on `dotnet run`. The EF tooling has to load `MoneyManagement.Infrastructure.dll`, and a running API holds an exclusive file lock on it that blocks the CLI.
2. **If the new migration adds a non-nullable enum-as-string column to an existing table, add `HasDefaultValue(<EnumMember>)` in the EF configuration before running the CLI.** Otherwise EF generates `defaultValue: ""` which breaks the enum read path on pre-existing rows.
3. Run:
   ```
   dotnet ef migrations add <Name> -p src/MoneyManagement.Infrastructure -s src/MoneyManagement.Api -o Database/Migrations
   ```
   (or `Add-Migration <Name> -Project MoneyManagement.Infrastructure -StartupProject MoneyManagement.Api` from VS Package Manager Console).
4. **Convert the generated `.cs` body from block-scoped to file-scoped namespace** (the repo enforces `IDE0161` as a build error; auto-generated `.Designer.cs` is exempt).
5. Run `dotnet ef migrations has-pending-model-changes` — must report no drift.

If the CLI fails for any reason other than the DLL lock, stop and ask the user — do not fall back to hand-writing.

## Project context

- Personal finance app — single user, self-hosted. See `WIKI.md` for the product brief.
- Clean Architecture .NET backend + Next.js 15 frontend in `web/`. See `BACKEND.md` and `FRONTEND.md`.
- Account-model expansion is currently mid-flight; see the "Account Model Roadmap" section in `WIKI.md` for which phase is in progress.
