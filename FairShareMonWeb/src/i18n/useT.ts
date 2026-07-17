import { useTranslation } from "react-i18next";
import type { ParseKeys } from "i18next";

/** All app namespaces, so `t("auth:...")`, `t("errors:...")`, etc. are typed. */
export const APP_NAMESPACES = [
  "common",
  "auth",
  "errors",
  "validation",
  "settings",
  "members",
  "categories",
  "tags",
  "expenses",
  "events",
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

/**
 * A single key in the default `common` namespace (bare, e.g. "nav.members"),
 * narrow enough to pass straight into `t(...)`. Use this for props/config that
 * forward one common-namespace key (`AppTKey` is broader — it also admits
 * arrays/plain strings — and does not satisfy `t`'s overloads on its own).
 */
export type AppCommonKey = ParseKeys<"common">;
