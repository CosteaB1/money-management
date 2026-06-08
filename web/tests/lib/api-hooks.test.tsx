import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { renderHook, waitFor } from '@testing-library/react';
import { HttpResponse, http } from 'msw';
import type { ReactNode } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { useArchiveAccount } from '@/src/lib/api/accounts';
import { useBudgets } from '@/src/lib/api/budgets';
import { useArchiveCategory, useDeleteCategory, useUpdateCategory } from '@/src/lib/api/categories';
import {
  useDeleteCategoryPattern,
  useUpdateCategoryPattern,
} from '@/src/lib/api/category-patterns';
import { ApiError, apiClient } from '@/src/lib/api/client';
import { downloadBackup, getBackupDownloadUrl } from '@/src/lib/api/data';
import { convertFx, useCreateFxRate, useDeleteFxRate } from '@/src/lib/api/fx-rates';
import { useDeleteTransaction, useTransactions } from '@/src/lib/api/transactions';
import { server } from '@/src/lib/mocks/server';

function wrapper() {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  );
}

describe('mutation hooks', () => {
  it('useUpdateCategory PUTs and resolves', async () => {
    const { result } = renderHook(() => useUpdateCategory('c1'), { wrapper: wrapper() });
    await result.current.mutateAsync({ name: 'Renamed', flow: 'Expense' });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it('useDeleteCategory + useArchiveCategory DELETE and resolve', async () => {
    const del = renderHook(() => useDeleteCategory(), { wrapper: wrapper() });
    await del.result.current.mutateAsync('c1');
    await waitFor(() => expect(del.result.current.isSuccess).toBe(true));

    const arch = renderHook(() => useArchiveCategory(), { wrapper: wrapper() });
    await arch.result.current.mutateAsync('c1');
    await waitFor(() => expect(arch.result.current.isSuccess).toBe(true));
  });

  it('useUpdateCategoryPattern + useDeleteCategoryPattern resolve', async () => {
    const upd = renderHook(() => useUpdateCategoryPattern('cp1'), { wrapper: wrapper() });
    await upd.result.current.mutateAsync({ keyword: 'NEW', categoryId: 'c1' });
    await waitFor(() => expect(upd.result.current.isSuccess).toBe(true));

    const del = renderHook(() => useDeleteCategoryPattern(), { wrapper: wrapper() });
    await del.result.current.mutateAsync('cp1');
    await waitFor(() => expect(del.result.current.isSuccess).toBe(true));
  });

  it('useArchiveAccount DELETEs and resolves', async () => {
    const { result } = renderHook(() => useArchiveAccount(), { wrapper: wrapper() });
    await result.current.mutateAsync('33333333-3333-3333-3333-333333333333');
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it('useDeleteTransaction DELETEs and resolves', async () => {
    const { result } = renderHook(() => useDeleteTransaction(), { wrapper: wrapper() });
    await result.current.mutateAsync('tx-coffee');
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
  });

  it('useCreateFxRate + useDeleteFxRate resolve', async () => {
    const create = renderHook(() => useCreateFxRate(), { wrapper: wrapper() });
    await create.result.current.mutateAsync({
      fromCurrency: 'USD',
      toCurrency: 'MDL',
      rate: 17.5,
      asOf: '2026-05-01',
    });
    await waitFor(() => expect(create.result.current.isSuccess).toBe(true));

    const del = renderHook(() => useDeleteFxRate(), { wrapper: wrapper() });
    await del.result.current.mutateAsync('f0000001-0000-0000-0000-000000000001');
    await waitFor(() => expect(del.result.current.isSuccess).toBe(true));
  });

  it('useTransactions serializes direction + multiple categoryIds into the query', async () => {
    let seenUrl = '';
    server.use(
      http.get('*/transactions', ({ request }) => {
        seenUrl = request.url;
        return HttpResponse.json({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 25,
          totalPages: 1,
        });
      }),
    );
    const { result } = renderHook(
      () =>
        useTransactions(
          { direction: 'Expense', categoryIds: ['cat-1', 'cat-2'], from: '2026-05-01' },
          1,
          25,
        ),
      { wrapper: wrapper() },
    );
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(seenUrl).toContain('direction=Expense');
    expect(seenUrl).toContain('categoryId=cat-1');
    expect(seenUrl).toContain('categoryId=cat-2');
  });

  it('useBudgets builds a year+month query string', async () => {
    let seenUrl = '';
    server.use(
      http.get('*/budgets', ({ request }) => {
        seenUrl = request.url;
        return HttpResponse.json([]);
      }),
    );
    const { result } = renderHook(() => useBudgets(2026, 5), { wrapper: wrapper() });
    await waitFor(() => expect(result.current.isSuccess).toBe(true));
    expect(seenUrl).toContain('year=2026');
    expect(seenUrl).toContain('month=5');
  });
});

describe('convertFx', () => {
  it('passes the query params and returns the response', async () => {
    server.use(
      http.get('*/fx-rates/convert', ({ request }) => {
        const url = new URL(request.url);
        expect(url.searchParams.get('from')).toBe('MDL');
        expect(url.searchParams.get('amount')).toBe('100');
        return HttpResponse.json({ convertedAmount: 5.8, rate: 0.058, hasRate: true });
      }),
    );
    const res = await convertFx({ from: 'MDL', to: 'USD', date: '2026-05-01', amount: 100 });
    expect(res.hasRate).toBe(true);
  });
});

describe('apiClient error handling', () => {
  it('uses the ProblemDetails title when detail is absent', async () => {
    server.use(
      http.get('*/accounts', () => HttpResponse.json({ title: 'Just a title' }, { status: 500 })),
    );
    await expect(apiClient.get('/accounts')).rejects.toMatchObject({ message: 'Just a title' });
  });

  it('falls back to a status message when the error body is not JSON', async () => {
    server.use(http.get('*/accounts', () => new HttpResponse('not json', { status: 503 })));
    await expect(apiClient.get('/accounts')).rejects.toMatchObject({
      message: 'Request failed with status 503',
      status: 503,
    });
    await expect(apiClient.get('/accounts')).rejects.toBeInstanceOf(ApiError);
  });
});

describe('data backup helpers', () => {
  it('getBackupDownloadUrl appends the export path', () => {
    expect(getBackupDownloadUrl('http://x')).toBe('http://x/data/export');
  });

  it('downloadBackup synthesizes an anchor click', () => {
    const click = vi.fn();
    const created = document.createElement('a');
    created.click = click;
    const spy = vi.spyOn(document, 'createElement').mockReturnValueOnce(created);
    downloadBackup('http://x');
    expect(click).toHaveBeenCalled();
    expect(created.download).toBe('money-management-backup.json');
    spy.mockRestore();
  });
});
