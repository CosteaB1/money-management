'use client';

import { create } from 'zustand';
import { persist } from 'zustand/middleware';

interface UiState {
  /** Desktop-only rail width toggle (w-16 ↔ w-60). Persisted across reloads. */
  sidebarCollapsed: boolean;
  toggleSidebar: () => void;
  setSidebarCollapsed: (collapsed: boolean) => void;
  /**
   * Mobile-only navigation drawer open state (md:hidden Sheet). Ephemeral — it
   * must not survive a reload or a route change, so it is deliberately kept out
   * of the persisted slice (see `partialize` below).
   */
  mobileNavOpen: boolean;
  setMobileNavOpen: (open: boolean) => void;
  openMobileNav: () => void;
  closeMobileNav: () => void;
}

export const useUiStore = create<UiState>()(
  persist(
    (set) => ({
      sidebarCollapsed: false,
      toggleSidebar: () => set((state) => ({ sidebarCollapsed: !state.sidebarCollapsed })),
      setSidebarCollapsed: (collapsed) => set({ sidebarCollapsed: collapsed }),
      mobileNavOpen: false,
      setMobileNavOpen: (open) => set({ mobileNavOpen: open }),
      openMobileNav: () => set({ mobileNavOpen: true }),
      closeMobileNav: () => set({ mobileNavOpen: false }),
    }),
    {
      name: 'mm.ui',
      // Only the desktop rail preference is durable; the mobile drawer state is
      // transient UI that should always start closed on a fresh load.
      partialize: (state) => ({ sidebarCollapsed: state.sidebarCollapsed }),
    },
  ),
);
