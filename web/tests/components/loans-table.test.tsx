import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { LoansTable } from '@/src/components/loans/loans-table';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
import { formatMoney } from '@/src/lib/utils/currency';
import type { LoanDto } from '@/src/types/api';

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

function makeLoan(overrides: Partial<LoanDto>): LoanDto {
  return {
    id: overrides.id ?? crypto.randomUUID(),
    direction: overrides.direction ?? 'Given',
    counterparty: overrides.counterparty ?? 'Somebody',
    principal: overrides.principal ?? 1000,
    currency: overrides.currency ?? 'MDL',
    loanDate: overrides.loanDate ?? '2026-01-01',
    totalRepaid: overrides.totalRepaid ?? 0,
    outstanding: overrides.outstanding ?? 1000,
    outstandingMdl: overrides.outstandingMdl !== undefined ? overrides.outstandingMdl : 1000,
    missingFxRate: overrides.missingFxRate ?? false,
    status: overrides.status ?? 'Active',
    paymentCount: overrides.paymentCount ?? 0,
    notes: overrides.notes ?? null,
    isArchived: overrides.isArchived ?? false,
  };
}

describe('LoansTable', () => {
  it('renders the three seeded loans with direction badges and status pills', async () => {
    renderWithClient(<LoansTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('loan-row').length).toBe(3);
    });

    const directions = screen.getAllByTestId('loan-direction-badge').map((b) => b.textContent);
    expect(directions.filter((d) => d === 'Borrowed').length).toBe(1);
    expect(directions.filter((d) => d === 'Lent').length).toBe(2);

    const pills = screen.getAllByTestId('loan-status-pill').map((p) => p.textContent);
    expect(pills.filter((p) => p === 'Active').length).toBe(2);
    expect(pills.filter((p) => p === 'Settled').length).toBe(1);
  });

  it('renders the counterparty cell as a link to /loans/{id}', async () => {
    renderWithClient(<LoansTable />);

    await waitFor(() => {
      expect(screen.getAllByTestId('loan-counterparty-link').length).toBe(3);
    });

    const link = screen.getByRole('link', { name: 'Parents' });
    expect(link).toHaveAttribute('href', '/loans/l0000001-0000-0000-0000-000000000001');
  });

  it('formats principal, repaid, and outstanding in the native currency', async () => {
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-row').length).toBe(3));

    const parentsRow = (await screen.findByText('Parents')).closest('tr') as HTMLElement;
    expect(within(parentsRow).getByTestId('loan-principal').textContent).toBe(
      formatMoney(5000, 'EUR'),
    );
    expect(within(parentsRow).getByTestId('loan-repaid').textContent).toBe(
      formatMoney(2000, 'EUR'),
    );
    expect(within(parentsRow).getByTestId('loan-outstanding').textContent).toBe(
      formatMoney(3000, 'EUR'),
    );
  });

  it('shows the muted MDL-eq line only on non-MDL rows with a convertible outstanding', async () => {
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-row').length).toBe(3));

    // EUR (Parents) + USD (Maria) rows show it; the MDL (Ion) row doesn't.
    const mdlEqLines = screen.getAllByTestId('loan-outstanding-mdl');
    expect(mdlEqLines.length).toBe(2);
    const parentsRow = (await screen.findByText('Parents')).closest('tr') as HTMLElement;
    expect(within(parentsRow).getByTestId('loan-outstanding-mdl').textContent).toBe(
      formatMoney(57600, 'MDL'),
    );
    const ionRow = (await screen.findByText('Ion')).closest('tr') as HTMLElement;
    expect(within(ionRow).queryByTestId('loan-outstanding-mdl')).not.toBeInTheDocument();
  });

  it('caps the repayment progress bar at 100% on a settled row', async () => {
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-row').length).toBe(3));

    const mariaRow = (await screen.findByText('Maria')).closest('tr') as HTMLElement;
    const bar = within(mariaRow).getByTestId('loan-progress-bar');
    expect(bar.getAttribute('style') ?? '').toMatch(/width:\s*100%/);
    expect(bar.getAttribute('data-status')).toBe('Settled');

    // Partial repayment renders proportionally (500 / 2500 = 20%).
    const ionRow = (await screen.findByText('Ion')).closest('tr') as HTMLElement;
    const ionBar = within(ionRow).getByTestId('loan-progress-bar');
    expect(ionBar.getAttribute('style') ?? '').toMatch(/width:\s*20%/);
  });

  it('shows the missing-FX warning icon on rows without a convertible rate', async () => {
    server.use(
      http.get('*/loans', () =>
        HttpResponse.json([
          makeLoan({
            id: 'missing-fx',
            counterparty: 'Cousin',
            currency: 'GBP',
            principal: 500,
            outstanding: 500,
            outstandingMdl: null,
            missingFxRate: true,
          }),
        ]),
      ),
    );

    renderWithClient(<LoansTable />);

    await waitFor(() => {
      expect(screen.getByTestId('loan-missing-fx-icon')).toBeInTheDocument();
    });
    // No MDL-eq line without a convertible rate.
    expect(screen.queryByTestId('loan-outstanding-mdl')).not.toBeInTheDocument();
  });

  it('renders the empty hint when no loans exist', async () => {
    server.use(http.get('*/loans', () => HttpResponse.json([])));

    renderWithClient(<LoansTable />);

    await waitFor(() => {
      expect(screen.getByText(/no loans yet/i)).toBeInTheDocument();
    });
  });

  it('renders the error state when the request fails', async () => {
    server.use(http.get('*/loans', () => HttpResponse.json({ error: 'boom' }, { status: 500 })));

    renderWithClient(<LoansTable />);

    await waitFor(() => {
      expect(screen.getByText(/failed to load loans/i)).toBeInTheDocument();
    });
  });

  it('renders skeleton rows while loading (no loan rows visible)', async () => {
    server.use(http.get('*/loans', () => new Promise(() => {}) as unknown as Promise<Response>));

    renderWithClient(<LoansTable />);

    expect(screen.queryAllByTestId('loan-row').length).toBe(0);
    expect(screen.getByTestId('loans-table')).toBeInTheDocument();
  });

  it('hides "Record payment" in the row menu for settled loans', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-row').length).toBe(3));

    // Settled row (Maria) — no Record payment action.
    const mariaRow = (await screen.findByText('Maria')).closest('tr') as HTMLElement;
    await user.click(within(mariaRow).getByTestId('loan-actions'));
    await waitFor(() => {
      expect(screen.getByTestId('edit-loan-action')).toBeInTheDocument();
    });
    expect(screen.queryByTestId('record-payment-action')).not.toBeInTheDocument();
    await user.keyboard('{Escape}');

    // Active row (Ion) — Record payment shows up.
    const ionRow = (await screen.findByText('Ion')).closest('tr') as HTMLElement;
    await user.click(within(ionRow).getByTestId('loan-actions'));
    await waitFor(() => {
      expect(screen.getByTestId('record-payment-action')).toBeInTheDocument();
    });
  });

  it('opens then closes the Record-payment dialog from an active row', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoansTable />);
    const ionRow = (await screen.findByText('Ion')).closest('tr') as HTMLElement;
    await user.click(within(ionRow).getByTestId('loan-actions'));
    await user.click(await screen.findByTestId('record-payment-action'));
    expect(await screen.findByTestId('record-payment-dialog')).toBeInTheDocument();
    // Closing runs the table's `if (!next) setRecordTarget(null)`.
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('record-payment-dialog')).not.toBeInTheDocument(),
    );
  });

  it('opens then closes the Edit dialog from the row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-actions').length).toBe(3));
    await user.click(screen.getAllByTestId('loan-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('edit-loan-action'));
    expect(await screen.findByTestId('edit-loan-dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() => expect(screen.queryByTestId('edit-loan-dialog')).not.toBeInTheDocument());
  });

  it('opens then closes the Archive dialog from the row menu', async () => {
    const user = userEvent.setup();
    renderWithClient(<LoansTable />);
    await waitFor(() => expect(screen.getAllByTestId('loan-actions').length).toBe(3));
    await user.click(screen.getAllByTestId('loan-actions')[0] as HTMLElement);
    await user.click(await screen.findByTestId('archive-loan-action'));
    expect(await screen.findByTestId('archive-loan-dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('archive-loan-dialog')).not.toBeInTheDocument(),
    );
  });
});
