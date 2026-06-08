import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { ImportPreview } from '@/src/components/transactions/import-preview';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import type { StatementPreviewDto } from '@/src/types/api';

vi.mock('next/navigation', () => ({ useRouter: () => ({ push: vi.fn() }) }));

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

const IMPORT_ACCOUNT_ID = '11111111-1111-1111-1111-111111111111';

function buildPreview(overrides?: Partial<StatementPreviewDto>): StatementPreviewDto {
  return {
    fileHash: 'hash-bulk',
    statementPeriod: { from: '2025-05-01', to: '2025-05-31' },
    bankSource: 'Maib',
    summary: {
      openingBalance: 1000,
      closingBalance: 1200,
      totalIn: 500,
      totalOut: 300,
      totalFees: 0,
    },
    transactions: [
      {
        transactionDate: '2025-05-04',
        direction: 'Expense',
        amount: 175,
        description: 'LINELLA',
        isDuplicate: true,
        isTransfer: false,
        originalAmount: 9,
        originalCurrency: 'EUR',
      },
      {
        transactionDate: '2025-05-20',
        direction: 'Income',
        amount: 500,
        description: 'SALARY',
        isDuplicate: false,
        isTransfer: false,
      },
    ],
    ...overrides,
  };
}

describe('ImportPreview — bulk actions + commit edges', () => {
  it('renders a duplicate badge and the original-amount sub-line', async () => {
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    expect(await screen.findByText('Already imported')).toBeInTheDocument();
    expect(screen.getByText(/9 EUR/)).toBeInTheDocument();
  });

  it('Include all, Exclude duplicates, and Reset selections re-tally the counter', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    await screen.findByTestId('import-counter');

    await user.click(screen.getByRole('button', { name: 'Exclude duplicates' }));
    // Duplicate row 0 dropped → importing 1 of 2.
    await waitFor(() => expect(screen.getByTestId('import-counter')).toHaveTextContent(/1 of 2/));

    await user.click(screen.getByRole('button', { name: 'Include all' }));
    await waitFor(() => expect(screen.getByTestId('import-counter')).toHaveTextContent(/2 of 2/));

    await user.click(screen.getByRole('button', { name: 'Reset selections' }));
    expect(screen.getByTestId('import-counter')).toBeInTheDocument();
  });

  it('disables the commit button when no rows are selected', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    await screen.findByTestId('import-counter');
    // Row 0 is a duplicate (starts excluded); row 1 starts included. Unchecking
    // row 1 leaves zero selected → the commit button disables (count === 0).
    await user.click(screen.getByTestId('import-row-checkbox-1'));
    await waitFor(() => expect(screen.getByTestId('import-commit-button')).toBeDisabled());
  });

  it('surfaces a commit failure as an error toast', async () => {
    server.use(
      http.post('*/imports/commit', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    await screen.findByTestId('import-counter');
    await user.click(screen.getByTestId('import-commit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });

  it('commits the selected rows and toasts the imported count', async () => {
    server.use(
      http.post('*/imports/commit', () =>
        HttpResponse.json({ importedCount: 2, skippedDuplicates: 0 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    await screen.findByTestId('import-counter');
    await user.click(screen.getByTestId('import-commit-button'));
    expect(await screen.findByText(/Imported 2 transactions/)).toBeInTheDocument();
  });

  it('commits an edited cross-currency received amount (blur path)', async () => {
    let captured: { transactions: Array<Record<string, unknown>> } | null = null;
    server.use(
      http.get('*/fx-rates/convert', () =>
        HttpResponse.json({ convertedAmount: 29.1, rate: 0.0582, hasRate: true }),
      ),
      http.post('*/imports/commit', async ({ request }) => {
        captured = (await request.json()) as { transactions: Array<Record<string, unknown>> };
        return HttpResponse.json({ importedCount: 1, skippedDuplicates: 0 });
      }),
    );
    const transferPreview = buildPreview({
      transactions: [
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 500,
          description: 'ATM',
          isDuplicate: false,
          isTransfer: true,
        },
      ],
    });
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={transferPreview}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    await waitFor(() =>
      expect(screen.getByTestId('import-row-counter-0')).toHaveTextContent(/optional/i),
    );
    // XTB is USD → cross-currency → reveals the received-amount field (pre-filled).
    await user.click(screen.getByTestId('import-row-counter-0'));
    await user.click(await screen.findByRole('option', { name: /XTB/i }));
    const received = (await screen.findByTestId('import-row-counter-amount-0')) as HTMLInputElement;
    await waitFor(() => expect(received.value).toBe('29.10'));
    // Edit then blur → commits the changed value via set-counter-amount.
    await user.clear(received);
    await user.type(received, '30');
    await user.tab();
    await user.click(screen.getByTestId('import-commit-button'));
    await waitFor(() => expect(captured).not.toBeNull());
    const body = captured as unknown as { transactions: Array<Record<string, unknown>> };
    expect(body.transactions[0]).toMatchObject({ counterAmount: 30 });
  });

  it('shows the mismatch reconciliation warning when opening + in − out ≠ closing', async () => {
    const mismatched = buildPreview({
      summary: {
        openingBalance: 1000,
        closingBalance: 9999,
        totalIn: 500,
        totalOut: 300,
        totalFees: 0,
      },
    });
    renderWithClient(
      <ImportPreview
        preview={mismatched}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    expect(await screen.findByText(/doesn't match closing/)).toBeInTheDocument();
  });

  it('flags the row-derived parse check when the header fees disagree with the rows', async () => {
    // No "Comision" row → rowFees = 0, but the header claims totalFees = 50 → Δ.
    const feesMismatch = buildPreview({
      summary: {
        openingBalance: 1000,
        closingBalance: 1200,
        totalIn: 500,
        totalOut: 300,
        totalFees: 50,
      },
      transactions: [
        {
          transactionDate: '2025-05-04',
          direction: 'Expense',
          amount: 300,
          description: 'GROCERY',
          isDuplicate: false,
          isTransfer: false,
        },
        {
          transactionDate: '2025-05-20',
          direction: 'Income',
          amount: 500,
          description: 'SALARY',
          isDuplicate: false,
          isTransfer: false,
        },
      ],
    });
    renderWithClient(
      <ImportPreview
        preview={feesMismatch}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    // The row-derived check shows a fees delta marker (⚠ Δ ...).
    const rowsCheck = await screen.findByTestId('import-rows-check');
    expect(rowsCheck.textContent ?? '').toMatch(/Δ/);
  });

  it('re-seeds a typed note back to blank when Reset selections is clicked', async () => {
    const user = userEvent.setup();
    renderWithClient(
      <ImportPreview
        preview={buildPreview()}
        accountId={IMPORT_ACCOUNT_ID}
        fileName="s.pdf"
        onCancel={() => {}}
      />,
    );
    // Open the note affordance on row 1, type a note.
    await user.click(await screen.findByTestId('import-row-note-toggle-1'));
    const note = await screen.findByTestId('import-row-note-input-1');
    await user.type(note, 'remember this');
    await user.tab();
    // Reset selections re-inits the rows → committedNote becomes '' → the
    // render-time re-sync mirrors it into the draft (note draft re-sync path).
    await user.click(screen.getByRole('button', { name: 'Reset selections' }));
    await waitFor(() => {
      expect(screen.queryByDisplayValue('remember this')).not.toBeInTheDocument();
    });
  });
});
