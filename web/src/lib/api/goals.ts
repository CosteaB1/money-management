'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  CreateGoalRequest,
  CreateGoalResponse,
  GoalDetailDto,
  GoalDto,
  UpdateGoalRequest,
  UpdateManualSavedRequest,
} from '@/src/types/api';
import { apiClient } from './client';

export const goalKeys = {
  all: ['goals'] as const,
  list: () => [...goalKeys.all] as const,
  detail: (id: string) => [...goalKeys.all, 'detail', id] as const,
};

/**
 * Lists all non-archived savings goals. Server rolls up `saved`,
 * `remaining`, and `progressPercent` per row — for linked-mode goals
 * `saved` reflects the linked account's live MDL-equivalent balance, so
 * the hook needs to invalidate `['goals']` after any mutation that
 * changes account balances (transactions, transfers, adjustments,
 * imports).
 */
export function useGoals() {
  return useQuery({
    queryKey: goalKeys.list(),
    queryFn: () => apiClient.get<GoalDto[]>('/goals'),
  });
}

/**
 * GET /goals/{id} — fetches the rich per-goal detail DTO used by the goal
 * detail page (progress, pace stats, saved-over-time history, contribution
 * list). Disabled until a non-empty id is supplied so the hook is safe to
 * call from a Client Component reading its id from the route params.
 *
 * Cache key is rooted at `['goals', 'detail', id]`, which sits under the
 * `['goals']` prefix — every existing mutation in this module
 * (`useCreateGoal`, `useUpdateGoal`, `useUpdateManualSaved`,
 * `useArchiveGoal`) already invalidates `goalKeys.all`, so the detail
 * picks up refreshes for free via TanStack Query's prefix invalidation.
 * `useAdjustBalance` in `./accounts` likewise invalidates `['goals']` so
 * a linked-mode goal's `saved` figure stays current after a balance
 * adjustment on the underlying account.
 */
export function useGoalDetail(id: string) {
  return useQuery({
    queryKey: goalKeys.detail(id),
    queryFn: () => apiClient.get<GoalDetailDto>(`/goals/${id}`),
    enabled: Boolean(id),
  });
}

export function useCreateGoal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateGoalRequest) => apiClient.post<CreateGoalResponse>('/goals', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: goalKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

/**
 * Full replace of a goal (lets the user toggle between linked and manual
 * mode by setting/clearing `linkedAccountId`).
 */
export function useUpdateGoal(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateGoalRequest) => apiClient.put<void>(`/goals/${id}`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: goalKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

/**
 * Sets the `saved` figure on a manual-mode goal. Backend rejects with
 * 400 if the goal is in linked mode — callers should gate the UI to
 * manual goals to keep this clean.
 */
export function useUpdateManualSaved(id: string) {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: UpdateManualSavedRequest) =>
      apiClient.patch<void>(`/goals/${id}/manual-saved`, input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: goalKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}

/**
 * Soft-archives a goal. Backend returns 204 — no body to thread through
 * to the caller.
 */
export function useArchiveGoal() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/goals/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: goalKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
    },
  });
}
