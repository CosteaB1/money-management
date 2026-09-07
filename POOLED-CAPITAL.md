# Pooled capital — tracking outside investors in the Binance account

> **Status: PROPOSAL. Nothing here is implemented.** This document exists to be argued with and
> decided on. It records the problem, four modelling decisions only the user can make, the options
> considered, a recommendation, and a phased plan. Once the decisions in [§8](#8-decisions-needed)
> are made, the surviving content moves into `WIKI.md` / `BACKEND.md` / `FRONTEND.md` / `QA.md` per
> [§7](#7-documentation-plan) and this file is deleted.
>
> Written 2026-09-06. All figures verified against the repo and a **read-only** `SELECT` on the real
> `money_management` database on that date.
>
> **Update, same day — Phase 1 has SHIPPED and three decisions are confirmed.** Net worth now folds in
> outstanding loans via a server-side `IExternalClaimSource` seam (`GET /dashboard/net-worth`, three
> dashboard tiles, as-of trend deduction). See BACKEND.md > "External claims and net worth". The user
> has confirmed: **(1)** the friends bear losses pro-rata and a negative month is not covered from the
> user's own money; **(2)** payouts go out as a Binance transfer of the profit; **(3)** the user takes
> their own profit out to Bybit, so the pool is Binance-only and owner withdrawals are redemptions.
> Unit/NAV accounting is therefore the agreed direction. Phase 2 is being re-scoped against the seam
> that actually shipped — note it expresses a **declining** obligation, not a **floating** stake.

---

## The situation

The user runs a small crypto trading operation out of a Binance account. Until September 2026, every
dollar in it was theirs. This month two friends each put in **USD 1,000**. The user will pay each
friend a share of the profit **monthly**; a losing month pays nothing, and the friends know that.

The money physically sits in **one** exchange account. It cannot be split into separate real
accounts, and its value is only known when the user types in a snapshot.

---

## 1. What actually broke

- **Nothing in the app knows the USD 2,000 isn't the user's.** The moment it lands, `Binanance`
  reads ≈ USD 3,050 and both net-worth surfaces count all of it. At USD/MDL 17.2682 that is
  **MDL 34,536 of other people's money booked as personal wealth.**

- **Net worth is computed twice, independently, and neither reads any liability.** The headline is a
  browser-side reduce (`web/src/components/dashboard/net-worth-card.tsx:12`); the trend is a
  separate server-side as-of sum (`GetNetWorthTrendQueryHandler.cs:122`). There is no
  `GET /dashboard/net-worth` endpoint at all. Fix one and the dashboard contradicts itself.

- **It was already wrong before the friends arrived.** Five non-archived `Received` loans —
  EUR 6,000 + MDL 14,152.01, **all five account-linked, none repaid** — are invisible to net worth.
  That is a deliberate v1 cut (`WIKI.md:121`), but it is now the *larger* of the two gaps:

  | | MDL |
  |---|---:|
  | Gross non-archived assets | 183,864 |
  | Loans owed (invisible today) | −134,380 |
  | **True net worth** | **49,484** |
  | Friends' capital, once recorded (also invisible) | −34,536 |

  The loans gap is **3.9× larger** than the investor gap, and closes without a migration.

- **The account detail page will start lying too.** `GetAccountDetailQueryHandler.cs:126-207` buckets
  every `IsTransfer + Income` row into *Contributions* and every `IsAdjustment` row into *Net P&L*.
  Binanance's all-time Contributions goes from USD 842.89 to USD 2,842.89, and its Net P&L
  (USD 1,007.11 today) becomes **the pool's** return presented as the user's. *(The card renders
  these MDL-converted; the USD figures are the native equivalents.)* The **Balance over time**
  report has the same problem — it deliberately includes transfers and adjustments (`WIKI.md:118`),
  so it will plot pool-gross.

- **There is a live data-entry landmine.** `POST /accounts/{id}/balance-changes` with kind
  `Adjustment` computes `delta = typed − derivedBalance`
  (`Features/Transactions/AdjustBalance/AdjustBalanceCommandHandler.cs:169`). **Snapshot the pool to
  3,050 before recording the friends' 2,000 and the whole 2,000 books as pure trading profit** — in
  a row indistinguishable from a real one. Worse, that derived balance sums *every* row on the
  account with **no date filter** (`:161-167`), so back-dating any movement after a snapshot
  silently corrupts the next snapshot's delta.

---

## 2. The four real decisions

These matter more than any code. Get them wrong and every implementation pays the wrong people
correctly.

### (i) Do the friends bear losses?

Either their stake falls with the pool, or the user absorbs the drawdown and their 1,000 is
guaranteed.

**Recommended: shared, pro-rata.** Three reasons:

1. *"If a month is negative nobody gets paid"* is only coherent if they are exposed. If the
   principal is actually guaranteed, this whole design is wrong and their money is a **Loan** with a
   profit-share kicker — model it on the existing Loans slice instead.
2. A −20% month on a 3,050 pool costs **USD 600 out of the user's own USD 1,050**. That is not
   fundable.
3. "Owner absorbs" is an unbounded ratchet: losses skip attribution, gains split pro-rata. A flat
   round-trip (−500 then +500 on a 3,050 pool) leaves the user **USD 392 poorer** and each friend
   USD 196 richer, with nothing stopping the owner's residual from going negative.

**This is the blocking decision.** Everything below assumes yes.

### (ii) High-water mark?

Read literally, *"pay their share of the profit monthly; a negative month pays nothing"* means
**monthly reset** — every green month pays, regardless of history. The alternative is a
**high-water mark**: after a drawdown, profit resumes only once the stake is back above where it was
when they were last paid.

Take a repeating −15% / +10% cycle — a pool that goes nowhere:

| | Monthly reset | High-water mark |
|---|---|---|
| Friend's stake after 6 cycles | 1,000 → **443.71** | ≈ 1,000, flat |
| Cash handed over | 85.00 + 72.25 + 61.41 + 52.20 + 44.37 + … | 0 |

Monthly reset pays profit on the *same recovered dollars* every cycle. The user funds it while the
friend's capital base ratchets down — neither party intended that.

**Recommended: high-water mark, on.** And it needs **no stored field**: if every positive month
distributes in full, the payout sweeps the stake back to exactly the capital base, so

```
payable = max(0, stakeValue − capitalBase)
```

*is* the high-water mark, permanently, with no reset logic and no blending on top-ups.

**The cost, which must be stated to the friends in writing:** a positive month can pay zero.
October −15% then November +10% leaves each friend at 935 against a 1,000 base — nobody is paid in
November even though November was green.

### (iii) How precise does the share split have to be?

| Approach | User moves own money mid-month? | Friends join on different dates? | Storage |
|---|---|---|---|
| **Fixed %** (33/33/34) | ✗ breaks the day 500 moves to Bybit | ✗ | a number in your head |
| **Time-weighted** (Modified Dietz) | ~ with a day-count convention | ✗ | dates + weights |
| **Units / NAV** | ✓ exactly, no special case | ✓ exactly | a register of unit events |

**Why not time-weighting.** Properly day-weighted Dietz gets the *simple* case right. It fails
because it assumes returns accrue **uniformly across the month**, which is false for a crypto book —
and the only inputs available are hand-typed snapshots, so the day-weights are applied to a return
the app never actually observed day by day. The moment a snapshot is missing at a cash-flow date,
the attribution is a guess dressed as arithmetic. *(A capital-weighted split with no day-weighting —
the naive version — is worse still: in the September example below it over-pays the later entrant
by USD 21 on 1,000 of capital, every month, forever.)*

**Recommended: units, with a mandatory pre-money valuation at every capital event.** The property
that makes it work is that a subscription or redemption **at NAV** leaves NAV unchanged:

```
(V − X) / (U − X/nav) = nav
```

So Bybit transfers, top-ups, exits and monthly payouts move nobody else's per-unit value by a cent.
No weights, no day counts, no effective-date policy.

**The price, stated honestly:** the user must type the exchange's real total *immediately before* any
money crosses the boundary, and the app should make that a hard block. The account shows **three
boundary events in 21 days** (08-13 in from Bybit, 08-25 P2P out, 09-02 500 out to Bybit), so this
is roughly **2–4 forced valuations per month**, not per year. Either accept that, or stop cycling
capital between exchanges while the pool is live.

### (iv) Is the pool Binance only, or Binance + Bybit?

*This is a real decision, not an appendix item.* In the worked example below, parking USD 400 on
Bybit moves the owner from 35.76% → 26.50% of the pool. If the friends believe they bought a share
of **the operation** rather than **the Binance account**, then every Bybit transfer is an unagreed
dilution of the owner and a windfall to them.

**Recommended: Binance only, enforced by policy and stated in the message to the friends.** A
multi-account pool is a materially harder problem and is vastly cheaper to decide now than to
retrofit once live unit events exist.

### Not modelled: tax

The app models no tax anywhere, and this document does not change that. Two things still need a
decision before the first payout: whether distributions are **gross or net** of any withholding, and
whether the user's own realised gain creates an obligation. If withholding is ever applied, the
distribution splits into two legs and the `Units × PricePerUnit == Amount` invariant breaks — so
decide it before the model is built, not after. **This is not tax advice.**

---

## 3. The options

Ordered lightest to heaviest. Five approaches were designed independently and reviewed by an
accounting judge and a codebase-fit judge; two collapsed into one.

### 3.1 The Mirror Envelope — zero code, tonight

One negative-balance `CreditCard` account per friend ("Investor · Andrei", USD, opening 0.00). Their
capital arrives as a two-leg transfer from the envelope into Binanance, driving the envelope to
−1,000. Net worth already sums `balanceMdl` with no sign guard and no type filter, so the two cancel.

**Right:** fixes *both* net-worth paths with one act, because the fix is data. History isn't rewritten.
And it exploits a genuinely enforced invariant — `CreditCard` is absent from
`AdjustBalanceCommandHandler.EligibleTypes`, so the app physically refuses to snapshot a liability.

**Wrong (3/10 on accounting):** the split is a single ratio read off two mutable screen numbers, so
it cannot price mid-period arrivals at all. Catastrophic case: pay a friend from the MAIB card
instead of Binance and the routine still writes the Expense on Binanance, creating a permanent gap
that regenerates as phantom profit every month and converges on **paying ~2.7× what is owed**. No
test anywhere pins "a negative account balance reduces net worth", and the promised loans fold-in
collides with it.

**Verdict: not a destination.** Its one keeper is the account-type-as-enforced-invariant trick.

### 3.2 The Pool Book — capture the perishable facts, compute in a spreadsheet

Spend 90 minutes recording the facts that expire (the pre-arrival NAV, the two capital legs, a
month-end snapshot ritual) using only existing UI. Do the split in a sheet. Buy an honest net-worth
number later for one evening of code.

**Right (8/10 on codebase fit — the highest of the five):** its central insight is correct and
important — *attribution can be added retroactively forever; a valuation you failed to record on the
day money crossed the boundary is gone permanently.* It also spots that a crude
"liability = capital base" approximation **is** the high-water mark, and errs conservative while the
friends are under water.

**Wrong (4/10 on accounting):** it captures the entry valuation and then throws it away — the
arithmetic it recommends is the time-weighting rejected in §2(iii), and no per-period valuation is
persisted, so the basis is unreconstructable.

**Verdict: its Phase 0 capture discipline is the single best short-term action available** and is
lifted wholesale into the plan below.

### 3.3 The Signed Stake Ledger — one child table, owner as residual

Each participant gets one signed, dated ledger of stake deltas. Stake at any date is a `SUM`. The
owner is never a row — the owner's stake is the residual `poolValue − Σ stakes`, which is right by
construction and gives rounding pennies a home.

**Right:** two of the best structural ideas in the review — **owner-as-residual** (which makes "the
user moved their own money mid-period" free, with zero user input) and **`payable = max(0, stake −
capitalBase)`** (the free high-water mark). Frozen closed periods with a live open period is the
correct immutability boundary.

**Wrong (5/10 on accounting):** every defect traces to one root — **no valuation is forced at a
capital event.** Per-snapshot attribution is exact only if a snapshot exists at every cash flow, and
the real account does not have that (July 2026 had one adjustment row, a 24-day gap). Add: the tie
rule is decided by data-entry order (a USD 61 swing on which dialog you open first), and the
net-worth deduction as specified sums only ledger rows, so unclosed accrued profit isn't counted —
the dashboard and the pool page disagree mid-month.

**Verdict:** force a snapshot at every flow and it converges on §3.4 anyway, with more bookkeeping.

### 3.4 The Unit Ledger — run it as an actual fund

Everyone including the user holds *units*. `NAV = poolValue ÷ unitsOutstanding`, where pool value is
the account's existing derived balance and the existing `IsAdjustment` snapshots are the re-pricing
mechanism. Contributions issue units at the prevailing NAV; withdrawals and payouts redeem units at
the prevailing NAV.

**Right (6/10 on accounting — the highest of the five):** the engine is correct. NAV invariance was
verified against the real numbers. Paying profit by redeeming at NAV **resets the capital base for
free** — both friends end holding ≈ 909 units worth exactly 1,000 despite entering at different
NAVs, with no line of code saying "reset their base". Units are denominated in USD with
`PricePerUnit` frozen on the row, so a retroactive FX correction can never rewrite who owns what.
And it writes the mark **before** pricing units inside one `SaveChangesAsync`, structurally
defeating the ordering trap rather than warning about it.

**Wrong:** missing guards, not bad arithmetic. No overdraw invariant (`LoanErrors.PaymentExceeds
Outstanding` has no equivalent). Performance-fee machinery that ships unused and caused two of the
review's four fatal findings. And "units" is alien vocabulary for a hobby app — the UI must say
"share" and show percentages.

**Verdict: the right engine.** Trim the surface.

---

## 4. Recommendation

> **Build the Unit Ledger's engine, at the Stake Ledger's size, behind a net-worth fix that ships
> first, on top of the Pool Book's capture discipline.**

The reasoning, specifically for this user: **you are the only person who moves money mid-month, and
you demonstrably do it** (500 to Bybit on 09-02, 300 P2P out on 08-25, 192.89 in from Bybit on
08-13). Every non-unit model needs a special case for that, and every one of them got it wrong in
review. Units make it a division. You also already produce the input units need — you type a
snapshot ≈1.7×/month, and each one *is* a NAV strike.

Sequencing matters as much as the model: **the loans gap is 3.9× larger than the investor gap**
(MDL 134,380 vs 34,536), closes with a ~40-line adapter over unchanged tables, and needs no
migration and no backup bump. Best value-per-risk work available; it should ship first, independent
of anything about the friends.

**Explicitly NOT building:** performance or management fees, a loss-policy flag, a NAV history
chart, a `/pools` list page, or multi-account pools. Each was a source of at least one fatal finding,
and none serves two friends and USD 2,000.

---

## 5. Phased plan

Three phases, each independently shippable and green on its own.

Per `CLAUDE.md`, backend work goes to the **`c-sharp-pro`** agent and frontend to
**`frontend-developer`**, fired in parallel from one message where a phase spans both. Every phase
ends with a Playwright smoke test on the QA ports (`:5180` / `:3001`, test DB) and its own QA.md
matrix rows before any commit. Doc updates land **in the same commit as the code they describe** —
split per phase, not batched at the end.

### Phase 0 — capture what expires (this week, zero code, ~90 min, **user-performed**)

> 🔴 **Do this before anything else.** Attribution can be reconstructed forever. A valuation you did
> not record on the day money crossed the boundary is **gone permanently**.
>
> ⚠️ These are writes to the **real** database, performed **by the user in the UI**. No agent
> executes them. Where SQL is needed it will be surfaced for the user to run.

1. **Take a fresh backup export** (Settings → Data → Export). It is the undo button — *but note it
   becomes unrestorable at Phase 2's v5→v6 bump, which has no upgrade path. Keep a pre-v6 build
   checked out if you want it to stay usable.*

2. **Establish the pre-money value — order matters.**
   - **If the friends' money has NOT landed on Binance yet:** read the true total now and record it
     as a Balance adjustment dated today. The app says 1,050.00, but the last real valuation was the
     +146.00 on **2026-08-27** — ten days stale in an account returning ~11%/month.
   - **If it HAS already landed:** do **not** type the current total. Go find the balance from the
     day *before* each arrival in Binance's history first, before it rolls off. Typing the
     post-arrival total books USD 2,000 of principal as permanent, indistinguishable profit.

3. **Create two categories, `Both` flow: `Investor capital` and `Investor payout`.** These are the
   machine-readable handle — `Investor capital` rows sum to the liability in Phase 1.

   > ⚠️ **Do not create them through Settings → Categories.** `CategorySeeder` backfills **by id
   > only** (`CategorySeeder.cs:44-67`) and there is **no unique index on category name**
   > (`CategoryConfiguration.cs:34` is a plain `HasIndex`). A hand-created row gets a random id, so
   > Phase 2's seeder would silently insert a **duplicate**, and the Phase-1 liability would key off
   > one id while the history carries the other — reading **zero**. Create them with the
   > deterministic ids Phase 2 will use, via a one-off `INSERT` run by the user, or resolve by name
   > instead of id. The exact statement will be surfaced for approval.

4. **Record each friend's USD 1,000 as a transfer-flagged Income row** on Binanance, category
   `Investor capital`, description carrying the pre-money pool value and the real arrival date.
   **Never as an Adjustment** — that books their principal as your profit.

5. **Set a calendar reminder for the 1st.** *A missed month-end snapshot is recoverable under the
   unit model — the next payout is simply larger. What is unrecoverable is a **capital event without
   a valuation**.* Treat any money crossing the boundary as the hard trigger.

6. **Send the friends one message.** It must cover loss-bearing, the high-water mark, exit terms and
   pool scope — the four things most likely to be disputed later. Draft:

   > *"Quick note so we're all on the same page. Your 1,000 buys a share of the Binance pool — not
   > the whole operation, and not a fixed return. Each month I value the pool and pay out your share
   > of any profit. A losing month means no payout, and it also means your stake genuinely goes
   > down; I don't cover it. Profit payments resume only once your stake is back above 1,000, so a
   > green month after a bad one can still pay zero. You can exit any time: you get your share
   > valued on the day you leave, which may be less than 1,000. No lock-up, no notice period."*

7. **Write the four decisions into `WIKI.md` with today's date**, including the message text.

**Ships:** an honest, recorded arrangement. Net worth is still wrong.

### ~~Phase 1 — make net worth true~~ ✅ SHIPPED 2026-09-06

**Prerequisite (~2–3 h, no behaviour change, ship separately — with its own QA row and smoke test):**
extract `AccountBalances.Live(anchor, income, expense)` across the **ten** duplicated sites —
`GetAccountsQueryHandler.cs:75`, `GetAccountDetailQueryHandler.cs:210`,
`GetNetWorthTrendQueryHandler.cs:122`, `GetBalanceOverTimeQueryHandler.cs:88`,
`GetGoalsQueryHandler.cs:102`, `GetGoalDetailQueryHandler.cs:173/:278/:337`,
`ParseStatementCommandHandler.cs:81`, and **`AdjustBalanceCommandHandler.cs:161-167`** — and lift
`ConvertInMemory` (`GetAccountsQueryHandler.cs:104-149`) into a shared `FxSnapshot`. The tenth site
is the one carrying the date-blind snapshot bug, so extracting it *is* the fix. Phase 2 needs
balance-as-of in ~10 more places; extract now or maintain 19 copies of the definition of "balance".

Then:

- `Abstractions/NetWorth/{ExternalClaim, IExternalClaimSource}.cs` — a dated, natively-denominated
  signed claim; `Side ∈ {ReducesNetWorth, IncreasesNetWorth}`.
- `Features/Loans/LoanExternalClaimSource.cs` — ~40 lines over unchanged tables. Closes
  `WIKI.md:121`.
  > **Must key off `disbursement_transaction_id IS NOT NULL`.** An unlinked `Given` loan never
  > reduced any balance, so adding it back inflates net worth; an unlinked `Received` loan never
  > raised one. All five current loans *are* linked, so today's figure is right — but `CreateLoan`
  > makes the account optional and `WIKI.md:126` actively recommends the unlinked path. Also decide
  > what an **archived-but-unsettled** loan means here: archiving is allowed with outstanding > 0
  > (QA row 57), so filtering archived loans out would make a real liability vanish.
- `Features/Pools/PooledCapitalClaimSource.cs` — **interim**, ~30 lines: the signed
  `Investor capital` category sum on pooled accounts. Replaced in Phase 2; the interface means
  neither UI surface changes.
- `Features/Dashboard/GetNetWorth/` → `NetWorthDto(GrossAssetsMdl, ExternalLiabilitiesMdl,
  ExternalAssetsMdl, NetWorthMdl, Claims[], MissingFxRate)`. One route on `DashboardEndpoints`.
- One extra Scrutor scan clause in `Application/DependencyInjection.cs:16-29`.
- `GetNetWorthTrendQueryHandler` — deduct as-of, between the account loop's close (`:138`) and point
  construction (`:146`). Use a `GetHistoryAsync()` that **materializes once**; the handler is
  already O(24 × accounts × all transactions) with an awaited FX call per account per point.
- `net-worth-card.tsx` — **delete** the reduce at `:12` so nothing is left to copy; render
  gross → deductions → net using the `border-t pt-3` block from `performance-card.tsx:121-141`. The
  caption at `:40-42` becomes false and must be rewritten.
- New "Not my money" dashboard tile (`md:col-span-4`), on the `loans-summary.tsx:31-116` pattern.

**Breakage to budget for:** `NetWorthTrendFilterDisciplineTests.cs:48-49` won't compile once the
handler's constructor changes; `DashboardEndpointTests` needs the new route; and
`net-worth-card.test.tsx`, `net-worth-card-states.test.tsx`, `net-worth-trend-chart.test.tsx` and
`pages.test.tsx` break at once because MSW runs `onUnhandledRequest: 'error'`
(`web/tests/setup.ts:86`).

> ⚠️ **Run `scripts/check-coverage.ps1` before starting.** `CoverageReport/Summary.txt` is dated
> 2026-06-02 — *before* the Loans slice landed — and `LoanEndpoints.cs` (147 lines) has no
> `Api.Tests` file. The 99% gate may already be red; don't discover that mid-slice.

**Ships:** the headline goes from ≈ MDL 183,864 to ≈ MDL 49,484, both surfaces agree, and the loans
gap closes. **That is a 134k drop and the trend chart reshapes retroactively** — correctly, but it
will be a shock; expect it.

### Phase 2 — the pool slice · **RE-SCOPED 2026-09-06** (15–18 focused days)

> Supersedes the original Phase 2. A review of three competing approaches, each stress-tested by an
> accounting lens and a codebase-fit lens, overturned this section's original assumption that the
> pool would ride the `IExternalClaimSource` seam.

#### The floating-claim problem

`ExternalClaim.OutstandingAsOf(d)` is `Amount − Σ settlements ≤ d` — a fixed principal that only
**declines**, and both consumers discard a non-positive result. A pool stake is
`units(d) × navPerUnit(d)`: it **rises** when Binance rises. Nothing in that record can rise.

Worse, the input it would need — the account balance — is something the handler computed one loop
earlier, and `GetHistoryAsync(CancellationToken)` has no channel to receive it. A claim-shaped pool
source would have to re-read the whole transaction table. *A downstream abstraction cannot be a
function of its upstream.* That is the tell that the claim seam is the wrong home, not that its
arithmetic is too narrow.

#### The resolution — owner fraction at the asset leg

The friends' money is **never counted as the user's asset**, so nothing has to be subtracted back
out. A second, parallel seam supplies a per-account **owner fraction** as a dated step function, and
both dashboard handlers multiply it into the balance they already compute.

```csharp
public sealed record AccountOwnership(Guid AccountId, IReadOnlyList<OwnedFractionPoint> Points)
{
    // 1.0 before the first point. END-OF-DAY cutoff, matching AccountBalanceLedger's `<= asOf`
    // rule — value and fraction must share a cutoff, or a subscription landing on a month-end
    // inflates the owner's share for exactly one trend point.
    public decimal OwnedFractionAsOf(DateOnly asOf) { /* last point dated <= asOf, else 1.0 */ }
}
public sealed record OwnedFractionPoint(DateOnly EffectiveFrom, decimal Fraction);

public interface IAccountOwnershipSource
{
    Task<IReadOnlyList<AccountOwnership>> GetHistoryAsync(CancellationToken cancellationToken);
}
```

**Why it beat the two claim-seam routes.** A polymorphic `IExternalClaim` reopens a record that
shipped hours earlier (5 pinned tests, a public type rename, a forced `IEnumerable` migration) and
still files pool value under a tile captioned *Outstanding on loans you borrowed*. A telescoping
negative-settlement series works arithmetically but contradicts `ExternalClaim`'s own
*amounts are magnitudes, never signed* rule with **no compile-time guard** — anyone who later clamps
settlements at zero silently freezes every pool stake at its seed level.

Owner-fraction pays neither price. `ExternalClaim.cs`, `IExternalClaimSource.cs` and
`LoanExternalClaimSource.cs` get **zero edits**. Cost: **+1 flat query per request, zero extra FX
round-trips** — the fraction is a ratio of *unit counts only*, so the source never touches
transactions.

`NetWorthDto` gains `OutsideCapitalMdl`, deliberately **outside** the identity — `NetWorthMdl` stays
`Gross − Liabilities + ExternalAssets`, so the existing identity test passes untouched.
`GrossAssetsMdl` now means *the user's share*.

#### Domain — `src/MoneyManagement.Domain/Pools/`

- **`Pool`** — `AccountId` (explicit, immutable, unique; never inferred from `AccountType` — Bybit is
  also CryptoExchange/USD/anchor 0), `Name`, `Currency` (== the account's; the pool never does FX),
  `InceptionDate`, `IsArchived`, `Notes`. The account type must be in
  `AdjustBalanceCommandHandler.EligibleTypes` — an account that cannot take an Adjustment can never
  be re-priced, so its NAV would freeze at inception.
- **`PoolParticipant`** — `PoolId`, `Name`, `IsOwner`, `JoinedOn`, `IsArchived`. Exactly one owner,
  created atomically with the pool. **The owner holds real units, not a residual**, so
  `Σ participantUnits == totalUnits` is assertable and an owner withdrawal is the same operation as a
  friend payout. Archiving a participant holding units is **rejected**.
- **`PoolUnitEvent`** — `PoolId`, `ParticipantId`, `Kind`, `OccurredOn`, `Units`, `NavPerUnit`,
  `PoolValuePreMoney`, `Cash` (`Money?`), `MovementTransactionId` (`Guid?`, SET NULL), `Notes`.
  `Kind = Seed | Subscription | Redemption | Distribution | CostReimbursement`.
  - **`Units` and `NavPerUnit` are plain `decimal` at `numeric(28,12)` — NOT `Money`, NOT a
    ComplexProperty.** Follow `FxRateConfiguration`, not the `numeric(18,2)` money convention: at 2dp
    a NAV near 1.0 quantizes ~0.1% per event and the invariance identity stops holding. **Decide
    before the EF CLI runs** — changing scale later is a second migration over live financial data.
  - `Seed`: one per pool, owner only, **no cash leg and no synthesized transaction** — the owner's
    existing balance simply becomes units. Without this, seeding 1,050 units would write a 1,050
    Income leg and double the account.
  - `NavPerUnit` is an **audit record, never an input**: the fraction reads only `Units`, so a NAV
    later found to be wrong still leaves every fraction internally consistent.

#### The two additions from the user walkthrough

**Cost reimbursement (`CostReimbursement`).** The user pays server bills from their own pocket and the
friends reimburse their share. That money never enters Binance, so this is **not** a subscription and
must not mint value out of nothing. It is a **pure unit transfer from the friends to the owner at the
prevailing NAV**: each friend's units fall by `E × theirShare / nav`, the owner's rise by the same
total. Total units unchanged, pool value unchanged, **NAV unchanged**. No cash leg, no transaction.

*BNB trading fees are explicitly NOT recorded* — bought with pool money and spent on pool trades, so
the snapshot already carries them; recording them again would deduct them twice. The one rule that
matters: **include the BNB balance in the total typed at close, always.** Either convention nets out
over time, but switching between them manufactures phantom profit.

**Close and pay are separate (`Distribution` becomes two-phase).** The month is marked at month-end
but the USDT leaves at the **start of the next month**. If the payout is dated at close and sent days
later, the next snapshot still contains that cash and the app re-attributes it as fresh profit — the
friends would be paid twice on the same money, every month.

- **Close** — mark, redeem units at that NAV, record the amount owed as *pending* (no transaction).
- **Pay** — write the real transaction on the day the transfer actually settles.
- **NAV must use `account balance − unpaid distributions`**, or money sitting in Binance between the
  two dates is counted as pool value a second time.

**Reinvestment needs no code.** With `capitalBase = Σ Subscription cash − Σ Redemption cash`
(distributions never touch it), a friend who skips a month simply leaves their distributable
accumulating and takes it whenever. This *is* the high-water mark, with no stored field: below basis
the distributable is 0, so a green month after a drawdown correctly pays nothing.

#### Guard table — the design's precondition, not a nicety

`value × fraction` means **any cash on the pool account that doesn't mint or burn units is shared
pro-rata with the friends.** Every row ships in the same commit as the writes.

| Handler | Blocked when the account is pooled | Why |
|---|---|---|
| `AdjustBalance` | `kind != Adjustment` | Investment/Withdrawal move value with no unit event |
| `AdjustBalance` | Adjustment dated ≠ today | the delta is computed against a **date-blind** sum; back-dating corrupts that NAV *and* today's balance |
| `CreateTransaction` | any manual Income/Expense | silently re-prices the friends |
| `CreateTransfer` | either leg on the pooled account | must be a Subscription/Redemption so units move with the cash |
| `CommitImport` | any row mapped to the pooled account | **live path** — the 2026-08-25 −300 P2P row is `source=Imported` |
| `DeleteTransaction` | row referenced by a unit event, or dated ≤ the latest unit event | re-prices units already issued to a third party |
| `ArchiveAccount` / `ArchivePool` | non-owner units outstanding | the fraction reverts to 1.0 and the stake is silently reabsorbed |
| Subscription/Redemption/Distribution validator | cash == the account's derived balance, to the cent | the demonstrated slip: two soft-deleted rows on the real account are each the then-current *level* typed into an *amount* field. **One sanctioned exit:** redeeming the *last* of a pool IS the whole pool value, so without an opt-out a wind-down ("I moved everything to Bybit and I'm done") is unreachable — partial redemptions converge on zero units and never arrive. `RecordRedemption.IsFullWindDown` relaxes it, and the handler then **verifies the claim** against the units it retires (`pools.not_a_full_wind_down`), so the flag is an assertion the ledger can refute rather than a bypass switch |
| `CreateLoan` | the disbursement `AccountId` is the pooled one | borrowed cash mints no units, so the friends own a slice of it — and net worth moves **twice**, since `LoanExternalClaimSource` books the claim on top |
| `RecordLoanPayment` | the payment `AccountId` is the pooled one | same shape, opposite direction: cash out with no unit event, so the owner funds part of the friends' stake |
| `DeleteLoanPayment` | its linked row is referenced by a unit event, or dated ≤ the latest one | soft-deletes the transaction **inline**, so `DeleteTransaction`'s guard never sees it — same rule, second door |
| `CreatePool` | a savings goal already links that account | the reverse of the goal guard, which only asked one way: `SavingsGoal.Saved` **is** the balance, so goal-then-pool counts the friends' capital as the user's progress |
| `DeleteAccount` | a pool (even archived) holds the account | `pools.account_id` is `RESTRICT`; a pool with no transactions clears every other check, so the FK raises an unhandled **500** where a 409 "archive it instead" was owed |
| `RecordRedemption` | a `DestinationAccountId` is named and the participant is **not the owner** | the pool-side leg is excluded from the owner's figures, but the **counter leg lands on a wholly-owned account**, where it reads as the user's own contribution and counts in full towards net worth — a friend's money would become the user's. The owner's Bybit withdrawal (§6, Sep 18) is the intended use and is untouched; a friend's payout leaves the tracked world, so it is one leg with no counter account (`pools.destination_requires_owner`) |

**Read-side divergence, decided explicitly:** `/accounts`, Balance-over-time and `GetSummary` keep
showing the **full** account value with a "Pooled" badge — the account really does hold that money.
Only the net-worth card and trend apply the fraction. The **Performance card** needs owner-only
arithmetic or it becomes meaningless: its current formulas would count the friends' 2,000 as the
user's contributions and their payouts as the user's withdrawals.

#### Sequencing — per CLAUDE.md, backend to `c-sharp-pro`, frontend to `frontend-developer`

| # | Commit | Owner | Days |
|---|---|---|---|
| 1 | **Ownership seam only** — 2 abstraction files + `AccountOwnershipLedger` + Scrutor clause + `IEnumerable` injection in both handlers + `OutsideCapitalMdl` + fake source. **Green with an empty source; every existing assertion holds byte-for-byte.** | `c-sharp-pro` | 1 |
| 2 | Domain (3 entities + enums + errors) + EF configs + **EF CLI** migration + backup **v6** + round-trip assertions | `c-sharp-pro` | 3.5 |
| 3 | Write slice: commands, movement synthesis, two-leg redemption, mark-in-same-save, **the entire guard table** | `c-sharp-pro` | 3.5 |
| 4 | Read slice: `PoolUnitRegister`, `PoolAccountOwnershipSource`, queries, endpoints, Performance-card owner arithmetic, reconciliation tripwire | `c-sharp-pro` | 2.5 |
| 5 | `/pools` + `/pools/[id]`, components, API client, nav item, total-assets sub-line, MSW fixtures | `frontend-developer` | 3 |
| 6 | WIKI / BACKEND (a new "Account ownership and net worth" section stating **why there are two seams**) / FRONTEND / QA matrix + Playwright pass on `:3001` / `:5180` | main thread | 2 |

Owed regardless of route: a test with a stake that **rises** point-over-point. Every existing claim
fixture is monotonically non-increasing, so nothing in the repo covers a rising external interest.

**Brief `c-sharp-pro` explicitly:** stop the API before `dotnet ef migrations add` (DLL lock); run
`migrations add` **only** — `DesignTimeDbContextFactory` hardcodes the **real** database; convert the
generated file to a file-scoped namespace.


## 6. Worked September 2026 example

All USD. Pool = `Binanance` (`019e841a-f5cf-7d8f-9d81-f9dcdb0efa4a`), opening anchor 0.00, live
balance 1,050.00 verified 2026-09-06. USD/MDL 17.2682. *(Illustrative dates and market moves; the
starting figures are real.)*

**Sep 6 — create the pool.** Binance really holds **1,092.00**, not the 1,050.00 the app derived
(last mark was 08-27). The create-pool command writes the catch-up mark first, then seeds.

```
tx#1   Binanance  2026-09-06  Income   42.00 USD  IsAdjustment  cat: Balance Adjustment
inv#1  "Me"       Kind=Owner  JoinedOn 2026-09-06  CostBasis 42.89   (= 842.89 in − 800.00 out)
evt#1  Issue/Seed 1,092.0000000000 units @ 1.0000000000 = 1,092.00
```

Pool 1,092.00 · units 1,092.0000 · **NAV 1.0000000000**

**Sep 8 — Andrei subscribes 1,000.** The dialog demands the pre-money total: 1,092.00, equal to
derived, so no mark is written (a zero delta is skipped, not an error).

```
tx#2   Binanance  2026-09-08  Income  1,000.00 USD  IsTransfer  cat: Investor capital
evt#2  Issue/Subscription 1,000.0000000000 units @ 1.0000000000 = 1,000.00  → tx#2
```

Pool 2,092.00 · units 2,092.0000 · **NAV 1.0000000000 — unchanged. That is the invariance.**

**Sep 12 — Bogdan subscribes 1,000.** Binance reads **2,175.68** against a derived 2,092.00. The mark
goes first, *then* units are priced.

```
tx#3   Binanance  2026-09-12  Income     83.68 USD  IsAdjustment
tx#4   Binanance  2026-09-12  Income  1,000.00 USD  IsTransfer   cat: Investor capital
evt#3  Issue/Subscription 961.5384615385 units @ 1.0400000000 = 1,000.00  → tx#4
```

`navPre = 2,175.68 ÷ 2,092.0000 = 1.0400000000`. That 83.68 splits `1092/2092` to the owner
(**43.68**) and `1000/2092` to Andrei (**40.00**). Bogdan gets none of it, correctly — his money
wasn't there.

Pool 3,175.68 · units 3,053.5384615385 · **NAV 1.0400000000**

**Sep 18 — 400 moves to Bybit.** One command writes the two-leg transfer *and* the unit event.

```
tx#5   Binanance  2026-09-18  Expense  400.00 USD  IsTransfer  counterAccount=Bybit
tx#6   Bybit      2026-09-18  Income   400.00 USD  IsTransfer  counterAccount=Binanance
evt#4  Redeem/Redemption 384.6153846154 units @ 1.0400000000 = 400.00  → tx#5
```

Pool 2,775.68 · units 2,668.9230769231 · **NAV 1.0400000000 — unchanged.** The withdrawal diluted
only the owner: 35.76% → **26.5044961955%**. No bookkeeping, no user input. *(And this is exactly the
§2(iv) decision made visible.)*

*(The fraction is `707.384615384615 ÷ 2,668.923076923077` = **0.265044961955** at the 12dp of the
`numeric(28,12)` unit columns. Elsewhere this document rounds it to 26.50% for readability; the
precise value is the one `PoolUnitRegister.OwnedFractionOf` produces and the one net worth
multiplies the account balance by.)*

**Sep 30 — close the month.** Binance reads **2,935.82**; the preview recomputes on every keystroke
via `dryRun`.

```
mark   = 2,935.82 − 2,775.68 = +160.14
navEnd = 2,935.82 ÷ 2,668.9230769231 = 1.1000017293
```

| | units | value | cost basis | payable | units redeemed |
|---|---:|---:|---:|---:|---:|
| **You** | 707.3846153846 | 778.12 | 42.89 | — | — |
| **Andrei** | 1,000.0000000000 | 1,100.00 | 1,000.00 | **100.00** | 90.9089480 |
| **Bogdan** | 961.5384615385 | 1,057.69 | 1,000.00 | **57.69** | 52.4453720 |
| **Pool** | 2,668.9230769231 | **2,935.82** | | **157.69 out** | |

```
tx#7   Binanance  2026-09-30  Income   160.14 USD  IsAdjustment
tx#8   Binanance  2026-09-30  Expense  100.00 USD  IsTransfer  cat: Investor payout
evt#5  Redeem/Distribution  90.9089480 units @ 1.1000017293 = 100.00  → tx#8
tx#9   Binanance  2026-09-30  Expense   57.69 USD  IsTransfer  cat: Investor payout
evt#6  Redeem/Distribution  52.4453720 units @ 1.1000017293 =  57.69  → tx#9
```

All five rows in **one `SaveChangesAsync`**. Then the USDT actually gets sent — see the
payment-timing rule above.

**After the close:**

```
pool  = 2,935.82 − 157.69 = 2,778.13
units = 2,668.923076923077 − 90.908947991464 − 52.445372096276 = 2,525.568756835337
NAV   = 2,778.13 ÷ 2,525.568756835337 = 1.1000017293   ← UNCHANGED
```

*(`units` and `nav_per_unit` are `numeric(28,12)`, not the 10dp this document prints. Earlier drafts
gave **2,525.5687569231**, which is what the same subtraction returns when the two retirements are
first rounded to the 7dp of the table above; the shipped retirements are `90.908947991464` and
`52.445372096276`. The two agree to 1e-8 of a unit, but the 12dp figures are the ones the tests
assert — see `PoolWorkedExample.TotalUnitsAfterClose`.)*

| | units | value |
|---|---:|---:|
| You | 707.3846153846 | 778.12 |
| Andrei | 909.0910520 | 1,000.00 |
| Bogdan | 909.0930895 | 1,000.00 |

**Both friends are back at exactly their 1,000 base, both holding ≈ 909 units, despite entering at
different NAVs.** No code says "reset their base" — it falls out of redeeming at NAV.

**Attribution check:**

| | Sep 12 (+83.68) | Sep 30 (+160.14) | total |
|---|---:|---:|---:|
| You | +43.68 | +42.44 | **+86.12** |
| Andrei | +40.00 | +60.00 | **+100.00** |
| Bogdan | — | +57.69 | **+57.69** |
| | | | **243.81 ≈ 243.82** ✓ |

Bogdan earned USD 42 less than Andrei on the same 1,000 because he bought in 4% higher. **A naive
capital-weighted split would have paid each friend 78.86** — Andrei short by 21.14, Bogdan over by
21.17. That ≈ USD 21/month per friend, on 1,000 of capital, is what units buy.

**Net worth, 2026-09-30:**

```
Binanance gross      2,778.13 × 17.2682 = MDL 47,973.30
Investors' capital  −2,000.00 × 17.2682 = MDL 34,536.40
Your share             778.12 × 17.2682 = MDL 13,436.73   ✓
```

> **The missing cent is deliberate.** Each stake is priced independently as `units × nav`, so the
> three round to 778.12 + 1,000.00 + 1,000.00 = 2,778.12 against a pool of 2,778.13. Earlier drafts
> gave the penny to the owner as a residual (778.13); the shipped model does **not** — the owner
> holds REAL units, which is the only reason `Σ participantUnits == totalUnits` is assertable at all
> (see the reconciliation tripwire). A one-cent unassigned residual is the price of that, and it is
> the correct price.

Note the payout moved net worth by **exactly zero** — gross fell 157.69 and the liability fell
157.69. Paying someone what you owe them makes you no poorer. That identity is worth a unit test.

---

## 7. Documentation plan

Split by phase; each set lands in the same commit as its code.

**`WIKI.md`** — new "Pooled capital (Binance)" section under Core Concepts: the arrangement, the four
decisions with dates and reasons, units/NAV in a paragraph, the `max(0, value − costBasis)` rule,
the monthly close, and the exact message sent to the friends. Account Model Roadmap gains Phases
0/1/2. Known rough edges gains: **`/accounts`, Balance-over-time and `GetSummary` show pool-gross**,
by explicit decision — the account really does hold that money, so they carry a "Pooled" badge instead
(the **Performance card is the exception and IS owner-only**, decided 2026-09-07); **one pool = one
account**; v6 invalidates v5 backups; tax is not modelled. Move *"loans do not affect net worth"* to Resolved at Phase 1.
**Also fix line 53** — the claim that both dashboard endpoints filter `IsTransfer`/`IsAdjustment` is
wrong for the trend endpoint (`BACKEND.md:392` has it right, and
`NetWorthTrendFilterDisciplineTests` exists to stop someone "fixing" the code to match the doc).

**`BACKEND.md`** — new `### Pools slice` after the Loans slice: entities, the invariance identity,
the `capitalBase = Σ subs − Σ redemptions` rule and why it is **floored where consumed**, the
`IsTransfer` money-side rule, the full guard table, the 11 endpoints, and the `numeric(28,12)`
deviation from the `numeric(18,2)` money convention. Extend `### External claims and net worth`
into **"Account ownership and net worth"**, stating **why there are two seams**: a claim only ever
*declines*, a pool stake *floats* with the account's value, so the pool rides a separate
`IAccountOwnershipSource` that supplies a dated owner **fraction** applied at the asset leg. Record
the two rules a reader will otherwise get wrong: the as-of convention (the card converts at **today**,
the trend at **each point's own date**), and that a `Distribution` retires units at **`SettledOn`**,
not at close — otherwise net worth credits the owner with cash already promised to the friends.
Event-handler list, migrations list, DataPortability arrays 11 → 14 and schema history 5 → 6 with
both wipe and insert orders.

**`FRONTEND.md`** — correct the "Aggregate-side trap" note (~line 625): *"net-worth-card sums account
balances so it's safe"* becomes false at Phase 1, as does the adjacent claim that the trend endpoint
already excludes transfers server-side. Document `NetWorthCard` as server-fed from
`GET /dashboard/net-worth`, plus the new dashboard tile, the **`/pools` and `/pools/[id]` routes** with
their components and dialogs, the `/accounts` "Pooled" badge fed by the new `AccountDto.IsPooled`, and
the owner-only Performance card on account detail.
*(An earlier draft of this plan put the pool UI on the account page rather than its own route; the
re-scoped Phase 2 sequencing table in §5 supersedes it — `/pools` + `/pools/[id]` is what ships.)*

**`QA.md`** — Phase 1 rows (next free number is 59): gross/deductions/net with a loan liability; a
trend point deducts as-of and earlier points don't; a loan payment reduces the deduction; an
unconvertible claim is omitted and trips the amber warning; plus a row for the balance-formula
extraction. Phase 2 rows: create pool + seed owner; subscription with forced pre-money valuation
cross-checked in Postgres against both the mark and the unit event; **preview equals post**; a losing
month writes the mark and zero payouts; owner withdrawal leaves NAV unchanged; payout drops balance
and liability equally; investor exit; wind-up; payment-timing; the archive guards. Re-run rows 42–43
(backup export + destructive restore) after v6.

**Stale claims to fix while in there:** *"there is no Infrastructure test project"* appears at
`WIKI.md:59`, `WIKI.md:119`, `BACKEND.md:328`, `BACKEND.md:509` and `QA.md:165` —
`tests/MoneyManagement.Infrastructure.Tests` exists with 18 source files. Separately, `QA.md:158`
claims the *Api* project has no test project; `tests/MoneyManagement.Api.Tests` has ~17 files.

---

## 8. Decisions needed

Say "yes to defaults" if these all look right.

| # | Question | Recommended default |
|---|---|---|
| 1 | ~~Do the friends bear losses?~~ | ✅ **ANSWERED 2026-09-06: yes.** Pro-rata; a month can end their stake below 1,000, and the user does not cover a negative month from their own money. This unblocks unit/NAV accounting. |
| 2 | ~~High-water mark?~~ | ✅ **ANSWERED 2026-09-06: yes — "no payment below their investment."** `payable = max(0, value − capitalBase)`, no stored field. A green month after a drawdown can pay zero; this is stricter than the literal words spoken to the friends and must be in the message. |
| 3 | ~~Pool scope: Binance only, or + Bybit?~~ | ✅ **ANSWERED 2026-09-06: Binance only.** The user moves *their own* profit out to Bybit, so Bybit sits outside the pool and those transfers are owner redemptions at NAV. |
| 4 | ~~Any cut for you?~~ | ✅ **ANSWERED 2026-09-06: no performance fee, but real costs ARE shared.** BNB trading fees need no handling at all — they are bought with pool money and spent on pool trades, so the snapshot already carries them; recording them again would double-count. **Server costs** are paid from outside the pool and the friends reimburse their share pro-rata: one typed figure at close. |
| 5 | **Exact arrival dates, and the *pre-money* Binance total on each.** | ⏳ **STILL OPEN — the only perishable item.** If the money has not landed, snapshot the pool the day before it does. If it has, recover the pre-arrival value from Binance history now. |
| 6 | ~~How do they get paid?~~ | ✅ **ANSWERED 2026-09-06: a Binance transfer of the profit.** Same currency, one leg, exact — and it avoids the MDL-payout trap (zero FX exists in any write path, and paying from elsewhere would leave pool value unchanged while units retire, over-crediting the other friend). |
| 7 | **Distributions gross or net of any tax withholding?** | ⏳ **STILL OPEN.** Default **gross**; tax is not modelled anywhere and this is not tax advice. Decide before the first payout — withholding would split a distribution into two legs and break the `Units × PricePerUnit == Amount` invariant. |

---

## 9. Requirements confirmed after the first review (2026-09-06)

These came out of walking the plan through with the user and are **binding on Phase 2**. Three were
not in the original design at all.

**Pool boundary — the whole Binance account, one number.** Binance holds futures / earn / fiat / spot
sub-wallets and the user explicitly does not want them tracked separately. So the pool is the app's
existing `Binanance` account, and the figure typed at close is the total across everything on Binance.

*Consequence, and it is the load-bearing one:* moving money futures → Earn is **invisible** to the app,
so it cannot be an exit. Value leaves the pool only when it lands in **another tracked account**
(Bybit). One rule, no special cases — and it removes the silent-overpay trap where money pulled into
Earn would still have been credited to the friends every month.

**The owner's profit and cost reimbursements stay in — the owner's share grows.** No transfer, no cash
leg. The user withdraws to Bybit when they actually want value out, and that is an owner redemption at
NAV like any other.

**Reinvestment, per investor, per month.** A friend may say *"don't send this month's profit, leave it
in."* Then: redeem nothing, and **raise that investor's capital base to their current value.** Raising
the base is what keeps the high-water mark honest — without it the app would think the same profit is
owed again next month. Each investor's stake can therefore diverge (one takes cash, one compounds),
which units handle for free and a fixed-percentage model cannot.

**Close and pay are separate steps.** The month is marked at month-end but the USDT physically leaves
at the **start of the next month**. If the payout is dated at close and sent days later, the next
month's snapshot still contains that cash and the app re-attributes it as fresh profit — the friends
would be paid twice on the same money, every month. So:
- **Close** — mark the pool, redeem units at that NAV, record the amount owed as *pending*.
- **Pay** — write the real transaction on the day the transfer actually settles.
- NAV must use **`account balance − unpaid distributions`**, or the money sitting in Binance between
  the two dates gets counted as pool value a second time.

**BNB is pool property until burned.** Include the BNB balance in the total typed at close. Either
convention nets out over time (include it and value is flat at purchase; exclude it and the pool dips
at purchase instead of at consumption) — but *switching* between them manufactures phantom profit.

---

## 10. BUILD LOG — resume from here

> Live status of the Phase 2 build. **Everything below is UNCOMMITTED** in the working tree; the user
> has not authorized a commit for this feature. Update this section as each commit lands.

| # | Commit | Status |
|---|---|---|
| 1 | Ownership seam (`IAccountOwnershipSource`, `AccountOwnership`, `AccountOwnershipLedger`, `OutsideCapitalMdl`, both dashboard handlers, Scrutor clause) | ✅ **done, verified** — behaviour-neutral, claim seam untouched |
| 2 | Domain `Pools/*` + 3 EF configs + migration `20260906212228_AddPools` + backup **v6** | ✅ **done, verified** — real DB untouched (`InitialCreate` + `AddLoans` only), no model drift |
| 3 | `PoolUnitRegister` + `PoolMovements` + write commands + endpoints + **the guard table** | ✅ **done, verified** — 9 routes, all 10 guard rows tested by error code, +91 tests |
| 4 | `PoolAccountOwnershipSource` + read queries + endpoints + Performance-card owner arithmetic + reconciliation tripwire | ✅ **done, verified 2026-09-07** — audited, 31 findings triaged, read slice went from **0 tests to +64**. See "Commit 4 audit" below |
| 5 | Frontend: `/pools`, `/pools/[id]`, components, API client, nav item, total-assets sub-line, MSW fixtures | ✅ **done, verified 2026-09-07** — 2 routes + 19 components + 9 dialogs; frontend tests **613 → 798** |
| 6 | Docs (WIKI / BACKEND / FRONTEND / QA rows) + Playwright smoke test on `:3001` / `:5180` | ✅ **done, verified 2026-09-07** — QA matrix rows **60-64**; smoke pass caught one blocker the 2,189 automated tests could not |

**Baseline after Commit 3:** 1,334 backend tests (Domain 371, Application 696, Infrastructure 112,
Api 155) + 613 frontend. `dotnet build MoneyManagement.slnx` clean, 0 warnings.

**Final baseline, all six commits (2026-09-07):** **1,402 backend** — Domain 371, Application **755**,
Infrastructure **113**, Api **163** — of which 1,401 pass; the only red is the pre-existing, unrelated
`ReportsEndpointTests.Top_payees_returns_aggregated_rows`. Plus **798 frontend** across 89 files, all
passing. **2,199 green tests, zero skipped** (from 1,947 at the start of the session: **+252**). Build
clean, **0 warnings**. `dotnet ef migrations has-pending-model-changes` → *no changes have been made to
the model*. `npx tsc --noEmit` → 5 errors, all pre-existing `LoanDto.isArchived` in loans test files.
`npx biome check .` → 16 diagnostics, **all on files this work never touched** (CRLF noise from
`core.autocrlf=true` + `* text=auto` versus Biome's LF; verified by checking each flagged file is
unmodified in the working tree).

**Real DB is untouched** — still `InitialCreate` + `AddLoans`, no pool tables. `AddPools` has been
applied to **`money_management_test` only**, by the QA app on startup.

> ⚠️ **`money_management_inttest` has accumulated residue** — ~813 categories and ~529 budgets from
> past runs. `EfMaterializationTests.BudgetPeriod_RoundTrips_ThroughEfMaterializationConstructor`
> picks a category with `FirstAsync()` and **no `ORDER BY`**, then creates an active budget on it, so
> a heap-order shift can make it collide with an existing active budget (`23505` on
> `ix_budgets_category_id_active`). Seen once on 2026-09-07 and not reproducible afterwards across
> five consecutive runs. Pre-existing and unrelated to pools; the durable fixes are a reset of that
> database or seeding its own category, which is also what the file's own contract already asks of
> every test there. Same root cause as the `Top_payees` known-red.

> ⚠️ **Run test projects ONE AT A TIME.** `dotnet test MoneyManagement.slnx` runs all four in parallel
> against the shared `money_management_inttest` database and produces a *moving* extra failure
> (`Infrastructure.Tests`' seeder mutating categories while `Api.Tests` creates accounts). Three
> separate agents hit this independently on 2026-09-07. It is contention, not a regression — same root
> cause as the `Top_payees` note above.

### Commit 4 audit — 2026-09-07

The read slice was written but never reviewed (the session died mid-write in a power cut). A
seven-dimension audit with adversarial verification confirmed **26 findings + 5 from a completeness
critic**. Resolved this session:

- 🔴 **The headline number was wrong.** Both dashboard handlers multiplied the owner fraction into
  the **raw** account balance, but between a distribution's *close* and its *settle* the account
  still holds cash already promised to the friends. §6's Sep-30 close read **822.29** instead of
  **778.12** — and a month-end close pinned that error onto that trend point **forever**, because the
  October payout row is dated after the as-of cutoff. **Fixed** by retiring a `Distribution`'s units
  at `SettledOn` rather than `OccurredOn` in `PoolUnitRegister.OwnershipCurve` (one extra date column
  in the projection; the seam stays unit-counts-only, no balance, no FX). Paying a distribution now
  moves net worth by exactly zero.
- **Zero/negative NAV** was accepted by `RequireNav()` and divided by → `DivideByZeroException`. Now
  a `PoolErrors.NavUndefined` failure, mirrored in `CreatePoolCommandHandler`'s backfill loop.
- **`capitalBase` could go negative** (normal for the owner: seeded units, no cash leg, then a
  withdrawal to Bybit) making `Distributable` exceed the whole stake. Floored **where consumed**;
  the stored `CapitalBase = Σ subs − Σ redemptions` rule is untouched.
- **Four reconciliation replay defects**: a same-day sibling distribution line was double-counted; the
  same-day exemption was scoped by calendar day instead of by close (now by shared `PoolValuePreMoney`);
  inception-day rows were falsely unaccounted; and leftover unlinked cash claims were silently
  discarded — now a **fourth finding class** (`UnbackedCashClaims`), the only check that can catch a
  backfill claiming money that never landed.
- **Mark staleness was measured off adjustment ROWS**, but a close that types a value equal to the
  derived balance writes no row (§6's Sep-8 step). A stablecoin-parked pool priced monthly would have
  climbed past 30/60/90 days of false staleness on the one tripwire the design says to trust. Now
  `max(latest adjustment ≥ inception, latest non-Seed event)`.
- **Five more guard-table holes closed**: `CreateLoan`, `RecordLoanPayment` and `DeleteLoanPayment`
  could all move money on a pooled account; `CreatePool` had no reverse savings-goal check (a goal
  linked *before* the pool existed would count the friends' capital as the user's progress, reporting
  itself complete at ~MDL 54,838 against a real share of ~MDL 18,857); and `DeleteAccount` hit the FK
  as a 500 instead of a 409.
- **Performance-card owner arithmetic built** (the user chose this over shipping pool-gross). Transfer
  legs are 0%/100% and are included or excluded **whole**; `IsAdjustment` marks are **scaled by the
  pre-money fraction** — §6's Sep-12 mark splits 1092/2092 to the owner, not the end-of-day 1092/3053.
  Reconciles: 842.89 − 1,200.00 + 1,135.23 = 778.12, the register's owner stake to the cent.
  `AccountDto`/`AccountDetailDto` gained `IsPooled` for the badge.

**Closed 2026-09-07, after the test-writing pass** — the last three backend defects in Commit 4:

- **A false value drift when a mark landed after a priced event on the same day.**
  `PoolReconciliation.FindValueDrifts` re-derived each event's pre-money from an **end-of-day**
  balance and undid only the *cash* of unit events, so a re-pricing `IsAdjustment` row written later
  the same day stayed inside it and every event already priced that day reported a drift **exactly
  equal to the mark**. Reachable with no guard bypassed at all — `AdjustBalance` deliberately still
  allows an Adjustment dated today on a pooled account, and a same-day close does it too. A false
  alarm on the one tripwire the design tells the user to trust. **Fixed by folding marks forward:**
  the replay strips every still-unclaimed mark dated that day and lets each event *claim* the prefix
  its own recorded pre-money implies; whatever it does not claim was written after it. Deleting a
  mark that justified a price still fails to reconcile, which is the finding the check exists for.
  *Rejected alternative:* ordering by `CreatedAt` (undo a same-day mark whose `CreatedAt` is after
  the event's). `AuditableEntitySaveChangesInterceptor` reads the clock **once per
  `SaveChangesAsync`** and stamps every Added entity with that one value, so a mark and the event it
  prices — always saved together — get **identical** timestamps and a strict `>` would be correct in
  principle. Rejected anyway, on three counts. **(1)** The interceptor lives in `Infrastructure` and
  **never runs in the Application unit tests** (`SharedKernel.csproj` says so in as many words), and
  `FakeApplicationDbContext` does not stamp — so the fix would be inert in every test that drives it
  through the real handlers, and the waiting test could only be made green by hand-assigning the
  timestamps, i.e. by fabricating the very ordering under test. **(2)** `CreatedAt` is a physical
  write stamp being asked to mean "was this mark inside the pre-money this event was struck
  against?"; the two coincide only while every write goes through one monotonic clock, and two saves
  landing in the same tick silently restore the old buggy behaviour with nothing to notice it.
  **(3)** It needs a new column threaded through `GetPoolDetailQueryHandler`'s projection into
  `PoolAccountRow`, where the forward fold needs nothing new — `Id` (UUIDv7, already carried) orders
  the day's marks. The shipped fix reads only what the write path itself recorded: the event's own
  `PoolValuePreMoney` and the account's rows.
- **A pool could never be wound down to zero units through the API.** `GuardCashIsNotABalance`
  rejects cash equal to the derived balance, and redeeming the *last* of a pool is by definition
  exactly that; partial redemptions converge on zero and never reach it. **Fixed** with an opt-in
  `RecordRedemptionCommand.IsFullWindDown`, **verified against the units retired** rather than
  trusted (`pools.not_a_full_wind_down`), so the typo the guard was built to catch still cannot get
  through. `Handle_ZeroUnitsPool_…` now drives the real handler instead of building its event at the
  domain boundary.
- **A non-owner redemption could name a tracked destination account.**
  `RecordRedemptionCommandHandler.ResolveDestinationAsync` did not check `participant.IsOwner`, so
  the counter leg landed on a wholly-owned account where it read as the user's contribution and
  counted in full towards net worth — a friend's money becoming the user's. **Fixed**
  (`pools.destination_requires_owner`); the owner's Bybit path is untouched. New guard-table row
  above.

**Closed 2026-09-07, the LAST Commit-4 defect** — found on the QA app, not by a test:

- **The Performance card mis-split a mark that shared its date with a capital event.** Marks were
  attributed with `OwnedFractionAsOf(rowDate.AddDays(-1))`. The ownership curve carries **one
  end-of-day point per date**, so "the day before" is exactly the pre-money state *whenever a date
  holds at most one capital event* — the normal case, and what §6 walks, which is why every §6 figure
  was right. Put a capital event and a **later** mark on one calendar day and it breaks: the mark
  takes the *pre-event* fraction. Reproduced live on a same-day bootstrap — pool created (mark
  +1,092.00, seed 1,092 units), Andrei subscribes 1,000, then the month closes writing a **+208.00**
  mark — where the owner was credited with all 208.00 instead of `1092/2092 × 208 = 108.57`, an
  over-statement of **99.43 USD**. **Net worth was never affected**: it multiplies a whole-day
  balance by the end-of-day fraction, which is the correct pairing, and that path is untouched.
- **Fixed** by attributing each mark at the units state in force *at the instant it was written*.
  `PoolPreMoneyReplay` (extracted from `PoolReconciliation.FindValueDrifts`, same walk, same
  behaviour) already re-derives each event's pre-money and lets it **claim** the day's marks that
  close the gap; `PoolMarkAttribution` reads the other half of that result — a mark claimed by event
  `E` was written immediately before `E`, so it takes the fraction *before* `E`; a mark nobody claims
  was written after the day's last event, so it takes the day's closing fraction.
  `PoolUnitRegister.OwnershipTimeline` is the single fold both readings come off, with
  `OwnershipCurve` now its per-day projection, so the card and the dashboard cannot diverge.
- ⚠️ **The pairing deliberately never orders a mark against its own unit event by `Id`.** They are
  written in one `SaveChangesAsync`, and .NET 10's `Guid.CreateVersion7` has **no intra-millisecond
  counter** — the low bits are random, so ids minted inside one millisecond sort randomly (measured
  on this machine: 171 of 200 three-id runs came out unsorted). The pairing is recorded-pre-money
  arithmetic instead. `Id` order is used only *between*
  separate commands, which is the assumption the whole slice already runs on. It follows that unit
  tests putting two pool commands on one calendar day must put a **real millisecond** between them
  (`AccountDetailOwnerArithmeticTests.SeparateWritesAsync`), or the ids come out shuffled and the
  scenario under test is not the one that ran.
- Residue, stated rather than hidden: a manual snapshot typed *before* a same-day `CreatePool`, and a
  mark written before a distribution *settles* that same day, still have no evidence in the data to
  place them, and fall back to the day's closing fraction. Both need two commands on one calendar day
  **and** a non-trivial intra-day fraction change to matter at all.

**§6 corrections made this session** (it is the oracle every test asserts against): the pre-withdrawal
owner share is **35.76%**, not 34.38% (the old figure divided units by *value*); and the post-close
owner stake is **778.12** (`units × nav`), not the residual 778.13 — see the note under the net-worth
block about the deliberately unassigned cent.

**One known-red test, PRE-EXISTING and unrelated:** `ReportsEndpointTests.Top_payees_returns_aggregated_rows`.
The shared `money_management_inttest` DB has accumulated 59 distinct Sept-2024 payees and the test
asserts against its own payee inside `limit=50`. Verified against the untouched tree and the test
file has zero references to pools. Needs a DB reset or a tighter filter — **do not "fix" it as part
of this feature.**

### Hard rules for whoever picks this up

- **Never** `dotnet ef database update` — `DesignTimeDbContextFactory` hardcodes the REAL database.
  The migration is generated only; the real DB must stay on `InitialCreate` + `AddLoans` until the
  user deliberately applies it.
- **Never** `git commit` / `git push` without the user asking in that turn.
- Real DB (`money_management`) is **read-only `SELECT`** unless the user approves a specific statement.
- Backend work → `c-sharp-pro`, frontend → `frontend-developer`. Main thread owns docs + verification.
- Smoke test on the QA ports (`:5180` API with the `qa` profile, `:3001` web) before any commit.
  `POSTGRES_PASSWORD` is in the gitignored `.env` with **CRLF** endings — `grep`+`tr -d '
'` it,
  do not `source` it.
- ⚠️ **Backup v6 invalidates every existing v5 export.** Re-export the day this ships.

### Decisions already settled — do not re-litigate

Friends bear losses pro-rata; high-water mark on via `capitalBase = Σ subscriptions − Σ redemptions`;
no performance fee but server costs ARE shared; BNB fees recorded nowhere (already in the snapshot);
pool = the whole Binance account, one typed number; value leaves the pool only into another tracked
account (Bybit); owner's profit and cost recovery stay in and grow their share; reinvestment needs no
code; close and pay are separate phases.

### Still open — needs the user

1. **The pre-money Binance total on each friend's arrival date** — the only perishable item, and it
   blocks the real-data bootstrap. Everything else can be reconstructed.
2. Tax: gross vs net distributions. Not modelled; decide before the first payout.

---

## Appendix — verification status

Everything asserted about the codebase above was checked against the files on 2026-09-06. Confirmed
directly: `net-worth-card.tsx:12`; `GetNetWorthTrendQueryHandler.cs:122`;
`AdjustBalanceCommandHandler` at `Features/Transactions/AdjustBalance/` with the delta at `:169` and
the date-blind balance at `:161-167`; `GetAccountDetailQueryHandler.cs:126-207`;
`Transaction.cs:129-134` using `DateTime.UtcNow` directly; `CategorySeeder.cs:44-67` backfilling by
id; `CategoryConfiguration.cs:34` being a non-unique index; `DeleteAccountCommandHandler.cs:35-49`
already blocking accounts with transactions; `BackupSchemaVersion.Current = 5`;
`ImportDataCommandHandler.cs:14-17`; absence of any `GET /dashboard/net-worth`.

Real-DB figures (read-only `SELECT`): Binanance 12 live rows → balance USD 1,050.00, contributions
842.89, withdrawals 800.00, net P&L 1,007.11; five non-archived `Received` loans, all
account-linked, none repaid, EUR 6,000 + MDL 14,152.01.

**Working tree at time of writing** had uncommitted changes in `QA.md`, `WIKI.md` and
`web/tests/setup.ts` (a Node-26 `localStorage` shim), unrelated to this work. Don't let the first
commit sweep them in.
