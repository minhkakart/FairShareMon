import type { AppCommonKey } from "@/i18n/useT";
import {
  useCurrentUser,
  useProfileStatus,
} from "@/features/auth/hooks/useAuth";

/**
 * Navigation-registration pattern (M1).
 *
 * `NAV_ENTRIES` is the SINGLE source of truth for the app's primary navigation.
 * The shell (`AppShellLayout`) and the home quick-links both render from it, and
 * `useNavEntries()` applies the role filter. Registering a later milestone's area
 * in the nav is a one-line addition to this array — nothing else to touch.
 *
 * Keys are bare `common`-namespace keys (the default namespace), e.g.
 * `"nav.members"` — pass them straight to `t(...)`.
 *
 * - `to`            — the route path (already registered in `router.tsx`).
 * - `labelKey`      — i18n key for the nav label (nav.*).
 * - `descriptionKey`— i18n key for the home quick-link one-liner (home.quickLinks.*).
 * - `requiresAdmin` — admin-only; hidden unless the resolved profile is ADMIN
 *                     (fail-safe: absent/unknown role never shows it).
 * - `end`           — NavLink exact-match for index-like routes.
 */
export interface NavEntry {
  to: string;
  labelKey: AppCommonKey;
  descriptionKey?: AppCommonKey;
  requiresAdmin?: boolean;
  end?: boolean;
}

export const NAV_ENTRIES: readonly NavEntry[] = [
  { to: "/dashboard", labelKey: "nav.dashboard", end: true },
  {
    to: "/members",
    labelKey: "nav.members",
    descriptionKey: "home.quickLinks.members",
  },
  {
    to: "/categories",
    labelKey: "nav.categories",
    descriptionKey: "home.quickLinks.categories",
  },
  {
    to: "/tags",
    labelKey: "nav.tags",
    descriptionKey: "home.quickLinks.tags",
  },
  {
    to: "/expenses",
    labelKey: "nav.expenses",
    descriptionKey: "home.quickLinks.expenses",
  },
  {
    to: "/events",
    labelKey: "nav.events",
    descriptionKey: "home.quickLinks.events",
  },
  {
    to: "/stats",
    labelKey: "nav.stats",
    descriptionKey: "home.quickLinks.stats",
  },
  {
    to: "/wallet",
    labelKey: "nav.wallet",
    descriptionKey: "home.quickLinks.wallet",
  },
  {
    to: "/admin",
    labelKey: "nav.admin",
    descriptionKey: "home.quickLinks.admin",
    requiresAdmin: true,
  },
];

/**
 * The nav entries visible to the current user. Admin-only entries are filtered
 * out unless the profile has resolved to `role === "ADMIN"` — mirroring
 * `AdminRoute`'s fail-safe: while the profile is still resolving (or on a
 * degraded `error` state, or an unknown role) admin entries stay hidden.
 */
export function useNavEntries(): readonly NavEntry[] {
  const user = useCurrentUser();
  const profileStatus = useProfileStatus();
  const isAdmin = profileStatus === "resolved" && user?.role === "ADMIN";
  return NAV_ENTRIES.filter((entry) => !entry.requiresAdmin || isAdmin);
}
