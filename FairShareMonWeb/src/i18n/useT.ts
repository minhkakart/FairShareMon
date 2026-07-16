import { useTranslation } from "react-i18next";

/** All app namespaces, so `t("auth:...")`, `t("errors:...")`, etc. are typed. */
export const APP_NAMESPACES = [
  "common",
  "auth",
  "errors",
  "validation",
] as const;

/**
 * Shared translation hook bound to every namespace. Use everywhere instead of
 * `useTranslation()` so `t("<ns>:<key>")` is fully type-checked and the API
 * client's error/message copy resolves.
 */
export function useT() {
  return useTranslation(APP_NAMESPACES);
}

/** The exact `t` type components receive — reused by schemas + error helpers. */
export type AppTFunction = ReturnType<typeof useT>["t"];

/** The union of all valid translation keys (for props that forward a key). */
export type AppTKey = Parameters<AppTFunction>[0];
