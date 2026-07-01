import { beforeEach, describe, expect, it } from 'vitest';
import { useUiStore } from '@/src/lib/stores/ui-store';

describe('useUiStore', () => {
  beforeEach(() => {
    useUiStore.setState({ sidebarCollapsed: false, mobileNavOpen: false });
  });

  it('starts uncollapsed', () => {
    expect(useUiStore.getState().sidebarCollapsed).toBe(false);
  });

  it('toggles the collapsed flag', () => {
    useUiStore.getState().toggleSidebar();
    expect(useUiStore.getState().sidebarCollapsed).toBe(true);
    useUiStore.getState().toggleSidebar();
    expect(useUiStore.getState().sidebarCollapsed).toBe(false);
  });

  it('sets the collapsed flag explicitly', () => {
    useUiStore.getState().setSidebarCollapsed(true);
    expect(useUiStore.getState().sidebarCollapsed).toBe(true);
  });

  it('starts with the mobile nav closed', () => {
    expect(useUiStore.getState().mobileNavOpen).toBe(false);
  });

  it('opens and closes the mobile nav via the helpers', () => {
    useUiStore.getState().openMobileNav();
    expect(useUiStore.getState().mobileNavOpen).toBe(true);
    useUiStore.getState().closeMobileNav();
    expect(useUiStore.getState().mobileNavOpen).toBe(false);
  });

  it('sets the mobile nav open state explicitly', () => {
    useUiStore.getState().setMobileNavOpen(true);
    expect(useUiStore.getState().mobileNavOpen).toBe(true);
    useUiStore.getState().setMobileNavOpen(false);
    expect(useUiStore.getState().mobileNavOpen).toBe(false);
  });
});
