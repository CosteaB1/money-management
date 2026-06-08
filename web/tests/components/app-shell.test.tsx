import { render, screen } from '@testing-library/react';
import { describe, expect, it, vi } from 'vitest';

vi.mock('next/navigation', () => ({
  usePathname: () => '/',
}));

import RootLayout from '@/app/layout';
import { Providers } from '@/app/providers';

describe('Providers', () => {
  it('renders children inside the theme + query + toaster providers', () => {
    render(
      <Providers>
        <span data-testid="child">hi</span>
      </Providers>,
    );
    expect(screen.getByTestId('child')).toHaveTextContent('hi');
  });
});

describe('RootLayout', () => {
  it('renders the sidebar, header and the page content', () => {
    // RootLayout emits its own <html>/<body>; React hoists those into the
    // document rather than the render container, so query the document.
    render(
      <RootLayout>
        <span data-testid="page">content</span>
      </RootLayout>,
    );
    expect(screen.getByTestId('page')).toBeInTheDocument();
    expect(document.querySelector('[data-testid="sidebar"]')).not.toBeNull();
    expect(document.querySelector('[data-testid="sidebar-toggle"]')).not.toBeNull();
  });
});
