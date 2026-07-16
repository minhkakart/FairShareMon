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
 *
 * Pass `queryClient` to reuse a specific client instance — needed when a spec
 * exercises the app's singleton `queryClient` (e.g. the `invalidateCurrentUser`
 * seam, or asserting a query is served from cache across remounts). Omit it for
 * the default per-render fresh, retry-off client.
 */
export function renderWithProviders(
  ui: ReactElement,
  {
    initialPath = "/",
    queryClient: providedClient,
    ...options
  }: RenderOptions & {
    initialPath?: string;
    queryClient?: QueryClient;
  } = {},
) {
  const queryClient =
    providedClient ??
    new QueryClient({
      defaultOptions: {
        queries: { retry: false },
        mutations: { retry: false },
      },
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
