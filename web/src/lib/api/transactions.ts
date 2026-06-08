'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  CreateTransactionRequest,
  CreateTransactionResponse,
  CreateTransferRequest,
  CreateTransferResponse,
  PagedResult,
  TransactionDirection,
  TransactionDto,
  UpdateTransactionCategoryRequest,
  UpdateTransactionNotesRequest,
} from '@/src/types/api';
import { accountKeys } from './accounts';
import { apiClient } from './client';

export interface TransactionFilters {
  accountId?: string;
  from?: string;
  to?: string;
  categoryIds?: string[];
  direction?: TransactionDirection;
  /**
   * Tri-state transfer filter.
   * - `true`  → only internal-transfer rows
   * - `false` → exclude transfers
   * - `undefined` → no filter (include both)
   */
  isTransfer?: boolean;
  /**
   * Tri-state adjustment filter — same shape as `isTransfer`.
   * - `true`  → only balance-adjustment rows
   * - `false` → exclude adjustments
   * - `undefined` → no filter
   */
  isAdjustment?: boolean;
}

export const transactionKeys = {
  all: ['transactions'] as const,
  list: (filters: TransactionFilters, page: number) =>
    [...transactionKeys.all, 'list', filters, page] as const,
};

function buildQuery(filters: TransactionFilters, page: number, pageSize: number): string {
  const params = new URLSearchParams();
  if (filters.accountId) params.set('accountId', filters.accountId);
  if (filters.from) params.set('from', filters.from);
  if (filters.to) params.set('to', filters.to);
  if (filters.direction) params.set('direction', filters.direction);
  if (filters.categoryIds && filters.categoryIds.length > 0) {
    for (const id of filters.categoryIds) params.append('categoryId', id);
  }
  if (filters.isTransfer !== undefined) {
    params.set('isTransfer', String(filters.isTransfer));
  }
  if (filters.isAdjustment !== undefined) {
    params.set('isAdjustment', String(filters.isAdjustment));
  }
  params.set('page', String(page));
  params.set('pageSize', String(pageSize));
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

export function useTransactions(filters: TransactionFilters, page: number, pageSize: number) {
  return useQuery({
    queryKey: transactionKeys.list(filters, page),
    queryFn: () =>
      apiClient.get<PagedResult<TransactionDto>>(
        `/transactions${buildQuery(filters, page, pageSize)}`,
      ),
  });
}

export function useCreateTransaction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateTransactionRequest) =>
      apiClient.post<CreateTransactionResponse>('/transactions', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: transactionKeys.all });
      // A new row shifts the owning account's balance + detail Performance card.
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      // Reports (balance-over-time, monthly summary, breakdowns) are derived
      // from transactions and live under their own ['reports'] root.
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      // A new expense row may shift a budget's `spent` aggregate.
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      // Account balance changes feed linked-mode goals' `saved`.
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}

export function useDeleteTransaction() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/transactions/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: transactionKeys.all });
      // Deleting a row shifts the owning account's balance + Performance card,
      // so invalidate `accountKeys.all` (prefix-covers both the list and the
      // per-account detail query) the same way create/adjust-balance do.
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}

/**
 * Re-categorizes a single transaction (or clears its category when
 * `categoryId` is null). Mirrors the per-row inline Select in the
 * transactions table. The backend returns 204, or 400 when the chosen
 * category's flow is incompatible with the row's direction — that detail is
 * surfaced verbatim to the caller for a toast.
 *
 * onSuccess invalidates every aggregate a re-categorization can shift:
 * the transactions list itself, account balances/detail (`accountKeys.all`
 * prefix-covers both), the dashboard, budgets (an expense moving in/out of a
 * budgeted category changes `spent`), reports (category breakdowns), and
 * goals (linked-mode `saved` is derived from account balances).
 */
export function useUpdateTransactionCategory(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (categoryId: string | null) =>
      apiClient.put<void>(`/transactions/${id}/category`, {
        categoryId,
      } satisfies UpdateTransactionCategoryRequest),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: transactionKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}

/**
 * Updates (or clears) a single transaction's user-authored note. Sending
 * `null` or an empty/blank string clears the note. The backend returns 204.
 * Mirrors `useUpdateTransactionCategory` — same per-row inline mutation shape
 * and same query-key invalidation.
 *
 * A note is purely descriptive metadata: it never shifts a balance, a budget's
 * `spent`, a report aggregate, or a goal's `saved`. We therefore invalidate
 * only the transactions list so the edited row's note re-renders — there is no
 * derived aggregate to refresh.
 */
export function useUpdateTransactionNotes(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (notes: string | null) =>
      apiClient.put<void>(`/transactions/${id}/notes`, {
        notes,
      } satisfies UpdateTransactionNotesRequest),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: transactionKeys.all });
    },
  });
}

/**
 * Creates an internal-transfer pair (one debit on the source account,
 * one credit on the destination). Invalidates transactions + accounts
 * so the freshly debited/credited balances appear immediately.
 *
 * Transfer legs never count toward budget spend (they're filtered out
 * server-side by `isTransfer === true`), but we still invalidate
 * `['budgets']` defensively in case the user re-categorizes one of the
 * legs to a budgeted expense category before the next refetch.
 *
 * Note on the account detail page: invalidating `accountKeys.all` here is
 * deliberately a prefix-invalidation — `['accounts']` covers both the
 * list query (`[...all, { includeArchived }]`) and the per-account detail
 * query (`[...all, 'detail', id]`). The detail page therefore refreshes
 * its Performance card after a transfer or balance adjustment without us
 * having to thread the account id through the mutation.
 */
export function useCreateTransfer() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateTransferRequest) =>
      apiClient.post<CreateTransferResponse>('/transfers', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: transactionKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['reports'] });
      queryClient.invalidateQueries({ queryKey: ['budgets'] });
      // Transfer legs change account balances → linked-mode goals' `saved`.
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}
