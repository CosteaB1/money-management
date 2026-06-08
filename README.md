# Money Management

**A self-hosted, privacy-first personal finance manager.** Track multi-currency accounts, transactions, budgets, and savings goals — import real bank statements, get automatic exchange-rate conversion, and watch your net worth in one place. Your financial data lives on **your** machine and never touches a third-party server.

> **Self-hosted, single-user, no third parties.** It runs locally against your own PostgreSQL database — no cloud account, no telemetry, no bank-API middleman. It ships without an authentication layer by design (built for a trusted personal machine/network), so don't expose it directly to the public internet.

## Highlights

- **Your data stays yours** — fully self-hosted; nothing leaves your machine.
- **Genuinely multi-currency** — every account holds its own ISO currency, with one reporting currency (MDL) for net-worth and dashboard aggregates.
- **Real statements, not manual entry** — import maib PDF statements; Coinpilot parses the rows, splits bank fees, auto-suggests categories and internal transfers, and flags duplicates before you commit.
- **Hands-off FX** — daily exchange rates fetched automatically from the Banca Națională a Moldovei (BNM), with manual overrides.
- **Built to last** — Clean Architecture .NET backend with ~950 automated tests across unit and integration suites.

## Features

- **Multi-currency accounts** — seven account types (Cash, CreditCard, BankCurrent, BankDeposit, Brokerage, CryptoExchange, P2PLending), each with its own ISO-4217 currency.
- **Transactions & transfers** — income/expense rows, internal transfers (kept out of income/expense totals), and balance adjustments for investment-style accounts.
- **Categories & auto-categorization** — hierarchical categories with DB-backed keyword rules that suggest a category on import.
- **Budgets** — per-category monthly limits with on-track / warning / over status.
- **Savings goals** — manual or account-linked, with pace stats and contribution history.
- **FX rates** — automatic daily fetch/backfill from BNM plus manual rates; MDL-equivalent net-worth aggregation.
- **PDF statement import** — parse maib statements into a reviewable preview before commit.
- **Dashboard, reports & CSV export**, plus light/dark theming.

## Tech stack

**Backend** — .NET 10, ASP.NET Core minimal APIs, Clean Architecture (Domain / Application / Infrastructure / Api + SharedKernel), EF Core (code-first) on PostgreSQL, Serilog, Scalar (OpenAPI UI), PdfPig.

**Frontend** (`web/`) — Next.js 15 (React 19, App Router), TypeScript, TanStack Query, Zustand, Tailwind CSS v4, Radix UI, Recharts, react-hook-form + Zod, Biome, Vitest, Playwright.

Architecture and design notes live in [WIKI.md](./WIKI.md) (product), [BACKEND.md](./BACKEND.md) (architecture/data model), and [FRONTEND.md](./FRONTEND.md) (UI/component stack).

## Getting started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 20+](https://nodejs.org/) (for the `web/` frontend)
- [Docker](https://www.docker.com/) (for PostgreSQL) — or a local PostgreSQL 17 instance

### 1. Clone

```bash
git clone https://github.com/CosteaB1/money-management.git
cd money-management
```

### 2. Start PostgreSQL

```bash
cp .env.example .env          # then edit .env and set a POSTGRES_PASSWORD
docker compose up -d          # starts postgres:17 on localhost:5432
```

(Or point the app at your own PostgreSQL instance instead.)

### 3. Configure the API connection string (user-secrets)

The connection string is **not** committed — supply it via [.NET user-secrets](https://learn.microsoft.com/aspnet/core/security/app-secrets):

```bash
cd src/MoneyManagement.Api
dotnet user-secrets set "ConnectionStrings:Default" "Host=localhost;Database=money_management;Username=postgres;Password=<your-password>"
# optional: an isolated DB for the `qa` launch profile
dotnet user-secrets set "ConnectionStrings:Test"    "Host=localhost;Database=money_management_test;Username=postgres;Password=<your-password>"
cd ../..
```

### 4. Run the API

```bash
dotnet run --project src/MoneyManagement.Api
```

The API listens on `http://localhost:5179`, applies EF migrations, and seeds reference data on first boot. Interactive API docs (Scalar) are at `http://localhost:5179/scalar/v1`.

### 5. Run the web app

```bash
cd web
cp .env.local.example .env.local   # sets NEXT_PUBLIC_API_BASE_URL=http://localhost:5179
npm install
npm run dev                        # http://localhost:3000
```

## Testing

**Backend** — from the repo root:

```bash
dotnet test
```

The unit suites (Domain, Application) need no infrastructure. The **integration** suites (Infrastructure, Api) require a running PostgreSQL and a `POSTGRES_PASSWORD` environment variable; they create and use a throwaway `money_management_inttest` database and refuse to run against any other:

```bash
# bash
POSTGRES_PASSWORD=<your-password> dotnet test
```
```powershell
# PowerShell
$env:POSTGRES_PASSWORD = "<your-password>"; dotnet test
```

**Frontend** — `cd web && npm test` (Vitest). End-to-end smoke tests use Playwright (`npm run test:e2e`).

## Project structure

```
src/
  MoneyManagement.Domain          # entities, value objects, domain events
  MoneyManagement.Application     # use cases (commands/queries), abstractions
  MoneyManagement.Infrastructure  # EF Core, PostgreSQL, parsers, external services
  MoneyManagement.Api             # minimal-API endpoints, composition root
  MoneyManagement.SharedKernel    # Result/Error, base types
tests/                            # xUnit suites (one per src project)
tools/MaibFixtureGenerator        # generates the synthetic bank-statement test fixtures
web/                              # Next.js frontend
```

The bank-statement test fixtures are **synthetic** — generated by `tools/MaibFixtureGenerator` (no real financial data).

## License

[MIT](./LICENSE) © 2026 Bivol Constantin
