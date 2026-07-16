/**
 * Tiny class-name joiner — no dependency. Filters out falsy values so
 * conditional classes read cleanly: cx(styles.base, active && styles.active).
 */
export function cx(...parts: Array<string | false | null | undefined>): string {
  return parts.filter(Boolean).join(" ");
}
