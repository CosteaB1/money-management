'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { toast } from 'sonner';
import type {
  BudgetDto,
  CreateBudgetRequest,
  CreateBudgetResponse,
  UpdateBudgetLimitRequest,
} from '@/src/types/api';
import { apiClient } from './client';

/**
 * Shape returned by `POST /budgets/rebuild-all-periods`. The backend
 * recomputes every budget's period rows from the underlying transactions
 * and reports how much it touched.
 */
interface RebuildBudgetPeriodsResponse {
  budgetsRebuilt: number;
  periodsAffected: number;
}

export const budgetKeys = {
  all: ['budgets'] as const,
  list: (year?: number, month?: number) => [...budgetKeys.all, { year, month }] as const,
};

function buildBudgetsQuery(year?: number, month?: number): string {
  const params = new URLSearchParams();
  if (year !== undefined) params.set('year', String(year));
  if (month !== undefined) params.set('month', String(month));
  const qs = params.toString();
  return qs ? `?${qs}` : '';
}

/**
 * Lists budgets for a given (year, month) window. When both args are
 * omitted, the backend defaults to the current UTC month.
 *
 * `spent` is rolled up server-side from the underlying transactions, so
 * the hook only needs to invalidate `['budgets']` after any mutation
 * that could shift category spend — transactions create/delete, imports
 * commit, transfers/adjustments, etc.
 */
export function useBudgets(year?: number, month?: number) {
  return useQuery({
    queryKey: budgetKeys.list(year, month),
    queryFn: () => apiClient.get<BudgetDto[]>(`/budgets${buildBudgetsQuery(year, month)}`),
  });
}

export function useCreateBudget() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateBudgetRequest) =>
      apiClient.post<CreateBudgetResponse>('/budgets', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: budgetKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

export function useUpdateBudgetLimit(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateBudgetLimitRequest) => apiClient.put<void>(`/budgets/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: budgetKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

/**
 * Recomputes every budget's period rows from the underlying transactions.
 * Needed when a budget is created over pre-existing transactions — without a
 * rebuild it shows `Spent 0` until new spend posts.
 *
 * No request body. Surfaces its own success/error toasts (no component-level
 * toast exists for this action) and invalidates `['budgets']` + `['dashboard']`
 * so the recomputed spend shows immediately.
 */
export function useRebuildBudgetPeriods() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: () => apiClient.post<RebuildBudgetPeriodsResponse>('/budgets/rebuild-all-periods'),
    onSuccess: (result) => {
      queryClient.invalidateQueries({ queryKey: budgetKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      toast.success(
        `Rebuilt ${result.budgetsRebuilt} budget${result.budgetsRebuilt === 1 ? '' : 's'} · ${result.periodsAffected} period${result.periodsAffected === 1 ? '' : 's'} updated`,
      );
    },
    onError: (err) => {
      toast.error(err instanceof Error ? err.message : 'Failed to rebuild budget periods');
    },
  });
}

/**
 * Soft-archives a budget. Backend returns 204 — no body to thread
 * through to the caller.
 */
export function useArchiveBudget() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/budgets/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: budgetKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}
