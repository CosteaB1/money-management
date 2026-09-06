import '@testing-library/jest-dom/vitest';
import { cleanup } from '@testing-library/react';
import { afterAll, afterEach, beforeAll } from 'vitest';
import { server } from '@/src/lib/mocks/server';

if (typeof Element !== 'undefined') {
  if (!Element.prototype.hasPointerCapture) {
    Element.prototype.hasPointerCapture = () => false;
  }
  if (!Element.prototype.releasePointerCapture) {
    Element.prototype.releasePointerCapture = () => {};
  }
  if (!Element.prototype.setPointerCapture) {
    Element.prototype.setPointerCapture = () => {};
  }
  if (!Element.prototype.scrollIntoView) {
    Element.prototype.scrollIntoView = () => {};
  }
}

// Node 26 ships an experimental `localStorage` global that shadows jsdom's and
// resolves to undefined unless the process runs with --localstorage-file, which
// crashes zustand's persist middleware (`storage.setItem` on undefined). Probe
// the ambient binding and replace it with an in-memory implementation whenever
// it is unusable, so the persisted-store suites work on any Node version.
const ambientLocalStorageWorks = (() => {
  try {
    globalThis.localStorage.setItem('__probe__', '1');
    globalThis.localStorage.removeItem('__probe__');
    return true;
  } catch {
    return false;
  }
})();

if (!ambientLocalStorageWorks) {
  const store = new Map<string, string>();
  const memoryLocalStorage = {
    get length() {
      return store.size;
    },
    clear: () => store.clear(),
    getItem: (key: string) => (store.has(key) ? store.get(key)! : null),
    key: (index: number) => [...store.keys()][index] ?? null,
    removeItem: (key: string) => {
      store.delete(key);
    },
    setItem: (key: string, value: string) => {
      store.set(key, String(value));
    },
  } as Storage;
  Object.defineProperty(globalThis, 'localStorage', {
    value: memoryLocalStorage,
    configurable: true,
    writable: true,
  });
}

if (typeof globalThis.ResizeObserver === 'undefined') {
  globalThis.ResizeObserver = class {
    observe() {}
    unobserve() {}
    disconnect() {}
  };
}

// sonner's <Toaster /> calls `window.matchMedia` on mount to honor the
// reduced-motion preference. jsdom doesn't ship it — stub a no-op matcher
// so any test that renders the Toaster (or any other component reading
// it) doesn't blow up.
if (typeof window !== 'undefined' && typeof window.matchMedia !== 'function') {
  window.matchMedia = (query: string) =>
    ({
      matches: false,
      media: query,
      onchange: null,
      addListener: () => {},
      removeListener: () => {},
      addEventListener: () => {},
      removeEventListener: () => {},
      dispatchEvent: () => false,
    }) as unknown as MediaQueryList;
}

beforeAll(() => {
  server.listen({ onUnhandledRequest: 'error' });
});

afterEach(() => {
  cleanup();
  server.resetHandlers();
});

afterAll(() => {
  server.close();
});
