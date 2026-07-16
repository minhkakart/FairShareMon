import { QueryClientProvider } from "@tanstack/react-query";
import { RouterProvider } from "react-router-dom";
import { queryClient } from "@/lib/query/queryClient";
import { LocaleProvider } from "@/i18n/LocaleProvider";
import { ThemeProvider } from "@/theme/ThemeProvider";
import { ToastHost } from "./ToastHost";
import { router } from "@/routes/router";
import { useSessionBootstrap } from "./useSessionBootstrap";

/**
 * Composes the app's cross-cutting providers around the router:
 * server cache (TanStack Query) · locale (i18n + Accept-Language) · theme
 * ([data-theme]) · toast queue. Boot rehydration runs here (outside the
 * router) so the session status is resolved before the guards render.
 */
export function AppProviders() {
  useSessionBootstrap();

  return (
    <QueryClientProvider client={queryClient}>
      <LocaleProvider>
        <ThemeProvider>
          <ToastHost>
            <RouterProvider router={router} />
          </ToastHost>
        </ThemeProvider>
      </LocaleProvider>
    </QueryClientProvider>
  );
}
