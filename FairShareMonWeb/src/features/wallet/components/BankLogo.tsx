import type { CSSProperties } from "react";
import { useState } from "react";
import { cx } from "@/components/ui";
import styles from "./BankLogo.module.css";

export type BankLogoProps = {
  /** Fully-built bank logo URL; falsy → the fallback tile is rendered directly (no network). */
  logoUrl?: string;
  /**
   * i18n alt text. Pass `""` when a sibling already labels the bank (option rows,
   * table cells) so the row is not announced twice; pass the meaningful string
   * only when the logo stands alone.
   */
  alt: string;
  /** Short/legal name → fallback initials. */
  name?: string;
  size?: "sm" | "md" | "lg";
  className?: string;
};

const SIZES: Record<NonNullable<BankLogoProps["size"]>, string> = {
  sm: "1.5rem",
  md: "1.75rem",
  lg: "2.5rem",
};

const BankGlyph = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <path
      d="M10 3l6 3H4l6-3zM5 8v6M9 8v6M11 8v6M15 8v6M3.5 16.5h13"
      strokeWidth="1.4"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  </svg>
);

/** Display initials from the short/legal name (diacritics preserved — shown, not searched). */
function initialsOf(name: string): string {
  const words = name.trim().split(/\s+/).filter(Boolean);
  if (words.length === 0) return "";
  if (words.length === 1) return words[0].slice(0, 2).toUpperCase();
  return (words[0][0] + words[1][0]).toUpperCase();
}

/**
 * A small square logo plate for a bank. Lazy-loads the bank logo and, on error
 * (or when `logoUrl` is absent — the synthetic unknown-BIN option), swaps to a
 * neutral initials tile (or a bank glyph when no name is available). The loaded
 * plate is a FIXED light background in both themes (bank artwork is drawn for
 * light grounds), with a theme-aware hairline border; the fallback tile is fully
 * theme-aware. Presentational only — strings arrive as props (OQ-D2).
 */
export function BankLogo({ logoUrl, alt, name, size = "md", className }: BankLogoProps) {
  const [errored, setErrored] = useState(false);
  const showFallback = !logoUrl || errored;
  const style = { "--bank-logo-size": SIZES[size] } as CSSProperties;

  if (showFallback) {
    const initials = name ? initialsOf(name) : "";
    return (
      <span
        className={cx(styles.plate, styles.fallback, styles[size], className)}
        style={style}
      >
        {initials ? (
          <span className={styles.initials} aria-hidden="true">
            {initials}
          </span>
        ) : (
          <span className={styles.glyph} aria-hidden="true">
            {BankGlyph}
          </span>
        )}
      </span>
    );
  }

  return (
    <span className={cx(styles.plate, styles[size], className)} style={style}>
      <img
        className={styles.img}
        src={logoUrl}
        alt={alt}
        loading="lazy"
        onError={() => setErrored(true)}
      />
    </span>
  );
}
