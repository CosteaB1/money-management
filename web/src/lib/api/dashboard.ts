'use client';

import { useQuery } from '@tanstack/react-query';
import type { DashboardSummaryDto, NetWorthTrendPointDto } from '@/src/types/api';
import { apiClient } from './client';

/**
 * Query keys for dashboard endpoints.
 *
 * IMPORTANT: every key starts with the literal `'dashboard'` so that the
 * existing invalidations in `useCreateTransaction`, `useAdjustBalance`, and
 * `useImportCommit` — which all call
 * `invalidateQueries({ queryKey: ['dashboard'] })` — cascade to the
 * summary and trend hooks defined here. Do NOT change the root prefix
 * without updating those mutations in lockstep.
 */
export const dashboardKeys = {
  all: ['dashboard'] as const,
  summary: (month?: string) => ['dashboard', 'summary', month ?? 'current'] as const,
  trend: (months: number) => ['dashboard', 'net-worth-trend', months] as const,
};

/**
 * GET /dashboard/summary?month=YYYY-MM
 *
 * Pass `undefined` (the default) to let the backend resolve to the current
 * UTC month. Returns income/expense/net MDL totals plus a savings rate and
 * a `missingFxRate` flag when any in-window transaction was unconvertible.
 */
export function useDashboardSummary(month?: string) {
  return useQuery({
    queryKey: dashboardKeys.summary(month),
    queryFn: () =>
      apiClient.get<DashboardSummaryDto>(
        month ? `/dashboard/summary?month=${encodeURIComponent(month)}` : '/dashboard/summary',
      ),
  });
}

/**
 * GET /dashboard/net-worth-trend?months=N
 *
 * Default 6, valid range [1, 24]. The returned array is **oldest first**;
 * past months use end-of-month as-of dates and the final point uses "today",
 * so a 6-month series yields 5 month-end snapshots followed by a live point.
 */
export function useNetWorthTrend(months = 6) {
  return useQuery({
    queryKey: dashboardKeys.trend(months),
    queryFn: () =>
      apiClient.get<NetWorthTrendPointDto[]>(`/dashboard/net-worth-trend?months=${months}`),
  });
}
