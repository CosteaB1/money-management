import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import {
  loanKeys,
  useArchiveLoan,
  useCreateLoan,
  useDeleteLoanPayment,
  useLoanDetail,
  useLoans,
  useRecordLoanPayment,
  useUpdateLoan,
} from '@/src/lib/api/loans';
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

// Structural spy shape — vi.spyOn's MockInstance generic is invariant in
// the spied signature, so accepting `{ mock: { calls } }` keeps the helper
// usable with the invalidateQueries spy without casts at every call site.
function invalidatedKeys(spy: { mock: { calls: unknown[][] } }): string[] {
  return spy.mock.calls.map((call) => {
    const filters = call[0] as { queryKey?: readonly unknown[] } | undefined;
    return JSON.stringify(filters?.queryKey);
  });
}

// Mutations that can move real money must refresh everything an account
// balance shift touches — mirrors useCreateTransfer's invalidation set.
const MONEY_MOVEMENT_KEYS = [
  ['loans'],
  ['accounts'],
  ['transactions'],
  ['dashboard'],
  ['reports'],
  ['budgets'],
  ['goals'],
].map((k) => JSON.stringify(k));

describe('loan query keys', () => {
  it('roots the detail key under the list prefix', () => {
    expect(loanKeys.all).toEqual(['loans']);
    expect(loanKeys.list(false)).toEqual(['loans', { includeArchived: false }]);
    expect(loanKeys.list(true)).toEqual(['loans', { includeArchived: true }]);
    expect(loanKeys.detail('x')).toEqual(['loans', 'detail', 'x']);
  });
});

describe('loan query hooks', () => {
  it('useLoans fetches the seeded list', async () => {
    const { result } = renderHook(() => useLoans(), { wrapper: wrapperFor(createClient()) });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.length).toBe(3);
    expect(result.current.data?.[0]?.counterparty).toBe('Parents');
  });

  it('useLoanDetail fetches the per-loan detail', async () => {
    const { result } = renderHook(() => useLoanDetail('l0000001-0000-0000-0000-000000000002'), {
      wrapper: wrapperFor(createClient()),
    });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(result.current.data?.disbursementAccountName).toBe('Cash Wallet');
    expect(result.current.data?.payments.length).toBe(1);
  });
});

describe('loan mutation invalidation sets', () => {
  it('useCreateLoan invalidates the full money-movement set', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useCreateLoan(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync({
      direction: 'Received',
      counterparty: 'Parents',
      principal: 5000,
      currency: 'EUR',
      loanDate: '2026-01-10',
      accountId: null,
      notes: null,
    });

    const keys = invalidatedKeys(spy);
    for (const key of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(key);
    }
  });

  it('useRecordLoanPayment invalidates the full money-movement set', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(
      () => useRecordLoanPayment('l0000001-0000-0000-0000-000000000001'),
      { wrapper: wrapperFor(client) },
    );

    await result.current.mutateAsync({
      amount: 500,
      occurredOn: '2026-08-01',
      accountId: null,
      notes: null,
    });

    const keys = invalidatedKeys(spy);
    for (const key of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(key);
    }
  });

  it('useDeleteLoanPayment hits the per-payment endpoint and invalidates the full set', async () => {
    let capturedUrl = '';
    server.use(
      http.delete('*/loans/:id/payments/:paymentId', ({ request }) => {
        capturedUrl = request.url;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(
      () => useDeleteLoanPayment('l0000001-0000-0000-0000-000000000001'),
      { wrapper: wrapperFor(client) },
    );

    await result.current.mutateAsync('lp000001-0000-0000-0000-000000000002');

    expect(capturedUrl).toContain(
      '/loans/l0000001-0000-0000-0000-000000000001/payments/lp000001-0000-0000-0000-000000000002',
    );
    const keys = invalidatedKeys(spy);
    for (const key of MONEY_MOVEMENT_KEYS) {
      expect(keys).toContain(key);
    }
  });

  it('useUpdateLoan invalidates only the loans root (metadata-only change)', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useUpdateLoan('l0000001-0000-0000-0000-000000000001'), {
      wrapper: wrapperFor(client),
    });

    await result.current.mutateAsync({ counterparty: 'Mom & Dad', notes: null });

    expect(invalidatedKeys(spy)).toEqual([JSON.stringify(['loans'])]);
  });

  it('useArchiveLoan invalidates only the loans root (nothing money-side changes)', async () => {
    const client = createClient();
    const spy = vi.spyOn(client, 'invalidateQueries');
    const { result } = renderHook(() => useArchiveLoan(), { wrapper: wrapperFor(client) });

    await result.current.mutateAsync('l0000001-0000-0000-0000-000000000001');

    expect(invalidatedKeys(spy)).toEqual([JSON.stringify(['loans'])]);
  });
});
