import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { fireEvent, render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it, vi } from 'vitest';
import { EditLoanDialog } from '@/src/components/loans/edit-loan-dialog';
import { server } from '@/src/lib/mocks/server';
import type { LoanDto } from '@/src/types/api';

function renderWithClient(ui: React.ReactElement) {
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });
  return render(<QueryClientProvider client={client}>{ui}</QueryClientProvider>);
}

const loan: LoanDto = {
  id: 'l0000001-0000-0000-0000-000000000001',
  direction: 'Received',
  counterparty: 'Parents',
  principal: 5000,
  currency: 'EUR',
  loanDate: '2026-01-10',
  totalRepaid: 2000,
  outstanding: 3000,
  outstandingMdl: 57600,
  missingFxRate: false,
  status: 'Active',
  paymentCount: 2,
  isAccountLinked: false,
  notes: 'Borrowed for the car',
};

describe('EditLoanDialog', () => {
  it('seeds the form from the loan and PUTs counterparty + notes', async () => {
    const user = userEvent.setup();
    const onOpenChange = vi.fn();

    let capturedBody: Record<string, unknown> | null = null;
    let capturedUrl = '';
    server.use(
      http.put('*/loans/:id', async ({ request }) => {
        capturedUrl = request.url;
        capturedBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<EditLoanDialog loan={loan} open onOpenChange={onOpenChange} />);

    const counterparty = screen.getByTestId('edit-loan-counterparty-input') as HTMLInputElement;
    expect(counterparty.value).toBe('Parents');
    const notes = screen.getByTestId('edit-loan-notes-input') as HTMLTextAreaElement;
    expect(notes.value).toBe('Borrowed for the car');

    await user.clear(counterparty);
    await user.type(counterparty, 'Mom & Dad');
    await user.click(screen.getByTestId('edit-loan-submit-button'));

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
    expect(capturedUrl).toContain(`/loans/${loan.id}`);
    const body = capturedBody as Record<string, unknown> | null;
    expect(body).not.toBeNull();
    expect(body?.counterparty).toBe('Mom & Dad');
    expect(body?.notes).toBe('Borrowed for the car');
  });

  it('sends notes as null when cleared', async () => {
    const user = userEvent.setup();

    let capturedBody: Record<string, unknown> | null = null;
    server.use(
      http.put('*/loans/:id', async ({ request }) => {
        capturedBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    renderWithClient(<EditLoanDialog loan={loan} open onOpenChange={vi.fn()} />);

    await user.clear(screen.getByTestId('edit-loan-notes-input'));
    await user.click(screen.getByTestId('edit-loan-submit-button'));

    await waitFor(() => expect(capturedBody).not.toBeNull());
    expect((capturedBody as Record<string, unknown> | null)?.notes).toBeNull();
  });

  it('rejects an empty counterparty', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditLoanDialog loan={loan} open onOpenChange={vi.fn()} />);

    await user.clear(screen.getByTestId('edit-loan-counterparty-input'));
    await user.click(screen.getByTestId('edit-loan-submit-button'));

    expect(await screen.findByText('Counterparty is required')).toBeInTheDocument();
  });

  it('rejects notes longer than 500 characters', async () => {
    const user = userEvent.setup();
    renderWithClient(<EditLoanDialog loan={loan} open onOpenChange={vi.fn()} />);

    // The textarea clamps at 500 via maxLength — strip it and drive the value
    // via fireEvent so the Zod guard (the server-mirroring layer) is exercised.
    const notes = screen.getByTestId('edit-loan-notes-input') as HTMLTextAreaElement;
    notes.removeAttribute('maxlength');
    fireEvent.change(notes, { target: { value: 'x'.repeat(501) } });

    await user.click(screen.getByTestId('edit-loan-submit-button'));

    expect(await screen.findByText('Notes must be 500 characters or less')).toBeInTheDocument();
  });

  it('surfaces an ApiError inline and keeps the dialog open', async () => {
    server.use(
      http.put('*/loans/:id', () =>
        HttpResponse.json({ detail: 'Loan is archived' }, { status: 400 }),
      ),
    );
    const user = userEvent.setup();
    const onOpenChange = vi.fn();
    renderWithClient(<EditLoanDialog loan={loan} open onOpenChange={onOpenChange} />);

    await user.click(screen.getByTestId('edit-loan-submit-button'));

    const error = await screen.findByTestId('edit-loan-error');
    expect(error).toHaveTextContent('Loan is archived');
    expect(error).toHaveAttribute('role', 'alert');
    expect(onOpenChange).not.toHaveBeenCalled();
  });

  it('falls back to an error toast on a network failure', async () => {
    const { Toaster } = await import('@/src/components/ui/sonner');
    server.use(http.put('*/loans/:id', () => HttpResponse.error()));
    const user = userEvent.setup();
    render(
      <QueryClientProvider
        client={
          new QueryClient({
            defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
          })
        }
      >
        <EditLoanDialog loan={loan} open onOpenChange={vi.fn()} />
        <Toaster />
      </QueryClientProvider>,
    );

    await user.click(screen.getByTestId('edit-loan-submit-button'));

    expect(await screen.findByText(/fetch/i)).toBeInTheDocument();
  });
});
