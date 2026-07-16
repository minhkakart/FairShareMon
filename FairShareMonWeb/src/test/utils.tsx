import type { ReactElement, ReactNode } from "react";
import { render } from "@testing-library/react";
import type { RenderOptions } from "@testing-library/react";
import { QueryClient, QueryClientProvider } from "@tanstack/react-query";
import { MemoryRouter } from "react-router-dom";
import { LocaleProvider } from "@/i18n/LocaleProvider";
import { ThemeProvider } from "@/theme/ThemeProvider";
import { ToastHost } from "@/app/ToastHost";
import "@/i18n";

/**
 * `renderWithProviders` — wraps a subject in the app's providers (fresh
 * QueryClient with retries off + a MemoryRouter at `initialPath`) so
 * web-test-engineer specs can drive components deterministically.
 */
export function renderWithProviders(
  ui: ReactElement,
  {
    initialPath = "/",
    ...options
  }: RenderOptions & { initialPath?: string } = {},
) {
  const queryClient = new QueryClient({
    defaultOptions: { queries: { retry: false }, mutations: { retry: false } },
  });

  function Wrapper({ children }: { children: ReactNode }) {
    return (
      <QueryClientProvider client={queryClient}>
        <LocaleProvider>
          <ThemeProvider>
            <ToastHost>
              <MemoryRouter initialEntries={[initialPath]}>
                {children}
              </MemoryRouter>
            </ToastHost>
          </ThemeProvider>
        </LocaleProvider>
      </QueryClientProvider>
    );
  }

  return render(ui, { wrapper: Wrapper, ...options });
}
