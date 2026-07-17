import type { ReactNode } from "react";
import { cx } from "../utils/cx";
import styles from "./Pagination.module.css";

const PrevIcon = (
  <svg
    className={styles.arrow}
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path d="M12.5 4L6.5 10l6 6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const NextIcon = (
  <svg
    className={styles.arrow}
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2"
    aria-hidden="true"
  >
    <path d="M7.5 4l6 6-6 6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);

/** An ellipsis sentinel in the windowed page list. */
const GAP = "gap" as const;
type PageEntry = number | typeof GAP;

/**
 * Build a windowed page list: always page 1 and the last page, `siblingCount`
 * pages either side of the current, and an ellipsis where pages are skipped.
 */
function buildPages(
  page: number,
  pageCount: number,
  siblingCount: number,
): PageEntry[] {
  // Show every page when there are few enough (first + last + window + 2 gaps).
  const total = siblingCount * 2 + 5;
  if (pageCount <= total) {
    return Array.from({ length: pageCount }, (_, i) => i + 1);
  }
  const left = Math.max(2, page - siblingCount);
  const right = Math.min(pageCount - 1, page + siblingCount);
  const entries: PageEntry[] = [1];
  if (left > 2) entries.push(GAP);
  for (let p = left; p <= right; p++) entries.push(p);
  if (right < pageCount - 1) entries.push(GAP);
  entries.push(pageCount);
  return entries;
}

export interface PaginationProps {
  /** Current page, 1-based. */
  page: number;
  /** Total number of pages (>= 1). */
  pageCount: number;
  /** Called with the target 1-based page when the user navigates. */
  onPageChange: (page: number) => void;
  /** Accessible label for the `nav` landmark (localized). */
  label?: string;
  /** Localized prev/next control labels (also used as the button `aria-label`). */
  prevLabel?: string;
  nextLabel?: string;
  /**
   * Renders the "Trang X / Y" summary — pass a formatter so i18n owns the copy
   * and interpolation. Default: a plain vi-VN "Trang {page} / {pageCount}".
   */
  pageInfo?: (page: number, pageCount: number) => ReactNode;
  /** Accessible label for a numbered page button (localized, `(n) => …`). */
  pageLabel?: (n: number) => string;
  /** Render numbered page buttons (windowed). Default true. */
  showPageNumbers?: boolean;
  /** Pages to show either side of the current when windowing (default 1). */
  siblingCount?: number;
  /** Disable the whole control (e.g. while a page is fetching). */
  disabled?: boolean;
  className?: string;
}

/**
 * Pagination — the shared paged-list control (first used by the M8 admin user
 * list). Presentational + controlled: the feature owns `page` state (URL-synced
 * in admin) and refetches in `onPageChange`.
 *
 * Accessibility: a `<nav aria-label>` landmark; native `<button>`s (full keyboard
 * — Tab + Enter/Space); the current page carries `aria-current="page"` AND a
 * filled/bordered/weighted treatment (state never rests on color alone); prev is
 * disabled on page 1, next on the last page; a `role="status"` "Trang X / Y"
 * summary announces page changes politely.
 *
 * Renders nothing when there is a single page (no control needed).
 */
export function Pagination({
  page,
  pageCount,
  onPageChange,
  label = "Phân trang",
  prevLabel = "Trang trước",
  nextLabel = "Trang sau",
  pageInfo = (p, n) => `Trang ${p} / ${n}`,
  pageLabel = (n) => `Trang ${n}`,
  showPageNumbers = true,
  siblingCount = 1,
  disabled = false,
  className,
}: PaginationProps) {
  if (pageCount <= 1) return null;

  const clamped = Math.min(Math.max(page, 1), pageCount);
  const atStart = clamped <= 1;
  const atEnd = clamped >= pageCount;
  const go = (target: number) => {
    const next = Math.min(Math.max(target, 1), pageCount);
    if (next !== clamped) onPageChange(next);
  };

  return (
    <nav className={cx(styles.pagination, className)} aria-label={label}>
      <div className={styles.controls}>
        <button
          type="button"
          className={styles.pageButton}
          onClick={() => go(clamped - 1)}
          disabled={disabled || atStart}
          aria-label={prevLabel}
        >
          {PrevIcon}
        </button>

        {showPageNumbers
          ? buildPages(clamped, pageCount, siblingCount).map((entry, i) =>
              entry === GAP ? (
                <span
                  key={`gap-${i}`}
                  className={styles.ellipsis}
                  aria-hidden="true"
                >
                  …
                </span>
              ) : (
                <button
                  key={entry}
                  type="button"
                  className={cx(
                    styles.pageButton,
                    entry === clamped && styles.current,
                  )}
                  onClick={() => go(entry)}
                  disabled={disabled}
                  aria-label={pageLabel(entry)}
                  aria-current={entry === clamped ? "page" : undefined}
                >
                  {entry}
                </button>
              ),
            )
          : null}

        <button
          type="button"
          className={styles.pageButton}
          onClick={() => go(clamped + 1)}
          disabled={disabled || atEnd}
          aria-label={nextLabel}
        >
          {NextIcon}
        </button>
      </div>

      <span className={styles.info} role="status" aria-live="polite">
        {pageInfo(clamped, pageCount)}
      </span>
    </nav>
  );
}
