import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { HttpResponse, http } from 'msw';
import { describe, expect, it } from 'vitest';
import { CategoriesManager } from '@/src/components/settings/categories-manager';
import { Toaster } from '@/src/components/ui/sonner';
import { server } from '@/src/lib/mocks/server';

// Known seeded ids (mirror tests/mocks/handlers.ts) so we can target a
// specific category's expand toggle / add-keyword controls by testid.
const GROCERIES_ID = 'c0000001-0000-0000-0000-000000000001';

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

describe('CategoriesManager', () => {
  it('renders categories including an Archived badge for archived rows', async () => {
    renderWithClient(<CategoriesManager />);

    await waitFor(() => {
      expect(screen.getAllByTestId('category-row').length).toBeGreaterThan(0);
    });

    expect(screen.getByText('Groceries')).toBeInTheDocument();
    // The seeded archived category surfaces because includeArchived=true.
    expect(screen.getByText('Old subscriptions')).toBeInTheDocument();
    expect(screen.getByText('Archived')).toBeInTheDocument();
  });

  it('creates a category and shows a success toast', async () => {
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    await user.click(screen.getByTestId('add-category-button'));

    await waitFor(() => {
      expect(screen.getByTestId('category-name-input')).toBeInTheDocument();
    });

    await user.type(screen.getByTestId('category-name-input'), 'Pets');
    await user.click(screen.getByTestId('category-submit-button'));

    await waitFor(() => {
      expect(screen.getByText('Category created')).toBeInTheDocument();
    });
  });

  it('expanding a category reveals its keyword chips with the right source variant', async () => {
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    const toggle = await screen.findByTestId(`category-expand-${GROCERIES_ID}`);
    // Collapsed by default — no chips, toggle reports not-expanded.
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    expect(screen.queryByTestId('keyword-chip')).not.toBeInTheDocument();

    await user.click(toggle);

    await waitFor(() => {
      expect(toggle).toHaveAttribute('aria-expanded', 'true');
    });

    const container = screen.getByTestId(`category-keywords-${GROCERIES_ID}`);
    const chips = within(container).getAllByTestId('keyword-chip');
    // Groceries has two seeded keywords: LINELLA (Seeded) + KAUFLAND (Learned).
    expect(chips.length).toBe(2);
    expect(within(container).getByText('LINELLA')).toBeInTheDocument();
    expect(within(container).getByText('KAUFLAND')).toBeInTheDocument();

    const sources = chips.map((c) => c.getAttribute('data-source'));
    expect(sources).toContain('Seeded');
    expect(sources).toContain('Learned');
  });

  it('shows the "No keywords yet" hint for a category with no patterns', async () => {
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    // Transport has no seeded patterns.
    const transportRow = (await screen.findAllByTestId('category-row')).find((row) =>
      within(row).queryByText('Transport'),
    );
    if (!transportRow) throw new Error('expected a Transport category row');

    await user.click(within(transportRow).getByRole('button', { name: /expand transport/i }));

    await waitFor(() => {
      expect(within(transportRow).getByText('No keywords yet')).toBeInTheDocument();
    });
    expect(within(transportRow).queryByTestId('keyword-chip')).not.toBeInTheDocument();
  });

  it('adds a keyword inline, calls the create endpoint, and shows a success toast', async () => {
    let createBody: Record<string, unknown> | null = null;
    server.use(
      http.post('*/category-patterns', async ({ request }) => {
        createBody = (await request.json()) as Record<string, unknown>;
        return HttpResponse.json({ id: 'created-pattern-id' }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    const toggle = await screen.findByTestId(`category-expand-${GROCERIES_ID}`);
    await user.click(toggle);

    const input = await screen.findByTestId(`add-keyword-input-${GROCERIES_ID}`);
    await user.type(input, 'KAFFEE');
    await user.click(screen.getByTestId(`add-keyword-submit-${GROCERIES_ID}`));

    await waitFor(() => {
      expect(screen.getByText('Keyword added')).toBeInTheDocument();
    });

    // Posts THIS category's id — no picker.
    expect(createBody).toEqual({ keyword: 'KAFFEE', categoryId: GROCERIES_ID });
    // Input clears on success.
    expect(input).toHaveValue('');
  });

  it('submits the inline add-keyword form on Enter', async () => {
    let createCalled = false;
    server.use(
      http.post('*/category-patterns', () => {
        createCalled = true;
        return HttpResponse.json({ id: 'created-pattern-id' }, { status: 201 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    await user.click(await screen.findByTestId(`category-expand-${GROCERIES_ID}`));
    const input = await screen.findByTestId(`add-keyword-input-${GROCERIES_ID}`);
    await user.type(input, 'BRICO{Enter}');

    await waitFor(() => {
      expect(createCalled).toBe(true);
    });
  });

  it('surfaces the 409 "keyword exists" detail verbatim via toast', async () => {
    server.use(
      http.post('*/category-patterns', () =>
        HttpResponse.json(
          {
            status: 409,
            detail: "A pattern for keyword 'LINELLA' already exists.",
          },
          { status: 409 },
        ),
      ),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    await user.click(await screen.findByTestId(`category-expand-${GROCERIES_ID}`));
    const input = await screen.findByTestId(`add-keyword-input-${GROCERIES_ID}`);
    await user.type(input, 'LINELLA');
    await user.click(screen.getByTestId(`add-keyword-submit-${GROCERIES_ID}`));

    await waitFor(() => {
      expect(
        screen.getByText("A pattern for keyword 'LINELLA' already exists."),
      ).toBeInTheDocument();
    });
  });

  it('edit opens a prefilled dialog and submitting renames via PUT + toasts', async () => {
    let putBody: Record<string, unknown> | null = null;
    let putId = '';
    server.use(
      http.put('*/categories/:id', async ({ request, params }) => {
        putId = String(params.id);
        putBody = (await request.json()) as Record<string, unknown>;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    // Find the Groceries row and click its Edit (Pencil) action.
    const groceriesRow = (await screen.findAllByTestId('category-row')).find((row) =>
      within(row).queryByText('Groceries'),
    );
    if (!groceriesRow) throw new Error('expected a Groceries category row');

    await user.click(within(groceriesRow).getByTestId('edit-category'));

    // Dialog opens prefilled with the row's current name.
    const dialog = await screen.findByTestId('edit-category-dialog');
    const nameInput = within(dialog).getByTestId('category-name-input');
    await waitFor(() => {
      expect(nameInput).toHaveValue('Groceries');
    });

    // Rename and submit.
    await user.clear(nameInput);
    await user.type(nameInput, 'Food & groceries');
    await user.click(within(dialog).getByTestId('category-edit-submit-button'));

    await waitFor(() => {
      expect(putId).toBe(GROCERIES_ID);
    });
    expect(putBody).toMatchObject({ name: 'Food & groceries', flow: 'Expense' });

    await waitFor(() => {
      expect(screen.getByText('Updated "Food & groceries"')).toBeInTheDocument();
    });
  });

  it('deletes a keyword chip immediately (no modal) and shows a success toast', async () => {
    let deleteCalled = false;
    server.use(
      http.delete('*/category-patterns/:id', () => {
        deleteCalled = true;
        return new HttpResponse(null, { status: 204 });
      }),
    );

    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);

    await user.click(await screen.findByTestId(`category-expand-${GROCERIES_ID}`));

    const container = await screen.findByTestId(`category-keywords-${GROCERIES_ID}`);
    const [firstDelete] = within(container).getAllByTestId('keyword-chip-delete');
    if (!firstDelete) throw new Error('expected at least one keyword chip');
    await user.click(firstDelete);

    await waitFor(() => {
      expect(deleteCalled).toBe(true);
    });
    await waitFor(() => {
      expect(screen.getByText(/Removed keyword/)).toBeInTheDocument();
    });
  });

  it('renders the error state when the categories query fails', async () => {
    server.use(http.get('*/categories', () => HttpResponse.json({}, { status: 500 })));
    renderWithClient(<CategoriesManager />);
    expect(await screen.findByText('Failed to load categories.')).toBeInTheDocument();
  });

  it('renders the empty state when there are no categories', async () => {
    server.use(http.get('*/categories', () => HttpResponse.json([])));
    renderWithClient(<CategoriesManager />);
    expect(await screen.findByText(/No categories yet/)).toBeInTheDocument();
  });

  it('opens then closes the Archive dialog from a category row', async () => {
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);
    await waitFor(() =>
      expect(screen.getAllByTestId('archive-category').length).toBeGreaterThan(0),
    );
    await user.click(screen.getAllByTestId('archive-category')[0] as HTMLElement);
    expect(await screen.findByTestId('archive-category-dialog')).toBeInTheDocument();
    await user.keyboard('{Escape}');
    await waitFor(() =>
      expect(screen.queryByTestId('archive-category-dialog')).not.toBeInTheDocument(),
    );
  });

  it('confirms an archive and toasts success', async () => {
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);
    await waitFor(() =>
      expect(screen.getAllByTestId('archive-category').length).toBeGreaterThan(0),
    );
    await user.click(screen.getAllByTestId('archive-category')[0] as HTMLElement);
    await user.click(await screen.findByTestId('archive-category-confirm-button'));
    // Success toast quotes the archived category name, e.g. Archived "Groceries".
    await waitFor(() => {
      expect(screen.getByText(/^Archived "/)).toBeInTheDocument();
    });
  });

  it('toasts an error when archiving a category fails', async () => {
    server.use(
      http.delete('*/categories/:id', () => HttpResponse.json({ detail: 'busy' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);
    await waitFor(() =>
      expect(screen.getAllByTestId('archive-category').length).toBeGreaterThan(0),
    );
    await user.click(screen.getAllByTestId('archive-category')[0] as HTMLElement);
    await user.click(await screen.findByTestId('archive-category-confirm-button'));
    expect(await screen.findByText('busy')).toBeInTheDocument();
  });

  it('toasts an error when editing a category fails', async () => {
    server.use(
      http.put('*/categories/:id', () => HttpResponse.json({ detail: 'nope' }, { status: 500 })),
    );
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);
    const groceriesRow = (await screen.findAllByTestId('category-row')).find((row) =>
      within(row).queryByText('Groceries'),
    );
    await user.click(within(groceriesRow as HTMLElement).getByTestId('edit-category'));
    const dialog = await screen.findByTestId('edit-category-dialog');
    await user.click(within(dialog).getByTestId('category-edit-submit-button'));
    expect(await screen.findByText('nope')).toBeInTheDocument();
  });

  it('toasts an error when removing a keyword chip fails', async () => {
    server.use(
      http.delete('*/category-patterns/:id', () =>
        HttpResponse.json({ detail: 'locked' }, { status: 500 }),
      ),
    );
    const user = userEvent.setup();
    renderWithClient(<CategoriesManager />);
    await user.click(await screen.findByTestId(`category-expand-${GROCERIES_ID}`));
    const container = await screen.findByTestId(`category-keywords-${GROCERIES_ID}`);
    const [firstDelete] = within(container).getAllByTestId('keyword-chip-delete');
    await user.click(firstDelete as HTMLElement);
    expect(await screen.findByText('locked')).toBeInTheDocument();
  });
});
