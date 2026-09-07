'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AddPoolParticipantRequest,
  AddPoolParticipantResponse,
  CloseDistributionRequest,
  CloseDistributionResponse,
  CreatePoolRequest,
  CreatePoolResponse,
  PoolDetailDto,
  PoolDto,
  RecordCostReimbursementRequest,
  RecordCostReimbursementResponse,
  RecordRedemptionRequest,
  RecordRedemptionResponse,
  RecordSubscriptionRequest,
  RecordSubscriptionResponse,
  SettleDistributionRequest,
  SettleDistributionResponse,
} from '@/src/types/api';
import { apiClient } from './client';

export const poolKeys = {
  all: ['pools'] as const,
  list: (includeArchived: boolean) => [...poolKeys.all, { includeArchived }] as const,
  detail: (id: string) => [...poolKeys.all, 'detail', id] as const,
};

/**
 * Invalidation set for every pool mutation.
 *
 * Applied unconditionally, and deliberately wide. Almost everything a pool
 * does writes real rows on the pooled account: a subscription mints an
 * Income leg, a redemption writes both legs of a transfer, and every pricing
 * route can additionally write a catch-up **mark** (`IsAdjustment`) before it
 * strikes the NAV — so even a "unit-only" cost reimbursement can move the
 * account's balance when `poolValueNow` is supplied.
 *
 * `['dashboard']` is doubly load-bearing here: the net-worth card and trend
 * multiply the OWNER's fraction into the account balance, so a subscription
 * that changes nobody's cash position still changes the user's net worth by
 * changing who owns what. `['accounts']` matters because the balance moved
 * and because `isPooled` flips the moment the first pool lands on an account.
 *
 * Mirrors `invalidateMoneyMovement` in ./loans.
 */
function invalidatePoolMovement(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: poolKeys.all });
  queryClient.invalidateQueries({ queryKey: ['accounts'] });
  queryClient.invalidateQueries({ queryKey: ['transactions'] });
  queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  queryClient.invalidateQueries({ queryKey: ['reports'] });
  queryClient.invalidateQueries({ queryKey: ['budgets'] });
  queryClient.invalidateQueries({ queryKey: ['goals'] });
}

/**
 * Lists pools — non-archived only by default; pass `includeArchived: true`
 * for the "Show archived" toggle.
 *
 * Keyed as `['pools', { includeArchived }]` — mirrors `useLoans`, so both
 * variants sit under the `['pools']` prefix and every mutation refreshes them
 * via prefix invalidation.
 */
export function usePools(includeArchived = false) {
  return useQuery({
    queryKey: poolKeys.list(includeArchived),
    queryFn: () => apiClient.get<PoolDto[]>(`/pools?includeArchived=${includeArchived}`),
  });
}

/**
 * GET /pools/{id} — the rich per-pool detail DTO behind `/pools/{id}`
 * (roster, ledger, mark staleness, reconciliation).
 *
 * Disabled until a non-empty id is supplied so the hook is safe to call from
 * a Client Component reading its id from the route params. Archived pools
 * still resolve here — they only drop off the list endpoint.
 */
export function usePoolDetail(id: string) {
  return useQuery({
    queryKey: poolKeys.detail(id),
    queryFn: () => apiClient.get<PoolDetailDto>(`/pools/${id}`),
    enabled: Boolean(id),
  });
}

/**
 * Creates a pool. The command seeds the owner's existing balance as units and,
 * when `poolValueAtInception` is supplied, writes a catch-up mark on the
 * account first — so this can move a real balance.
 */
export function useCreatePool() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreatePoolRequest) => apiClient.post<CreatePoolResponse>('/pools', input),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Adds a participant holding zero units. No money moves and no units change
 * hands until they subscribe, so only `['pools']` is invalidated.
 */
export function useAddPoolParticipant(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: AddPoolParticipantRequest) =>
      apiClient.post<AddPoolParticipantResponse>(`/pools/${poolId}/participants`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: poolKeys.all });
    },
  });
}

/** Money in: mints units at the prevailing NAV and writes the Income leg. */
export function useRecordSubscription(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordSubscriptionRequest) =>
      apiClient.post<RecordSubscriptionResponse>(`/pools/${poolId}/subscriptions`, input),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Money out: burns units at the prevailing NAV and writes the Expense leg —
 * plus the receiving leg on `destinationAccountId` when one is given.
 */
export function useRecordRedemption(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordRedemptionRequest) =>
      apiClient.post<RecordRedemptionResponse>(`/pools/${poolId}/redemptions`, input),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Closes the month: marks the pool, retires units at that NAV, records what is
 * owed. **No cash leg** — but the mark itself is a real row on the account, so
 * the full invalidation set still applies.
 */
export function useCloseDistribution(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CloseDistributionRequest) =>
      apiClient.post<CloseDistributionResponse>(`/pools/${poolId}/distributions`, input),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Pays a closed distribution out: writes the real transaction on the day the
 * transfer physically settled. This is the step that actually moves cash.
 */
export function useSettleDistribution(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: ({ eventId, ...input }: SettleDistributionRequest & { eventId: string }) =>
      apiClient.post<SettleDistributionResponse>(
        `/pools/${poolId}/distributions/${eventId}/settle`,
        input,
      ),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Records a pool cost the owner paid out of pocket as a pure unit transfer.
 * No cash leg — but supplying `poolValueNow` writes a mark first, so the
 * money-movement set applies here too.
 */
export function useRecordCostReimbursement(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordCostReimbursementRequest) =>
      apiClient.post<RecordCostReimbursementResponse>(
        `/pools/${poolId}/cost-reimbursements`,
        input,
      ),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Deletes a unit event (DELETE /pools/{id}/events/{eventId} → 204). The
 * backend soft-deletes any transaction the event synthesized, and removes the
 * whole same-day batch for a cost transfer — half of one would change total
 * units and NAV with it. Full invalidation set.
 */
export function useDeletePoolEvent(poolId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (eventId: string) => apiClient.delete<void>(`/pools/${poolId}/events/${eventId}`),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Soft-archives a pool (POST /pools/{id}/archive → 204, idempotent). Rejected
 * by the backend while any non-owner units are outstanding — the owner
 * fraction would revert to 1.0 and the outside stake would be silently
 * reabsorbed.
 *
 * Archiving flips `isPooled` on the account and restores its full balance to
 * net worth, so this is not a metadata-only change — the wide set applies.
 *
 * Reversible via {@link useUnarchivePool}. It is **not** the way to get rid of
 * a pool created by mistake — that is {@link useDeletePool}.
 */
export function useArchivePool() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.post<void>(`/pools/${id}/archive`),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Puts an archived pool back in service (POST /pools/{id}/unarchive → 204,
 * idempotent). The mirror of `useArchivePool`, and it needs no mirror of that
 * route's outside-units check: the direction of travel is towards **more**
 * restriction, because an active pool re-arms every guard on the account
 * (manual transactions, transfers, imports and loan movements are refused
 * again).
 *
 * Same wide invalidation as archive — `isPooled` flips back on and the
 * net-worth seam re-reads the account through the owner fraction.
 */
export function useUnarchivePool() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.post<void>(`/pools/${id}/unarchive`),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}

/**
 * Hard-deletes a pool with its participants and unit events
 * (DELETE /pools/{id} → 204). Works on archived rows too — those are the stuck
 * ones, since an archived pool still holds its account through the `RESTRICT`
 * FK and the unfiltered unique index on `account_id`.
 *
 * Refused with **409 `pools.delete_has_movements`** the moment the pool holds
 * non-owner units or carries an event with cash or a linked transaction. That
 * is the ordinary answer for a pool that has been used, not a failure: such a
 * pool is wound down by redeeming and archiving. Callers surface it as
 * guidance — see `DeletePoolDialog`.
 *
 * **A 204 does not roll the account's history back.** The pool's catch-up
 * marks stay: they are real adjustment rows that moved the balance to what the
 * exchange was showing and may already have been reconciled against. Nothing
 * on the transactions side changes here — the wide set still applies because
 * `isPooled` flips off and the net-worth seam stops applying an owner fraction
 * to the account.
 */
export function useDeletePool() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/pools/${id}`),
    onSuccess: () => invalidatePoolMovement(queryClient),
  });
}
