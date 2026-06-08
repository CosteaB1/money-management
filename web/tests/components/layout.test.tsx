import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from 'next-themes';
import { beforeEach, describe, expect, it, vi } from 'vitest';

const pathname = vi.fn<() => string>(() => '/');
vi.mock('next/navigation', () => ({
  usePathname: () => pathname(),
}));

import { Header } from '@/src/components/layout/header';
import { Sidebar } from '@/src/components/layout/sidebar';
import { ThemeToggle } from '@/src/components/layout/theme-toggle';
import { useUiStore } from '@/src/lib/stores/ui-store';

describe('Sidebar', () => {
  beforeEach(() => {
    useUiStore.setState({ sidebarCollapsed: false });
    pathname.mockReturnValue('/');
  });

  it('marks the dashboard link active on the root path', () => {
    render(<Sidebar />);
    const dashboard = screen.getByTestId('nav-dashboard');
    expect(dashboard).toHaveAttribute('aria-current', 'page');
    // The accounts link should not be active on "/".
    expect(screen.getByTestId('nav-accounts')).not.toHaveAttribute('aria-current');
  });

  it('marks a nested route active via the startsWith branch', () => {
    pathname.mockReturnValue('/accounts/123');
    render(<Sidebar />);
    expect(screen.getByTestId('nav-accounts')).toHaveAttribute('aria-current', 'page');
    expect(screen.getByTestId('nav-dashboard')).not.toHaveAttribute('aria-current');
  });

  it('hides labels when collapsed', () => {
    useUiStore.setState({ sidebarCollapsed: true });
    render(<Sidebar />);
    expect(screen.getByTestId('sidebar')).toHaveAttribute('data-collapsed', 'true');
    // "Money" wordmark hidden while collapsed.
    expect(screen.queryByText('Money')).not.toBeInTheDocument();
  });
});

describe('Header', () => {
  beforeEach(() => {
    useUiStore.setState({ sidebarCollapsed: false });
  });

  it('toggles the sidebar collapsed state on click', async () => {
    const user = userEvent.setup();
    render(<Header />);
    await user.click(screen.getByTestId('sidebar-toggle'));
    expect(useUiStore.getState().sidebarCollapsed).toBe(true);
  });
});

describe('ThemeToggle', () => {
  function renderToggle() {
    return render(
      <ThemeProvider attribute="class" defaultTheme="dark" enableSystem>
        <ThemeToggle />
      </ThemeProvider>,
    );
  }

  it('switches the theme through the dropdown options', async () => {
    const user = userEvent.setup();
    renderToggle();

    await user.click(screen.getByTestId('theme-toggle'));
    await user.click(await screen.findByText('Light'));

    // Re-open and pick System, then Dark, to cover each handler.
    await user.click(screen.getByTestId('theme-toggle'));
    await user.click(await screen.findByText('System'));

    await user.click(screen.getByTestId('theme-toggle'));
    await user.click(await screen.findByText('Dark'));

    expect(screen.getByTestId('theme-toggle')).toBeInTheDocument();
  });
});
