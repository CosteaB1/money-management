'use client';

import { useQuery } from '@tanstack/react-query';
import type {
  BalanceOverTimeInterval,
  BalanceOverTimePoint,
  CategoryBreakdownDto,
  MonthlySummaryReportRow,
  ReportDirection,
  TopPayeeReportRow,
} from '@/src/types/api';
import { apiClient } from './client';

/**
 * Query keys for the Reports slice. Rooted at the literal `'reports'` so
 * any future mutation that needs to bust report caches can do a blanket
 * `invalidateQueries({ queryKey: ['reports'] })` and hit every sub-key.
 */
export const reportsKeys = {
  all: ['reports'] as const,
  monthlySummary: (params: MonthlySummaryParams) => ['reports', 'monthly-summary', params] as const,
  categoryBreakdown: (params: CategoryBreakdownParams) =>
    ['reports', 'category-breakdown', params] as const,
  topPayees: (params: TopPayeesParams) => ['reports', 'top-payees', params] as const,
  balanceOverTime: (params: BalanceOverTimeParams) =>
    ['reports', 'balance-over-time', params] as const,
};

export interface MonthlySummaryParams {
  /** "YYYY-MM"; omit both ends for the backend default (trailing 12 months). */
  from?: string;
  to?: string;
}

export interface CategoryBreakdownParams {
  /** ISO date string (yyyy-MM-dd) */
  from: string;
  to: string;
  direction: ReportDirection;
}

export interface TopPayeesParams {
  from: string;
  to: string;
  direction: ReportDirection;
  /** Defaults server-side to 10. */
  limit?: number;
}

export interface BalanceOverTimeParams {
  accountId: string;
  from: string;
  to: string;
  interval: BalanceOverTimeInterval;
}

function appendIfDefined(params: URLSearchParams, key: string, value: string | number | undefined) {
  if (value === undefined || value === null) return;
  params.append(key, String(value));
}

/**
 * GET /reports/monthly-summary?from=YYYY-MM&to=YYYY-MM
 *
 * Pass nothing to fall back to the backend default (trailing 12 months).
 * Response is oldest-first; income/expense exclude transfers & adjustments.
 */
export function useMonthlySummary(params: MonthlySummaryParams = {}) {
  const search = new URLSearchParams();
  appendIfDefined(search, 'from', params.from);
  appendIfDefined(search, 'to', params.to);
  const qs = search.toString();
  return useQuery({
    queryKey: reportsKeys.monthlySummary(params),
    queryFn: () =>
      apiClient.get<MonthlySummaryReportRow[]>(`/reports/monthly-summary${qs ? `?${qs}` : ''}`),
  });
}

/** GET /reports/category-breakdown — `items` are sorted desc by `amountMdl`. */
export function useCategoryBreakdown(params: CategoryBreakdownParams) {
  const search = new URLSearchParams({
    from: params.from,
    to: params.to,
    direction: params.direction,
  });
  return useQuery({
    queryKey: reportsKeys.categoryBreakdown(params),
    queryFn: () =>
      apiClient.get<CategoryBreakdownDto>(`/reports/category-breakdown?${search.toString()}`),
  });
}

/** GET /reports/top-payees — already sorted desc by `amountMdl`. */
export function useTopPayees(params: TopPayeesParams) {
  const search = new URLSearchParams({
    from: params.from,
    to: params.to,
    direction: params.direction,
  });
  appendIfDefined(search, 'limit', params.limit);
  return useQuery({
    queryKey: reportsKeys.topPayees(params),
    queryFn: () => apiClient.get<TopPayeeReportRow[]>(`/reports/top-payees?${search.toString()}`),
  });
}

/** GET /reports/balance-over-time — oldest first. */
export function useBalanceOverTime(params: BalanceOverTimeParams) {
  const search = new URLSearchParams({
    accountId: params.accountId,
    from: params.from,
    to: params.to,
    interval: params.interval,
  });
  return useQuery({
    queryKey: reportsKeys.balanceOverTime(params),
    queryFn: () =>
      apiClient.get<BalanceOverTimePoint[]>(`/reports/balance-over-time?${search.toString()}`),
    enabled: Boolean(params.accountId),
  });
}
