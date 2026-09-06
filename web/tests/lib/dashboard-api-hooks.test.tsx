import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it } from 'vitest';
import { dashboardKeys, useNetWorth } from '@/src/lib/api/dashboard';
import { server } from '@/src/lib/mocks/server';

function createClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false } } });
}

function wrapperFor(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

describe('dashboard query keys', () => {
  // Load-bearing: every mutation that moves money invalidates the bare
  // `['dashboard']` root (transactions, balance adjustments, imports,
  // loans). A net-worth key that didn't start with that literal would
  // leave the three headline tiles frozen after a repayment.
  it('roots the net-worth key under the shared dashboard prefix', () => {
    expect(dashboardKeys.netWorth()).toEqual(['dashboard', 'net-worth']);
    expect(dashboardKeys.netWorth()[0]).toBe(dashboardKeys.all[0]);
  });

  it('keeps the net-worth key distinct from the trend key', () => {
    expect(dashboardKeys.netWorth()).not.toEqual(dashboardKeys.trend(6));
  });
});

describe('useNetWorth', () => {
  it('fetches the seeded net-worth figures', async () => {
    const { result } = renderHook(() => useNetWorth(), { wrapper: wrapperFor(createClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));

    const data = result.current.data;
    expect(data?.grossAssetsMdl).toBe(48100);
    expect(data?.externalLiabilitiesMdl).toBe(57600);
    expect(data?.externalAssetsMdl).toBe(2000);
    expect(data?.netWorthMdl).toBe(
      (data?.grossAssetsMdl ?? 0) -
        (data?.externalLiabilitiesMdl ?? 0) +
        (data?.externalAssetsMdl ?? 0),
    );
  });

  it('refetches when a mutation invalidates the bare dashboard root', async () => {
    let requests = 0;
    server.use(
      http.get('*/dashboard/net-worth', () => {
        requests += 1;
        return HttpResponse.json({
          grossAssetsMdl: 100,
          externalLiabilitiesMdl: 40,
          externalAssetsMdl: 0,
          netWorthMdl: 60,
          accountsMissingFxRate: 0,
          loansMissingFxRate: 0,
        });
      }),
    );

    const client = createClient();
    const { result } = renderHook(() => useNetWorth(), { wrapper: wrapperFor(client) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(requests).toBe(1);

    // Exactly what useCreateTransaction / useAdjustBalance / useImportCommit
    // and the loan money-movement hooks fire.
    client.invalidateQueries({ queryKey: ['dashboard'] });
    await waitFor(() => expect(requests).toBe(2));
  });
});
