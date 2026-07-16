import type { Locale } from "@/components/ui";

/**
 * Framework-agnostic wiring the API client reads at request time but that is
 * OWNED by React providers:
 *  - the active locale → `Accept-Language` header (set by LocaleProvider);
 *  - a "session expired" callback → router redirect to /login (set by the shell).
 * Keeping these behind setters lets the client stay free of React/router deps.
 */

let activeLocale: Locale = "vi-VN";

export function setActiveLocale(locale: Locale): void {
  activeLocale = locale;
}

export function getActiveLocale(): Locale {
  return activeLocale;
}

/** IANA timezone for the `X-Time-Zone` header. */
export function getTimeZone(): string {
  return Intl.DateTimeFormat().resolvedOptions().timeZone;
}

type SessionExpiredHandler = () => void;
let sessionExpiredHandler: SessionExpiredHandler | null = null;

/** The router shell registers a navigate-to-login callback here. */
export function registerSessionExpiredHandler(
  handler: SessionExpiredHandler | null,
): void {
  sessionExpiredHandler = handler;
}

export function notifySessionExpired(): void {
  sessionExpiredHandler?.();
}
