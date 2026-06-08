'use client';

import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import type {
  BackfillBnmRatesRequest,
  BackfillBnmRatesResponse,
  ConvertFxResponse,
  CreateFxRateRequest,
  CreateFxRateResponse,
  FxRateDto,
  PagedResult,
  RefreshBnmRatesRequest,
  RefreshBnmRatesResponse,
} from '@/src/types/api';
import { accountKeys } from './accounts';
import { apiClient } from './client';

export const fxRateKeys = {
  all: ['fx-rates'] as const,
  list: (page: number, pageSize: number) => [...fxRateKeys.all, 'list', page, pageSize] as const,
};

export interface ConvertFxInput {
  /** Source currency ISO code (e.g. "MDL"). */
  from: string;
  /** Destination currency ISO code (e.g. "USD"). */
  to: string;
  /** ISO date string (yyyy-MM-dd) the rate is looked up against. */
  date: string;
  /** Amount in `from` currency to convert into `to`. */
  amount: number;
}

/**
 * Imperative FX conversion against `GET /fx-rates/convert`. Deliberately NOT a
 * hook: the import preview converts an arbitrary number of rows on demand (when
 * the user picks a different-currency counter account), so a per-row `useQuery`
 * would be unwieldy. Callers invoke this directly and seed local state from the
 * result. Reuses `apiClient.get`, so 4xx/5xx surface as `ApiError` like the rest
 * of this module. When no rate exists, the backend returns
 * `{ convertedAmount: null, rate: null, hasRate: false }`.
 */
export function convertFx({ from, to, date, amount }: ConvertFxInput): Promise<ConvertFxResponse> {
  const params = new URLSearchParams({
    from,
    to,
    date,
    amount: String(amount),
  });
  return apiClient.get<ConvertFxResponse>(`/fx-rates/convert?${params.toString()}`);
}

export function useFxRates(page: number, pageSize: number) {
  return useQuery({
    queryKey: fxRateKeys.list(page, pageSize),
    queryFn: () =>
      apiClient.get<PagedResult<FxRateDto>>(`/fx-rates?page=${page}&pageSize=${pageSize}`),
  });
}

export function useCreateFxRate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: CreateFxRateRequest) =>
      apiClient.post<CreateFxRateResponse>('/fx-rates', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: fxRateKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
    },
  });
}

export function useDeleteFxRate() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (id: string) => apiClient.delete<void>(`/fx-rates/${id}`),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: fxRateKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
    },
  });
}

/**
 * Triggers a synchronous BNM refresh on the backend. The call can take
 * up to ~10s while BNM responds — we deliberately don't add a timeout
 * here; let TanStack Query manage it via the mutation's abort signal.
 *
 * Refreshing rates ripples through every MDL-converted value in the app
 * (account balance MDL-eq, transaction MDL-eq, dashboard aggregates,
 * linked-mode goal `saved`), so we invalidate four query roots:
 *  - `['fx-rates']`  — the table itself
 *  - `['accounts']`  — `balanceMdl` recomputes server-side
 *  - `['dashboard']` — summary + net-worth trend recompute
 *  - `['goals']`     — linked-mode `saved` recomputes
 */
export function useRefreshBnmRates() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: RefreshBnmRatesRequest) =>
      apiClient.post<RefreshBnmRatesResponse>('/fx-rates/refresh', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: fxRateKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}

/**
 * Triggers a historical BNM backfill — the backend loops every business day
 * in `[from, to]` (where `to` defaults to today), so this can take up to a
 * minute for a wide range. We deliberately don't impose a client-side
 * timeout; TanStack Query manages the in-flight mutation.
 *
 * Like `useRefreshBnmRates`, a backfill rewrites MDL-converted values across
 * the app, so on success we invalidate the same four query roots:
 *  - `['fx-rates']`  — the table itself
 *  - `['accounts']`  — `balanceMdl` recomputes server-side
 *  - `['dashboard']` — summary + net-worth trend recompute
 *  - `['goals']`     — linked-mode `saved` recomputes
 *
 * A bad range (future start / end-before-start / span > ~2 years) comes back
 * as a 400 ProblemDetails; `apiClient` surfaces the `detail` as the thrown
 * `ApiError.message`, so callers can show it verbatim.
 */
export function useBackfillBnmRates() {
  const queryClient = useQueryClient();
  return useMutation({
    mutationFn: (input: BackfillBnmRatesRequest) =>
      apiClient.post<BackfillBnmRatesResponse>('/fx-rates/backfill', input),
    onSuccess: () => {
      queryClient.invalidateQueries({ queryKey: fxRateKeys.all });
      queryClient.invalidateQueries({ queryKey: accountKeys.all });
      queryClient.invalidateQueries({ queryKey: ['dashboard'] });
      queryClient.invalidateQueries({ queryKey: ['goals'] });
    },
  });
}
