import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import type { ReactElement } from 'react';
import { describe, expect, it, vi } from 'vitest';
import { CreateCategoryDialog } from '@/src/components/categories/create-category-dialog';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

function renderWithClient(ui: ReactElement) {
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

describe('CreateCategoryDialog (controlled)', () => {
  it('validates a blank name', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateCategoryDialog open onOpenChange={vi.fn()} />);
    await user.click(screen.getByTestId('category-submit-button'));
    expect(await screen.findByText('Name is required')).toBeInTheDocument();
  });

  it('creates a category with the chosen flow and reports it via onCreated', async () => {
    const onCreated = vi.fn();
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(
      <CreateCategoryDialog
        open
        onOpenChange={onOpenChange}
        defaultFlow="Income"
        onCreated={onCreated}
      />,
    );
    await user.type(screen.getByTestId('category-name-input'), 'Freelance');
    // Switch the flow select to exercise onFlowChange.
    await user.click(screen.getByTestId('category-flow-select'));
    await user.click(await screen.findByRole('option', { name: /Expense/ }));
    await user.click(screen.getByTestId('category-submit-button'));

    await waitFor(() => expect(onCreated).toHaveBeenCalled());
    expect(onCreated).toHaveBeenCalledWith(
      expect.objectContaining({ name: 'Freelance', flow: 'Expense' }),
    );
    expect(onOpenChange).toHaveBeenCalledWith(false);
    expect(await screen.findByText('Category created')).toBeInTheDocument();
  });

  it('shows an error toast when creation fails', async () => {
    server.use(
      http.post('*/categories', () => HttpResponse.json({ detail: 'dup name' }, { status: 409 })),
    );
    const user = userEvent.setup();
    renderWithClient(<CreateCategoryDialog open onOpenChange={vi.fn()} />);
    await user.type(screen.getByTestId('category-name-input'), 'Groceries');
    await user.click(screen.getByTestId('category-submit-button'));
    expect(await screen.findByText('dup name')).toBeInTheDocument();
  });

  it('resets the form when closed via Cancel', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<CreateCategoryDialog open onOpenChange={onOpenChange} />);
    await user.type(screen.getByTestId('category-name-input'), 'Temp');
    await user.click(screen.getByRole('button', { name: 'Cancel' }));
    expect(onOpenChange).toHaveBeenCalledWith(false);
  });

  it('re-seeds defaults on Escape close (controlled wrapper)', async () => {
    const onOpenChange = vi.fn();
    const user = userEvent.setup();
    renderWithClient(<CreateCategoryDialog open onOpenChange={onOpenChange} />);
    await user.keyboard('{Escape}');
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false));
  });
});
