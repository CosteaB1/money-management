import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CreateBudgetDialog } from '@/src/components/budgets/create-budget-dialog';
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

describe('CreateBudgetDialog', () => {
  it('only offers expense-flow categories (Income is filtered out)', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateBudgetDialog />);

    await user.click(screen.getByTestId('add-budget-button'));

    await waitFor(() => {
      expect(screen.getByTestId('budget-category-select')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('budget-category-select'));

    // Expense + Both categories are budgetable.
    await waitFor(() => {
      expect(screen.getByRole('option', { name: /Groceries/ })).toBeInTheDocument();
    });
    expect(screen.getByRole('option', { name: /Dining out/ })).toBeInTheDocument();
    expect(screen.getByRole('option', { name: /^Misc$/ })).toBeInTheDocument();

    // Income-flow categories never show up.
    expect(screen.queryByRole('option', { name: /^Salary$/ })).not.toBeInTheDocument();
  });

  it('submits a valid budget and closes the dialog on success', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateBudgetDialog />);

    await user.click(screen.getByTestId('add-budget-button'));

    await waitFor(() => {
      expect(screen.getByTestId('budget-category-select')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('budget-category-select'));
    // Utilities is seeded but not yet budgeted, so it's enabled.
    const utilitiesOption = await screen.findByRole('option', { name: /^Utilities$/ });
    await user.click(utilitiesOption);

    const limitInput = screen.getByTestId('budget-monthly-limit-input');
    await user.clear(limitInput);
    await user.type(limitInput, '2500');

    await user.click(screen.getByTestId('budget-submit-button'));

    await waitFor(() => {
      expect(screen.queryByTestId('budget-submit-button')).not.toBeInTheDocument();
    });
  });

  it('shows the inline 409 conflict error when a budget already exists for the category', async () => {
    const user = userEvent.setup();

    server.use(
      http.post('*/budgets', () =>
        HttpResponse.json({ error: 'Budget already exists for category' }, { status: 409 }),
      ),
    );

    renderWithClient(<CreateBudgetDialog />);

    await user.click(screen.getByTestId('add-budget-button'));

    await waitFor(() => {
      expect(screen.getByTestId('budget-category-select')).toBeInTheDocument();
    });

    await user.click(screen.getByTestId('budget-category-select'));
    const option = await screen.findByRole('option', { name: /^Utilities$/ });
    await user.click(option);

    const limit = screen.getByTestId('budget-monthly-limit-input');
    await user.clear(limit);
    await user.type(limit, '500');

    await user.click(screen.getByTestId('budget-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/a budget already exists for this category/i)).toBeInTheDocument();
    });

    // Dialog stays open so the user can pick a different category.
    expect(screen.getByTestId('budget-submit-button')).toBeInTheDocument();
  });

  it('shows the inline 404 error when the category no longer exists', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/budgets', () => HttpResponse.json({ detail: 'gone' }, { status: 404 })),
    );
    renderWithClient(<CreateBudgetDialog />);
    await user.click(screen.getByTestId('add-budget-button'));
    await user.click(await screen.findByTestId('budget-category-select'));
    await user.click(await screen.findByRole('option', { name: /^Utilities$/ }));
    const limit = screen.getByTestId('budget-monthly-limit-input');
    await user.clear(limit);
    await user.type(limit, '500');
    await user.click(screen.getByTestId('budget-submit-button'));
    expect(await screen.findByText('Category not found.')).toBeInTheDocument();
  });

  it('shows a generic error toast for a non-conflict failure', async () => {
    const user = userEvent.setup();
    server.use(
      http.post('*/budgets', () => HttpResponse.json({ detail: 'boom' }, { status: 500 })),
    );
    renderWithClient(<CreateBudgetDialog />);
    await user.click(screen.getByTestId('add-budget-button'));
    await user.click(await screen.findByTestId('budget-category-select'));
    await user.click(await screen.findByRole('option', { name: /^Utilities$/ }));
    const limit = screen.getByTestId('budget-monthly-limit-input');
    await user.clear(limit);
    await user.type(limit, '500');
    await user.click(screen.getByTestId('budget-submit-button'));
    expect(await screen.findByText('boom')).toBeInTheDocument();
  });

  it('validates that the monthly limit is positive', async () => {
    const user = userEvent.setup();
    renderWithClient(<CreateBudgetDialog />);

    await user.click(screen.getByTestId('add-budget-button'));

    await waitFor(() => {
      expect(screen.getByTestId('budget-submit-button')).toBeInTheDocument();
    });

    // Submit with the default 0 — should fail validation client-side.
    await user.click(screen.getByTestId('budget-submit-button'));

    await waitFor(() => {
      expect(screen.getByText(/monthly limit must be greater than 0/i)).toBeInTheDocument();
    });
  });
});
