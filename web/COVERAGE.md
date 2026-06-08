# Frontend test coverage

The `web/` test suite (Vitest + Testing Library + MSW) enforces **100 % line/statement
coverage** of hand-written application code via the `@vitest/coverage-v8` provider.

```
npm test            # vitest run
npx vitest run --coverage
```

The gate lives in `web/vitest.config.ts`:

```ts
coverage: {
  provider: 'v8',
  reporter: ['text', 'html'],
  include: ['app/**', 'src/**'],
  exclude: [ /* see below */ ],
  thresholds: { lines: 100 },   // line-only gate
}
```

- **Lines / statements: 100 %** on the included set — the build fails below this.
- **Branches / functions are informational** (currently ~92 % / ~91 %). We deliberately
  do not chase every branch arm; the goal is that every hand-written executable *line*
  runs under test.

## What's measured

Everything under `app/**` and `src/**` — pages, layouts, providers, components, hooks
(`src/lib/api/*`), stores, and utils.

## Exclusions (with justification)

| Path | Why excluded |
|------|--------------|
| `src/types/api.ts` | Generated from the backend OpenAPI schema (`npm run gen:api`). Type-only — no executable lines. |
| `**/*.d.ts` | Ambient type declarations — no runtime code. |
| `**/*.config.*`, `**/postcss.*`, `**/next-env.d.ts` | Build/tooling config, not application logic. |
| `e2e/**` | Playwright end-to-end specs — run by `npm run test:e2e`, not Vitest. |
| `tests/**` | The Vitest suite itself plus its MSW handlers, fixtures, and `setup.ts`. |
| `src/lib/mocks/**` | MSW server bootstrap — test-only infrastructure, exercised indirectly. |

No real component, hook, page, or util is excluded.

## Unreachable defensive lines (`/* v8 ignore */`)

A handful of statements are genuinely unreachable through the UI or the real runtime,
so they're wrapped in `/* v8 ignore start/stop */` (or `next`) with an inline reason.
Each is a defensive guard or an exhaustive-switch safety net, not skipped real logic:

| File | Lines | Reason |
|------|-------|--------|
| `accounts/create-account-dialog.tsx` | currency `errors.currency` block | The currency `<Select>` only emits valid 3-letter ISO codes, so the Zod regex never fails. |
| `categories/create-category-dialog.tsx` | `errors.flow` + `errors.color` blocks | The flow `<Select>` only emits valid enum values; the native colour input only yields valid hex. |
| `settings/create-fx-rate-dialog.tsx` | `errors.fromCurrency` block | The From `<Select>` only emits valid 3-letter codes. |
| `transactions/add-transaction-dialog.tsx` | `errors.counterAccountId` block | `counterAccountId` is an optional uuid set only from a `<Select>`; the backend error mapping has no counter-account arm. |
| `transactions/create-transfer-dialog.tsx` | FX-prefill `.catch` reset line | Timing-flaky in jsdom: the effect cleanup usually flips `cancelled` before the rejection microtask runs. |
| `transactions/import-preview.tsx` | reducer `init` case, `default` case, commit empty-guard | `init` is never dispatched (lazy `initial` seeds state); `default` is an exhaustive-switch net; the commit button is disabled at count 0 so the empty-guard can't fire via the UI. |
| `transactions/import-upload.tsx` | `handleSubmit` account/file guards | The Upload button is disabled until both an account and a file are present, so the guards can't fire via the UI. |

Everything else — including chart tooltip components and helpers (extracted with an
`export` so they can be unit-tested directly, since Recharts tooltips don't render in
jsdom's layout-less DOM) — is exercised by real rendering/interaction tests.
