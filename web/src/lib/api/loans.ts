'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  CreateLoanRequest,
  CreateLoanResponse,
  LoanDetailDto,
  LoanDto,
  RecordLoanPaymentRequest,
  RecordLoanPaymentResponse,
  UpdateLoanRequest,
} from '@/src/types/api';
import { apiClient } from './client';

export const loanKeys = {
  all: ['loans'] as const,
  list: (includeArchived: boolean) => [...loanKeys.all, { includeArchived }] as const,
  detail: (id: string) => [...loanKeys.all, 'detail', id] as const,
};

/**
 * Invalidation set for loan mutations that can move real money: creating
 * a loan with a linked account, recording a payment against an account,
 * and deleting a payment (which also deletes its linked transaction).
 *
 * Mirrors `useCreateTransfer` in ./transactions — loan-linked legs are
 * transfer-flagged transactions, so they shift account balances (which
 * feed the dashboard, balance-over-time reports, and linked-mode goals'
 * `saved`) and add rows to the transactions list. `['dashboard']` is
 * doubly load-bearing since the net-worth tiles landed: a repayment
 * shrinks both the account balance and the outstanding loan, and both
 * terms live in `GET /dashboard/net-worth`. Budgets exclude
 * transfer legs server-side but are invalidated defensively, same as the
 * transfer hook. Applied unconditionally — simplest correct approach —
 * so an unlinked mutation just triggers a few cheap no-op refetches.
 */
function invalidateMoneyMovement(queryClient: ReturnType<typeof useQueryClient>) {
  queryClient.invalidateQueries({ queryKey: loanKeys.all });
  queryClient.invalidateQueries({ queryKey: ['accounts'] });
  queryClient.invalidateQueries({ queryKey: ['transactions'] });
  queryClient.invalidateQueries({ queryKey: ['dashboard'] });
  queryClient.invalidateQueries({ queryKey: ['reports'] });
  queryClient.invalidateQueries({ queryKey: ['budgets'] });
  queryClient.invalidateQueries({ queryKey: ['goals'] });
}

/**
 * Lists loans — non-archived only by default; pass `includeArchived: true`
 * to fetch archived rows too (the "Show archived" toggle). Server rolls up
 * `totalRepaid`, `outstanding`, `outstandingMdl`, and `status` per row.
 *
 * Keyed as `['loans', { includeArchived }]` — mirrors `useAccounts`, so
 * both variants sit under the `['loans']` prefix and every mutation that
 * invalidates `loanKeys.all` refreshes them via prefix invalidation.
 */
export function useLoans(includeArchived = false) {
  return useQuery({
    queryKey: loanKeys.list(includeArchived),
    queryFn: () => apiClient.get<LoanDto[]>(`/loans?includeArchived=${includeArchived}`),
  });
}

/**
 * GET /loans/{id} — fetches the rich per-loan detail DTO used by the loan
 * detail page (payment history, disbursement link, archive metadata).
 * Disabled until a non-empty id is supplied so the hook is safe to call
 * from a Client Component reading its id from the route params.
 *
 * Cache key is rooted at `['loans', 'detail', id]`, which sits under the
 * `['loans']` prefix — every mutation in this module invalidates
 * `loanKeys.all`, so the detail picks up refreshes for free via TanStack
 * Query's prefix invalidation. Archived loans still resolve here (they
 * only drop off the list endpoint).
 */
export function useLoanDetail(id: string) {
  return useQuery({
    queryKey: loanKeys.detail(id),
    queryFn: () => apiClient.get<LoanDetailDto>(`/loans/${id}`),
    enabled: Boolean(id),
  });
}

/**
 * Creates a loan. When `accountId` is set the backend also writes a real
 * transfer-flagged transaction on that account, so the full
 * money-movement invalidation set applies.
 */
export function useCreateLoan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateLoanRequest) => apiClient.post<CreateLoanResponse>('/loans', input),
    onSuccess: () => invalidateMoneyMovement(queryClient),
  });
}

/**
 * Edits a loan's user-mutable metadata (`counterparty` + `notes`).
 * Purely descriptive — no balances shift, so only `['loans']` refreshes.
 */
export function useUpdateLoan(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateLoanRequest) => apiClient.put<void>(`/loans/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: loanKeys.all });
    },
  });
}

/**
 * Soft-archives a loan (DELETE /loans/{id} → 204). Archiving hides the
 * loan from the list but keeps its transactions and payment history —
 * nothing money-side changes, so only `['loans']` is invalidated.
 *
 * Not even the net-worth tiles: `GET /dashboard/net-worth` deliberately
 * counts archived loans (a debt hidden from the UI is still a debt), so
 * the dashboard is unmoved by archiving. This is the one place where the
 * /loans summary tiles and the dashboard are allowed to disagree.
 */
export function useArchiveLoan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/loans/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: loanKeys.all });
    },
  });
}

/**
 * Un-archives a loan (POST /loans/{id}/unarchive → 204, idempotent). The
 * reverse of `useArchiveLoan` — no money moves, so only `['loans']` is
 * invalidated; both list variants and the detail cache live under that
 * prefix. Mirrors `useUnarchiveAccount`.
 */
export function useUnarchiveLoan() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.post<void>(`/loans/${id}/unarchive`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: loanKeys.all });
    },
  });
}

/**
 * Records a repayment. When `accountId` is set the backend also writes a
 * transfer-flagged transaction on that account, so the full
 * money-movement invalidation set applies.
 */
export function useRecordLoanPayment(loanId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RecordLoanPaymentRequest) =>
      apiClient.post<RecordLoanPaymentResponse>(`/loans/${loanId}/payments`, input),
    onSuccess: () => invalidateMoneyMovement(queryClient),
  });
}

/**
 * Deletes a payment. When the payment was linked to an account the
 * backend also deletes that transaction, so the full money-movement
 * invalidation set applies.
 */
export function useDeleteLoanPayment(loanId: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (paymentId: string) =>
      apiClient.delete<void>(`/loans/${loanId}/payments/${paymentId}`),
    onSuccess: () => invalidateMoneyMovement(queryClient),
  });
}
