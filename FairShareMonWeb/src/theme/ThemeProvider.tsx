import { createContext, use, useEffect, useState } from "react";
import type { ReactNode } from "react";
import type { ThemePreference } from "@/components/ui";

export const THEME_STORAGE_KEY = "fsm.theme";

interface ThemeContextValue {
  theme: ThemePreference;
  setTheme: (theme: ThemePreference) => void;
}

const ThemeContext = createContext<ThemeContextValue | null>(null);

function isPreference(value: string | null): value is ThemePreference {
  return value === "light" || value === "dark" || value === "system";
}

export function readStoredTheme(): ThemePreference {
  try {
    const stored = window.localStorage.getItem(THEME_STORAGE_KEY);
    if (isPreference(stored)) return stored;
  } catch {
    // ignore
  }
  return "system";
}

/**
 * Implements the design system's `[data-theme]` contract: `system` removes the
 * attribute (follow the OS via prefers-color-scheme); `light`/`dark` force it
 * and always win. The initial value is also applied by a pre-paint inline
 * script in index.html to avoid a flash; this effect keeps it in sync.
 */
function applyTheme(theme: ThemePreference): void {
  const root = document.documentElement;
  if (theme === "system") {
    root.removeAttribute("data-theme");
  } else {
    root.setAttribute("data-theme", theme);
  }
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<ThemePreference>(readStoredTheme);

  useEffect(() => {
    applyTheme(theme);
  }, [theme]);

  function setTheme(next: ThemePreference) {
    try {
      window.localStorage.setItem(THEME_STORAGE_KEY, next);
    } catch {
      // persistence best-effort
    }
    setThemeState(next);
  }

  return <ThemeContext value={{ theme, setTheme }}>{children}</ThemeContext>;
}

export function useTheme(): ThemeContextValue {
  const ctx = use(ThemeContext);
  if (!ctx) throw new Error("useTheme must be used within ThemeProvider");
  return ctx;
}
