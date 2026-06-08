import { beforeEach, describe, expect, it } from 'vitest';
import { useUiStore } from '@/src/lib/stores/ui-store';

describe('useUiStore', () => {
  beforeEach(() => {
    useUiStore.setState({ sidebarCollapsed: false });
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
});
