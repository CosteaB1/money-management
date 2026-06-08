import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { TransactionsTable } from '@/src/components/transactions/transactions-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(
    <QueryClientProvider client={client}>
      {ui}
      <Toaster />
    </QueryClientProvider>,
  );
}

describe('TransactionsTable transfer integration', () => {
  it('renders a Transfer badge for rows where isTransfer is true', async () => {
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await waitFor(() => {
      // The default mock seed has two transfer rows + one normal row.
      const badges = screen.getAllByTestId('transfer-badge');
      expect(badges.length).toBeGreaterThanOrEqual(1);
    });
  });

  it('passes the isTransfer query param when the filter is set', async () => {
    let lastUrl = '';
    server.use(
      http.get('*/transactions', ({ request }) => {
        lastUrl = request.url;
        return HttpResponse.json({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 25,
          totalPages: 1,
        });
      }),
    );

    renderWithClient(
      <TransactionsTable
        filters={{ isTransfer: true }}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
      />,
    );

    await waitFor(() => {
      expect(lastUrl).toContain('isTransfer=true');
    });
  });

  it('passes isTransfer=false when transfers are explicitly excluded', async () => {
    let lastUrl = '';
    server.use(
      http.get('*/transactions', ({ request }) => {
        lastUrl = request.url;
        return HttpResponse.json({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 25,
          totalPages: 1,
        });
      }),
    );

    renderWithClient(
      <TransactionsTable
        filters={{ isTransfer: false }}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
      />,
    );

    await waitFor(() => {
      expect(lastUrl).toContain('isTransfer=false');
    });
  });

  it('omits isTransfer when the filter is undefined', async () => {
    let lastUrl = '';
    server.use(
      http.get('*/transactions', ({ request }) => {
        lastUrl = request.url;
        return HttpResponse.json({
          items: [],
          totalCount: 0,
          pageNumber: 1,
          pageSize: 25,
          totalPages: 1,
        });
      }),
    );

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await waitFor(() => {
      expect(lastUrl).not.toContain('isTransfer');
    });
  });
});

describe('TransactionsTable inline category Select', () => {
  // Single Expense row, uncategorized, so the trigger starts at
  // "Uncategorized" and the option list is filtered to Expense|Both flows.
  const expenseRow = {
    items: [
      {
        id: 'tx-edit-me',
        accountId: '11111111-1111-1111-1111-111111111111',
        transactionDate: '2025-05-12',
        direction: 'Expense' as const,
        amount: 35,
        description: 'Coffee at Tucano',
        source: 'Manual' as const,
        isTransfer: false,
        counterAccountId: null,
        currency: 'MDL',
        amountMdl: 35,
        isAdjustment: false,
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  };

  it('renders a per-row category Select showing "Uncategorized" for an uncategorized row', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(expenseRow)));

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    const select = await screen.findByTestId('tx-category-select');
    expect(select).toHaveTextContent('Uncategorized');
  });

  it('changing a row category calls PUT /transactions/{id}/category and toasts success', async () => {
    let putBody: Record<string, unknown> | null = null;
    let putId = '';
    server.use(
      http.get('*/transactions', () => HttpResponse.json(expenseRow)),
      http.put('*/transactions/:id/category', async ({ request, params }) => {
        putId = String(params.id);
        putBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    const select = await screen.findByTestId('tx-category-select');
    await user.click(select);

    // Pick an Expense category from the popover.
    const groceries = await screen.findByRole('option', { name: 'Groceries' });
    await user.click(groceries);

    await waitFor(() => {
      expect(putId).toBe('tx-edit-me');
    });
    expect(putBody).toEqual({ categoryId: 'c0000001-0000-0000-0000-000000000001' });

    await waitFor(() => {
      expect(screen.getByText('Category updated')).toBeInTheDocument();
    });
  });

  it('surfaces the backend 400 flow-mismatch detail via an error toast', async () => {
    server.use(
      http.get('*/transactions', () => HttpResponse.json(expenseRow)),
      http.put('*/transactions/:id/category', () =>
        HttpResponse.json(
          {
            status: 400,
            detail: "Category flow 'Income' is incompatible with an Expense transaction.",
          },
          { status: 400 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await user.click(await screen.findByTestId('tx-category-select'));
    await user.click(await screen.findByRole('option', { name: 'Groceries' }));

    await waitFor(() => {
      expect(
        screen.getByText("Category flow 'Income' is incompatible with an Expense transaction."),
      ).toBeInTheDocument();
    });
  });

  it('filters options by direction: an Expense row does not offer an Income-only category', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(expenseRow)));

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await user.click(await screen.findByTestId('tx-category-select'));

    // The popover (listbox) is open — assert Expense + Both flow categories
    // are offered but the Income-only "Salary" category is not.
    const listbox = await screen.findByRole('listbox');
    expect(within(listbox).getByRole('option', { name: 'Groceries' })).toBeInTheDocument();
    // "Misc" is flow Both → still offered on an Expense row.
    expect(within(listbox).getByRole('option', { name: 'Misc' })).toBeInTheDocument();
    // "Salary" is flow Income → must be filtered out for an Expense row.
    expect(within(listbox).queryByRole('option', { name: 'Salary' })).not.toBeInTheDocument();
  });
});

describe('TransactionsTable inline note editor', () => {
  const rowWithNote = {
    items: [
      {
        id: 'tx-note-me',
        accountId: '11111111-1111-1111-1111-111111111111',
        transactionDate: '2025-05-12',
        direction: 'Expense' as const,
        amount: 35,
        description: 'Coffee at Tucano',
        notes: 'Treat after the gym',
        source: 'Manual' as const,
        isTransfer: false,
        counterAccountId: null,
        currency: 'MDL',
        amountMdl: 35,
        isAdjustment: false,
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  };

  const rowWithoutNote = {
    ...rowWithNote,
    items: [{ ...rowWithNote.items[0]!, id: 'tx-no-note', notes: null }],
  };

  it('renders the note as a muted sub-line under the description when present', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(rowWithNote)));

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    const note = await screen.findByTestId('transaction-note');
    expect(note).toHaveTextContent('Treat after the gym');
  });

  it('does not render a note sub-line when the row has no note', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(rowWithoutNote)));

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    // The edit affordance is always present; the display sub-line is not.
    await screen.findByTestId('edit-note-tx-no-note');
    expect(screen.queryByTestId('transaction-note')).not.toBeInTheDocument();
  });

  it('exposes the edit affordance regardless of allowDelete (account Activity case)', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(rowWithNote)));

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    expect(await screen.findByTestId('edit-note-tx-note-me')).toBeInTheDocument();
    // No actions column is rendered when allowDelete is unset.
    expect(screen.queryByTestId('delete-transaction')).not.toBeInTheDocument();
  });

  it('opens the dialog seeded with the current note, edits it, and PUTs to the notes endpoint', async () => {
    let putBody: Record<string, unknown> | null = null;
    let putId = '';
    server.use(
      http.get('*/transactions', () => HttpResponse.json(rowWithNote)),
      http.put('*/transactions/:id/notes', async ({ request, params }) => {
        putId = String(params.id);
        putBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await user.click(await screen.findByTestId('edit-note-tx-note-me'));

    const textarea = await screen.findByTestId('note-textarea');
    // Seeded with the existing note.
    expect(textarea).toHaveValue('Treat after the gym');

    await user.clear(textarea);
    await user.type(textarea, 'Updated note');
    await user.click(screen.getByTestId('note-save'));

    await waitFor(() => {
      expect(putId).toBe('tx-note-me');
    });
    expect(putBody).toEqual({ notes: 'Updated note' });

    await waitFor(() => {
      expect(screen.getByText('Note saved')).toBeInTheDocument();
    });
  });

  it('clearing the textarea and saving sends null to clear the note', async () => {
    let putBody: Record<string, unknown> | null = null;
    server.use(
      http.get('*/transactions', () => HttpResponse.json(rowWithNote)),
      http.put('*/transactions/:id/notes', async ({ request }) => {
        putBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    await user.click(await screen.findByTestId('edit-note-tx-note-me'));
    await user.clear(await screen.findByTestId('note-textarea'));
    await user.click(screen.getByTestId('note-save'));

    await waitFor(() => {
      expect(putBody).toEqual({ notes: null });
    });
    await waitFor(() => {
      expect(screen.getByText('Note cleared')).toBeInTheDocument();
    });
  });
});

describe('TransactionsTable account sub-line (All accounts vs single account)', () => {
  // A single Cash Wallet row — the account id matches the seeded "Cash Wallet"
  // account in the mock handlers, so the table can resolve its name.
  const cashWalletRow = {
    items: [
      {
        id: 'tx-acct-line',
        accountId: '11111111-1111-1111-1111-111111111111',
        transactionDate: '2025-05-12',
        direction: 'Expense' as const,
        amount: 35,
        description: 'Coffee at Tucano',
        source: 'Manual' as const,
        isTransfer: false,
        counterAccountId: null,
        currency: 'MDL',
        amountMdl: 35,
        isAdjustment: false,
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  };

  it('renders the owning account name as a sub-line when no account filter is applied', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(cashWalletRow)));

    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );

    const accountLine = await screen.findByTestId('transaction-account');
    expect(accountLine).toHaveTextContent('Cash Wallet');
  });

  it('omits the account sub-line on a single-account list (filters.accountId set)', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(cashWalletRow)));

    renderWithClient(
      <TransactionsTable
        filters={{ accountId: '11111111-1111-1111-1111-111111111111' }}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
      />,
    );

    // The row renders, but the redundant account sub-line is not present.
    await screen.findByTestId('transaction-row');
    expect(screen.queryByTestId('transaction-account')).not.toBeInTheDocument();
  });
});

describe('TransactionsTable category cell + note errors', () => {
  const categorizedRow = {
    items: [
      {
        id: 'tx-cat',
        accountId: '11111111-1111-1111-1111-111111111111',
        transactionDate: '2025-05-12',
        direction: 'Expense' as const,
        amount: 35,
        description: 'Linella run',
        categoryId: 'c0000001-0000-0000-0000-000000000001',
        categoryName: 'Groceries',
        notes: 'note here',
        source: 'Manual' as const,
        isTransfer: false,
        counterAccountId: null,
        currency: 'MDL',
        amountMdl: 35,
        isAdjustment: false,
      },
    ],
    totalCount: 1,
    pageNumber: 1,
    pageSize: 25,
    totalPages: 1,
  };

  it('renders the current category name with a colour dot in the inline select', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json(categorizedRow)));
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );
    const select = await screen.findByTestId('tx-category-select');
    expect(within(select).getByText('Groceries')).toBeInTheDocument();
  });

  it('toasts an error when saving a note fails', async () => {
    server.use(
      http.get('*/transactions', () => HttpResponse.json(categorizedRow)),
      http.put('*/transactions/:id/notes', () =>
        HttpResponse.json({ detail: 'boom' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );
    await user.click(await screen.findByTestId('edit-note-tx-cat'));
    const textarea = await screen.findByTestId('note-textarea');
    await user.clear(textarea);
    await user.type(textarea, 'changed');
    await user.click(screen.getByTestId('note-save'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });
});

describe('TransactionsTable states + amount rendering', () => {
  it('renders the error row when the query fails', async () => {
    server.use(http.get('*/transactions', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );
    expect(await screen.findByText('Failed to load transactions.')).toBeInTheDocument();
  });

  it('renders the empty row with the empty action when no rows match', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({ items: [], totalCount: 0, pageNumber: 1, pageSize: 25, totalPages: 1 }),
      ),
    );
    renderWithClient(
      <TransactionsTable
        filters={{}}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
        emptyAction={<button type="button">Add one</button>}
      />,
    );
    expect(await screen.findByText(/No transactions match/)).toBeInTheDocument();
    expect(screen.getByRole('button', { name: 'Add one' })).toBeInTheDocument();
  });

  it('renders a pinned footer row', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({ items: [], totalCount: 0, pageNumber: 1, pageSize: 25, totalPages: 1 }),
      ),
    );
    renderWithClient(
      <TransactionsTable
        filters={{}}
        page={1}
        pageSize={25}
        onPageChange={() => {}}
        pinnedFooter={<div data-testid="pinned">Opening balance</div>}
      />,
    );
    expect(await screen.findByTestId('pinned')).toBeInTheDocument();
  });

  it('paginates between pages', async () => {
    server.use(
      http.get('*/transactions', ({ request }) => {
        const page = Number(new URL(request.url).searchParams.get('page') ?? '1');
        return HttpResponse.json({
          items: [
            {
              id: `tx-p${page}`,
              accountId: '11111111-1111-1111-1111-111111111111',
              transactionDate: '2025-05-12',
              direction: 'Expense',
              amount: 10,
              description: `Page ${page} row`,
              notes: null,
              source: 'Manual',
              isTransfer: false,
              counterAccountId: null,
              currency: 'MDL',
              amountMdl: 10,
              isAdjustment: false,
            },
          ],
          totalCount: 2,
          pageNumber: page,
          pageSize: 1,
          totalPages: 2,
        });
      }),
    );
    const onPageChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={1} onPageChange={onPageChange} />,
    );
    expect(await screen.findByText('Page 1 of 2')).toBeInTheDocument();
    await user.click(screen.getByRole('button', { name: 'Next page' }));
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it('renders an income row in emerald and a USD row with original-amount + missing MDL-eq', async () => {
    server.use(
      http.get('*/transactions', () =>
        HttpResponse.json({
          items: [
            {
              id: 'tx-income-usd',
              accountId: '44444444-4444-4444-4444-444444444444',
              transactionDate: '2025-05-12',
              direction: 'Income',
              amount: 100,
              description: 'USD income',
              notes: null,
              source: 'Manual',
              isTransfer: false,
              counterAccountId: null,
              currency: 'USD',
              amountMdl: null,
              originalAmount: 95,
              originalCurrency: 'EUR',
              isAdjustment: false,
            },
          ],
          totalCount: 1,
          pageNumber: 1,
          pageSize: 25,
          totalPages: 1,
        }),
      ),
    );
    renderWithClient(
      <TransactionsTable filters={{}} page={1} pageSize={25} onPageChange={() => {}} />,
    );
    const row = await screen.findByTestId('transaction-row');
    // Income → emerald amount class.
    expect(row.querySelector('.text-emerald-500')).not.toBeNull();
    // original-amount sub-line.
    expect(within(row).getByText(/95 EUR/)).toBeInTheDocument();
    // missing MDL-eq → em dash with a title.
    const mdlEq = within(row).getByTestId('tx-mdl-eq');
    expect(mdlEq).toHaveTextContent('—');
  });
});
