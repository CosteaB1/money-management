import { render, screen, waitFor, within } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { ThemeProvider } from 'next-themes';
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest';

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
    useUiStore.setState({ sidebarCollapsed: false, mobileNavOpen: false });
    pathname.mockReturnValue('/');
  });

  it('toggles the sidebar collapsed state on click', async () => {
    const user = userEvent.setup();
    render(<Header />);
    await user.click(screen.getByTestId('sidebar-toggle'));
    expect(useUiStore.getState().sidebarCollapsed).toBe(true);
  });

  it('reflects the mobile drawer state via aria-expanded', async () => {
    const user = userEvent.setup();
    render(<Header />);
    const toggle = screen.getByTestId('sidebar-toggle');
    expect(toggle).toHaveAttribute('aria-expanded', 'false');
    await user.click(toggle);
    // The same click both collapses the desktop rail AND opens the mobile drawer.
    expect(useUiStore.getState().mobileNavOpen).toBe(true);
    expect(toggle).toHaveAttribute('aria-expanded', 'true');
  });
});

describe('SidebarDrawer (mobile nav)', () => {
  // In a real browser Next's App Router intercepts nav-link clicks and prevents
  // the native anchor navigation; jsdom has no router, so a clicked <Link> would
  // otherwise emit a noisy "Not implemented: navigation" error. Mirror the
  // router's behavior with a capturing listener so the click still fires React's
  // onClick (which is the behavior under test) without a real navigation.
  const preventNav = (e: MouseEvent) => {
    if ((e.target as HTMLElement | null)?.closest('a[href]')) e.preventDefault();
  };
  beforeEach(() => {
    useUiStore.setState({ sidebarCollapsed: false, mobileNavOpen: false });
    pathname.mockReturnValue('/');
    document.addEventListener('click', preventNav, true);
  });
  afterEach(() => {
    document.removeEventListener('click', preventNav, true);
  });

  it('opens on the header toggle and lists every nav item', async () => {
    const user = userEvent.setup();
    render(<Header />);

    // Closed initially → the drawer's portalled content is absent.
    expect(screen.queryByTestId('mobile-nav-drawer')).not.toBeInTheDocument();

    await user.click(screen.getByTestId('sidebar-toggle'));

    const drawer = await screen.findByTestId('mobile-nav-drawer');
    expect(drawer).toBeInTheDocument();
    // The accessible dialog name is present for screen readers.
    expect(screen.getByText('Navigation menu')).toBeInTheDocument();

    // Every NAV_ITEM renders inside the drawer, keyed by its nav-* testid.
    for (const testid of [
      'nav-dashboard',
      'nav-accounts',
      'nav-transactions',
      'nav-budgets',
      'nav-goals',
      'nav-reports',
      'nav-fx-rates',
      'nav-data',
      'nav-settings',
    ]) {
      expect(within(drawer).getByTestId(testid)).toBeInTheDocument();
    }
  });

  it('closes when a nav item is clicked', async () => {
    const user = userEvent.setup();
    render(<Header />);
    await user.click(screen.getByTestId('sidebar-toggle'));

    const drawer = await screen.findByTestId('mobile-nav-drawer');
    await user.click(within(drawer).getByTestId('nav-accounts'));

    // onNavigate → closeMobileNav() flips the store and Radix unmounts the panel.
    expect(useUiStore.getState().mobileNavOpen).toBe(false);
    await waitFor(() => {
      expect(screen.queryByTestId('mobile-nav-drawer')).not.toBeInTheDocument();
    });
  });

  it('closes on a route change', async () => {
    const user = userEvent.setup();
    const { rerender } = render(<Header />);
    await user.click(screen.getByTestId('sidebar-toggle'));
    await screen.findByTestId('mobile-nav-drawer');
    expect(useUiStore.getState().mobileNavOpen).toBe(true);

    // Simulate navigation: the pathname changes and the component re-renders.
    // The drawer's usePathname effect fires and closes it.
    pathname.mockReturnValue('/accounts');
    rerender(<Header />);

    expect(useUiStore.getState().mobileNavOpen).toBe(false);
    await waitFor(() => {
      expect(screen.queryByTestId('mobile-nav-drawer')).not.toBeInTheDocument();
    });
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
