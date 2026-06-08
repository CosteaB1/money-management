'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  AccountDetailDto,
  AccountDto,
  BalanceChangeRequest,
  BalanceChangeResponse,
  CreateAccountRequest,
  CreateAccountResponse,
  UpdateAccountRequest,
} from '@/src/types/api';
import { apiClient } from './client';

export const accountKeys = {
  all: ['accounts'] as const,
  list: (includeArchived: boolean) => [...accountKeys.all, { includeArchived }] as const,
  detail: (id: string) => [...accountKeys.all, 'detail', id] as const,
};

export function useAccounts(includeArchived = false) {
  return useQuery({
    queryKey: accountKeys.list(includeArchived),
    queryFn: () => apiClient.get<AccountDto[]>(`/accounts?includeArchived=${includeArchived}`),
  });
}

/**
 * GET /accounts/{id} — fetches the rich per-account detail DTO used by the
 * account detail page. Disabled until a non-empty id is supplied, so the
 * hook is safe to call from a Client Component that reads its id from the
 * route params.
 *
 * Cache key is rooted at `['accounts', 'detail', id]`, which sits under the
 * `['accounts']` prefix — any mutation that already invalidates
 * `accountKeys.all` (create / archive / adjust-balance / transfers /
 * generic transaction CUD) picks this up for free via TanStack Query's
 * prefix invalidation.
 */
export function useAccountDetail(id: string) {
  return useQuery({
    queryKey: accountKeys.detail(id),
    queryFn: () => apiClient.get<AccountDetailDto>(`/accounts/${id}`),
    enabled: Boolean(id),
  });
}

export function useCreateAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateAccountRequest) =>
      apiClient.post<CreateAccountResponse>('/accounts', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
    },
  });
}

/**
 * PUT /accounts/{id} → 204 No Content. Edits the account's user-mutable
 * metadata — `name` and `notes` only (currency and type are fixed at
 * creation and not part of the contract). Returns 404 for a missing id and
 * 400 ProblemDetails on a validation error, both surfaced as a thrown
 * `ApiError` whose `.message` carries the backend's human-readable detail —
 * callers should catch and show `err.message`.
 *
 * On success invalidates `accountKeys.all` (list + detail under the same
 * prefix) and `['dashboard']`, since the renamed account is shown on the
 * dashboard account cards.
 */
export function useUpdateAccount(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateAccountRequest) => apiClient.put<void>(`/accounts/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useArchiveAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/accounts/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
    },
  });
}

/**
 * POST /accounts/{id}/unarchive → 204 No Content. The reverse of
 * `useArchiveAccount`; takes no request body. Invalidates `accountKeys.all`
 * so both the list (any `includeArchived` variant) and the detail cache
 * (rooted under the same prefix) re-fetch the now-active account.
 */
export function useUnarchiveAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.post<void>(`/accounts/${id}/unarchive`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
    },
  });
}

/**
 * DELETE /accounts/{id}/permanent → 204 No Content on success.
 *
 * The hard counterpart to `useArchiveAccount`. The backend only allows this
 * when the account has no history: it returns **409 Conflict** (which
 * `apiClient` surfaces as a thrown `ApiError`) when the account still has
 * linked transactions, imports, or goals — callers should catch and show
 * `err.message`, which carries the backend's human-readable detail. A
 * missing id yields 404.
 *
 * On success invalidates `accountKeys.all` (list + detail under the same
 * prefix) and `['dashboard']` so the dashboard account cards drop the now
 * non-existent account.
 */
export function useDeleteAccount() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/accounts/${id}/permanent`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

/**
 * Records a balance change on an account — backend writes a single synthetic
 * income-or-expense transaction. The `kind` discriminator selects the
 * semantics (see `BalanceChangeKind`):
 *   - `Investment`/`Withdrawal` → `value` is the positive amount moved.
 *   - `Adjustment` → `value` is the new total balance; delta is the P&L.
 *
 * Allowed only on account types where balances drift independently of
 * transactions (Brokerage, CryptoExchange, P2PLending, BankDeposit). The
 * caller is responsible for gating the UI by `account.type`.
 *
 * Invalidates the accounts list (balance changed), the transactions list
 * (a new row exists), and any dashboard aggregates that depend on either.
 */
export function useAdjustBalance(accountId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: BalanceChangeRequest) =>
      apiClient.post<BalanceChangeResponse>(`/accounts/${accountId}/balance-changes`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['transactions'] });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      // Balance-over-time chart + monthly summaries are reports-rooted, not
      // under ['accounts'] — invalidate them so the detail page chart refreshes.
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      // Balance-change rows are excluded from budget spend server-side, but
      // invalidate defensively so we pick up any cascading recompute on the
      // budgets list.
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      // Balance changes move the account balance → linked goals' `saved`.
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}
