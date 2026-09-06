import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { ArchiveLoanDialog } from '@/src/components/loans/archive-loan-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';
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

const loan: LoanDto = {
  id: 'l0000001-0000-0000-0000-000000000002',
  direction: 'Given',
  counterparty: 'Ion',
  principal: 2500,
  currency: 'MDL',
  loanDate: '2026-03-05',
  totalRepaid: 500,
  outstanding: 2000,
  outstandingMdl: 2000,
  missingFxRate: false,
  status: 'Active',
  paymentCount: 1,
  isAccountLinked: true,
  notes: null,
};

describe('ArchiveLoanDialog', () => {
  it('archives on confirm and closes', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();

    let capturedUrl = '';
    server.use(
      http.delete('*/loans/:id', ({ request }) => {
        capturedUrl = request.url;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<ArchiveLoanDialog loan={loan} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('archive-loan-confirm-button'));
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(capturedUrl).toContain(`/loans/${loan.id}`);
    expect(await screen.findByText(/Archived "Ion" loan/)).toBeInTheDocument();
  });

  it('cancel closes without archiving', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<ArchiveLoanDialog loan={loan} open onOpenChange={onOpenChange} />);
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('shows an error toast when archive fails', async () => {
    server.use(
      http.delete('*/loans/:id', () =>
        HttpResponse.json({ detail: 'cannot archive' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<ArchiveLoanDialog loan={loan} open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('archive-loan-confirm-button'));
    expect(await screen.findByText('cannot archive')).toBeInTheDocument();
  });
});
