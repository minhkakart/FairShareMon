import type { CSSProperties } from "react";
import { cx } from "../utils/cx";
import styles from "./CategoryMarker.module.css";

export type CategoryMarkerSize = "sm" | "md";

export type CategoryMarkerProps = {
  /** The category color as a hex string (`#RRGGBB`). Rendered as a tinted tile. */
  color: string;
  /**
   * The category icon — an emoji glyph stored verbatim by the backend
   * (🍜 🚗 …). When absent, the tile falls back to a solid color dot so the
   * color signal is still present.
   */
  icon?: string | null;
  /** The category name — carries meaning; color is never the sole signal. */
  name: string;
  /**
   * Render the name as visible text beside the tile (list rows, detail views).
   * When false the marker is icon-only and exposes `name` via `aria-label`.
   */
  showLabel?: boolean;
  /** Marks this as the default category — adds a star pip on the tile. */
  isDefault?: boolean;
  /**
   * Localized "default" text (e.g. "mặc định"). Appended to the accessible
   * name in icon-only mode and used as the star's title. In labelled/list mode
   * the row's own status badge carries the default text for screen readers.
   */
  defaultLabel?: string;
  size?: CategoryMarkerSize;
  className?: string;
};

const StarIcon = (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);

/**
 * A small presentational chip pairing a category's color swatch with its emoji
 * glyph and (optionally) its name. Used in the categories list Name cell and
 * reusable by expenses (M4) and chart legends (M6).
 *
 * Accessibility: the tile is always decorative (`aria-hidden`); meaning is
 * carried by the visible name (labelled mode) or an `aria-label` + `role="img"`
 * (icon-only mode). Color is paired with the glyph + name, never used alone.
 */
export function CategoryMarker({
  color,
  icon,
  name,
  showLabel = false,
  isDefault = false,
  defaultLabel,
  size = "md",
  className,
}: CategoryMarkerProps) {
  const accessibleName =
    isDefault && defaultLabel ? `${name}, ${defaultLabel}` : name;

  return (
    <span
      className={cx(styles.marker, styles[size], className)}
      role={showLabel ? undefined : "img"}
      aria-label={showLabel ? undefined : accessibleName}
    >
      <span
        className={styles.tile}
        style={{ "--marker-color": color } as CSSProperties}
        aria-hidden="true"
      >
        {icon ? (
          <span className={styles.glyph}>{icon}</span>
        ) : (
          <span className={styles.dot} />
        )}
        {isDefault ? (
          <span className={styles.star} title={defaultLabel}>
            {StarIcon}
          </span>
        ) : null}
      </span>
      {showLabel ? <span className={styles.name}>{name}</span> : null}
    </span>
  );
}
