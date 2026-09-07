import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  poolKeys,
  useAddPoolParticipant,
  useArchivePool,
  useCloseDistribution,
  useCreatePool,
  useDeletePool,
  useDeletePoolEvent,
  usePoolDetail,
  usePools,
  useRecordCostReimbursement,
  useRecordRedemption,
  useRecordSubscription,
  useSettleDistribution,
  useUnarchivePool,
} from '@/src/lib/api/pools';
import { server } from '@/src/lib/mocks/server';

function createClient() {
  return new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
}

function wrapperFor(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

// Structural spy shape — same helper the loans hook suite uses, for the same
// reason (vi.spyOn's MockInstance generic is invariant in the spied signature).
function invalidatedKeys(spy: { mock: { calls: unknown[][] } }): string[] {
  return spy.mock.calls.map((call) => {
    const filters = call[0] as { queryKey?: readonly unknown[] } | undefined;
    return JSON.stringify(filters?.queryKey);
  });
}

const MONEY_MOVEMENT_KEYS = [
  ['pools'],
  ['accounts'],
  ['transactions'],
  ['dashboard'],
  ['reports'],
  ['budgets'],
  ['goals'],
].map((k) => JSON.stringify(k));

const POOL_ID = 'aaaa0001-0000-4000-8000-000000000001';

describe('pool query keys', () => {
  it('roots the detail key under the list prefix', () => {
    expect(poolKeys.all).toEqual(['pools']);
    expect(poolKeys.list(false)).toEqual(['pools', { includeArchived: false }]);
    expect(poolKeys.list(true)).toEqual(['pools', { includeArchived: true }]);
    expect(poolKeys.detail('x')).toEqual(['pools', 'detail', 'x']);
  });
});

describe('pool query hooks', () => {
  it('usePools fetches only non-archived pools by default', async () => {
    const { result } = renderHook(() => usePools(), { wrapper: wrapperFor(createClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.length).toBe(1);
    expect(result.current.data?.[0]?.name).toBe('Binance pool');
  });

  it('usePools includes archived pools when asked', async () => {
    const { result } = renderHook(() => usePools(true), { wrapper: wrapperFor(createClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.length).toBe(2);
  });

  it('usePoolDetail fetches the roster, ledger and reconciliation', async () => {
    const { result } = renderHook(() => usePoolDetail(POOL_ID), {
      wrapper: wrapperFor(createClient()),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.participants.length).toBe(3);
    expect(result.current.data?.events.length).toBe(4);
    expect(result.current.data?.reconciliation.isClean).toBe(true);
  });

  it('usePoolDetail stays disabled without an id', () => {
    const { result } = renderHook(() => usePoolDetail(''), {
      wrapper: wrapperFor(createClient()),
    });
    expect(result.current.fetchStatus).toBe('idle');
  });
});

describe('pool mutation invalidation sets', () => {
  it('useCreatePool invalidates the full money-movement set', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useCreatePool(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync({
      accountId: '66666666-6666-6666-6666-666666666666',
      name: 'Binance pool',
      currency: 'USD',
      inceptionDate: '2026-08-01',
      ownerName: 'Me',
      poolValueAtInception: 1500,
    });

    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useAddPoolParticipant invalidates only the pools root (no money, no shares)', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useAddPoolParticipant(POOL_ID), {
      wrapper: wrapperFor(client),
    });

    await result.current.mutateAsync({ name: 'Andrei', joinedOn: '2026-08-05' });

    expect(invalidatedKeys(spy)).toEqual([JSON.stringify(['pools'])]);
  });

  it('useRecordSubscription invalidates the full money-movement set', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useRecordSubscription(POOL_ID), {
      wrapper: wrapperFor(client),
    });

    await result.current.mutateAsync({
      participantId: 'bbbb0001-0000-4000-8000-000000000002',
      poolValueNow: 2500,
      cash: 1000,
    });

    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useRecordRedemption posts to the redemptions route', async () => {
    let seen: unknown = null;
    server.use(
      http.post('*/pools/:id/redemptions', async ({ request }) => {
        seen = await request.json();
        return HttpResponse.json(
          {
            eventId: 'e',
            units: 1,
            navPerUnit: 1,
            poolValuePreMoney: 1,
            markDelta: 0,
            markTransactionId: null,
            movementTransactionId: 'tx',
            counterTransactionId: null,
          },
          { status: 201 },
        );
      }),
    );

    const { result } = renderHook(() => useRecordRedemption(POOL_ID), {
      wrapper: wrapperFor(createClient()),
    });
    await result.current.mutateAsync({
      participantId: 'bbbb0001-0000-4000-8000-000000000001',
      poolValueNow: 2500,
      cash: 120,
      destinationAccountId: null,
      isFullWindDown: false,
    });

    expect(seen).toMatchObject({ cash: 120, poolValueNow: 2500, isFullWindDown: false });
  });

  it('useCloseDistribution sends a null payout list when nobody is excluded', async () => {
    let seen: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions', async ({ request }) => {
        seen = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json(
          {
            navPerUnit: 1.25,
            poolValuePreMoney: 2500,
            markDelta: 0,
            markTransactionId: null,
            totalCash: 100,
            lines: [],
          },
          { status: 201 },
        );
      }),
    );

    const { result } = renderHook(() => useCloseDistribution(POOL_ID), {
      wrapper: wrapperFor(createClient()),
    });
    await result.current.mutateAsync({ poolValueNow: 2600, payouts: null });

    expect(seen).not.toBeNull();
    expect((seen as unknown as Record<string, unknown>).payouts).toBeNull();
  });

  it('useSettleDistribution targets the per-event settle route and keeps the id out of the body', async () => {
    let url = '';
    let seen: Record<string, unknown> | null = null;
    server.use(
      http.post('*/pools/:id/distributions/:eventId/settle', async ({ request }) => {
        url = request.url;
        seen = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({
          transactionId: 'tx',
          settledOn: '2026-09-03',
          cash: 100,
        });
      }),
    );

    const { result } = renderHook(() => useSettleDistribution(POOL_ID), {
      wrapper: wrapperFor(createClient()),
    });
    await result.current.mutateAsync({ eventId: 'evt-1', settledOn: '2026-09-02', notes: null });

    expect(url).toContain(`/pools/${POOL_ID}/distributions/evt-1/settle`);
    expect(seen).toEqual({ settledOn: '2026-09-02', notes: null });
  });

  it('useRecordCostReimbursement invalidates the full set (a supplied total writes a mark)', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useRecordCostReimbursement(POOL_ID), {
      wrapper: wrapperFor(client),
    });

    await result.current.mutateAsync({ amount: 20, poolValueNow: 2600 });

    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useDeletePoolEvent hits the per-event endpoint and invalidates the full set', async () => {
    let url = '';
    server.use(
      http.delete('*/pools/:id/events/:eventId', ({ request }) => {
        url = request.url;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useDeletePoolEvent(POOL_ID), {
      wrapper: wrapperFor(client),
    });
    await result.current.mutateAsync('evt-9');

    expect(url).toContain(`/pools/${POOL_ID}/events/evt-9`);
    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useArchivePool invalidates the full set — the account goes back to counting in full', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useArchivePool(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync(POOL_ID);

    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useUnarchivePool posts to the unarchive route and invalidates the full set', async () => {
    let url = '';
    let method = '';
    server.use(
      http.post('*/pools/:id/unarchive', ({ request }) => {
        url = request.url;
        method = request.method;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useUnarchivePool(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync(POOL_ID);

    expect(method).toBe('POST');
    expect(url).toContain(`/pools/${POOL_ID}/unarchive`);
    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useDeletePool hits DELETE /pools/{id} and invalidates the full set', async () => {
    let url = '';
    let method = '';
    server.use(
      http.delete('*/pools/:id', ({ request }) => {
        url = request.url;
        method = request.method;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useDeletePool(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync(POOL_ID);

    expect(method).toBe('DELETE');
    expect(url).toMatch(new RegExp(`/pools/${POOL_ID}$`));
    const keys = invalidatedKeys(spy);
    for (const expected of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(expected);
    }
  });

  it('useDeletePool surfaces the has-movements refusal to the caller', async () => {
    server.use(
      http.delete('*/pools/:id', () =>
        HttpResponse.json(
          { errorCode: 'pools.delete_has_movements', detail: 'Archive it instead.' },
          { status: 409 },
        ),
      ),
    );

    const client = createClient();
    const { result } = renderHook(() => useDeletePool(), { wrapper: wrapperFor(client) });

    await expect(result.current.mutateAsync(POOL_ID)).rejects.toMatchObject({
      status: 409,
      message: 'Archive it instead.',
    });
  });
});
