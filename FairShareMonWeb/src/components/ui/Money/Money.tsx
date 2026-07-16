import { cx } from "../utils/cx";
import styles from "./Money.module.css";

/** Default VND formatter — vi-VN grouping, 0 fraction digits, no float math.
 *  The implementer SHOULD inject the app's shared `formatMoneyVnd` via `format`
 *  so there is one formatter of record; this is only a self-contained fallback. */
const defaultVndFormatter = new Intl.NumberFormat("vi-VN", {
  style: "currency",
  currency: "VND",
  maximumFractionDigits: 0,
});
const formatVnd = (n: number) => defaultVndFormatter.format(n);

export type MoneyProps = {
  /** The amount as returned by the API (already computed server-side). */
  amount: number;
  /**
   * plain    — a neutral figure (expense total, share amount).
   * balance  — a signed figure: + = credit (owed to you / được nhận lại),
   *            − = debit (you owe / phải trả), 0 = settled. The sign glyph is
   *            the color-independent cue; color reinforces it.
   */
  variant?: "plain" | "balance";
  size?: "sm" | "md" | "lg" | "xl";
  /** Force a tone regardless of sign (rarely needed). */
  tone?: "default" | "muted" | "credit" | "debit" | "settled";
  /** Inject the app's shared formatter; defaults to a vi-VN VND fallback. */
  format?: (amount: number) => string;
  className?: string;
};

export function Money({
  amount,
  variant = "plain",
  size = "md",
  tone,
  format = formatVnd,
  className,
}: MoneyProps) {
  const resolvedTone =
    tone ??
    (variant === "balance"
      ? amount > 0
        ? "credit"
        : amount < 0
          ? "debit"
          : "settled"
      : "default");

  const sign = variant === "balance" && amount !== 0 ? (amount > 0 ? "+" : "−") : "";
  // Presentation only: format the magnitude, prepend the sign glyph.
  const body = format(variant === "balance" ? Math.abs(amount) : amount);

  return (
    <span
      className={cx(styles.money, styles[size], styles[resolvedTone], className)}
    >
      {sign ? (
        <span className={styles.sign} aria-hidden="true">
          {sign}
        </span>
      ) : null}
      {body}
    </span>
  );
}
