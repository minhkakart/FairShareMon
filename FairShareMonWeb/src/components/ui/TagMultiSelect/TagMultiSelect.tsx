import type { ReactNode } from "react";
import { useEffect, useId, useRef, useState } from "react";
import { cx } from "../utils/cx";
import styles from "./TagMultiSelect.module.css";

export type TagOption = {
  value: string;
  label: string;
};

export type TagMultiSelectProps = {
  /** Selected tag ids (controlled). */
  value: string[];
  /** Emits the next selected-id array. */
  onChange: (value: string[]) => void;
  options: TagOption[];
  /** Visible label — required for accessibility. */
  label: ReactNode;
  /** Shown in the control when nothing is selected. */
  placeholder?: string;
  hint?: ReactNode;
  /** Error message — sets invalid styling + aria-describedby. */
  error?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  id?: string;
  /** Localized label for the open/close toggle (default "Chọn nhãn"). */
  toggleLabel?: string;
  /** Localized builder for a chip's remove button (default `Bỏ "{label}"`). */
  removeLabel?: (label: string) => string;
  /** Localized empty-list message (default "Chưa có nhãn nào"). */
  emptyLabel?: string;
  className?: string;
};

const ChevronIcon = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <path
      d="M5.5 8l4.5 4 4.5-4"
      strokeWidth="1.75"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  </svg>
);

const RemoveIcon = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <path
      d="M5.5 5.5l9 9M14.5 5.5l-9 9"
      strokeWidth="1.75"
      strokeLinecap="round"
    />
  </svg>
);

/**
 * Multi-select for the tag set (OQ9a): the selected tags read as removable chips
 * in the control, and a checkbox-list popover toggles membership. `value` is an
 * array of tag ids. Native checkboxes keep the list fully keyboard-operable;
 * chips are removable via keyboard; the popover closes on Escape / outside click.
 *
 * Presentational + controlled: the parent owns `value`/`onChange` (wire to RHF)
 * and passes the active tag `options` + localized strings.
 */
export function TagMultiSelect({
  value,
  onChange,
  options,
  label,
  placeholder,
  hint,
  error,
  required,
  disabled,
  id,
  toggleLabel = "Chọn nhãn",
  removeLabel = (l) => `Bỏ ${l}`,
  emptyLabel = "Chưa có nhãn nào",
  className,
}: TagMultiSelectProps) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const labelId = `${fieldId}-label`;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const listId = `${fieldId}-list`;
  const invalid = Boolean(error);

  const [open, setOpen] = useState(false);
  const rootRef = useRef<HTMLDivElement>(null);

  // Close on outside pointerdown / Escape while open.
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    const onKeyDown = (event: KeyboardEvent) => {
      if (event.key === "Escape") setOpen(false);
    };
    document.addEventListener("pointerdown", onPointerDown);
    document.addEventListener("keydown", onKeyDown);
    return () => {
      document.removeEventListener("pointerdown", onPointerDown);
      document.removeEventListener("keydown", onKeyDown);
    };
  }, [open]);

  const selectedSet = new Set(value);
  // Chips render in options order for a stable, scannable arrangement.
  const selectedOptions = options.filter((o) => selectedSet.has(o.value));

  const toggle = (optionValue: string) => {
    onChange(
      selectedSet.has(optionValue)
        ? value.filter((v) => v !== optionValue)
        : [...value, optionValue],
    );
  };

  const describedBy =
    cx(hint && !invalid ? hintId : undefined, invalid ? errorId : undefined) ||
    undefined;

  return (
    <div className={cx(styles.field, className)} ref={rootRef}>
      <span className={styles.label} id={labelId}>
        <span>{label}</span>
        {required ? (
          <span className={styles.required} aria-hidden="true">
            *
          </span>
        ) : null}
      </span>

      <div
        className={cx(styles.control, invalid && styles.controlInvalid)}
        role="group"
        aria-labelledby={labelId}
      >
        {selectedOptions.length > 0 ? (
          <ul className={styles.chips}>
            {selectedOptions.map((option) => (
              <li key={option.value} className={styles.chip}>
                <span className={styles.chipLabel}>{option.label}</span>
                <button
                  type="button"
                  className={styles.chipRemove}
                  aria-label={removeLabel(option.label)}
                  disabled={disabled}
                  onClick={() => toggle(option.value)}
                >
                  {RemoveIcon}
                </button>
              </li>
            ))}
          </ul>
        ) : null}

        <button
          type="button"
          className={styles.toggle}
          aria-haspopup="true"
          aria-expanded={open}
          aria-controls={listId}
          aria-describedby={describedBy}
          disabled={disabled}
          onClick={() => setOpen((o) => !o)}
        >
          <span
            className={cx(
              styles.toggleText,
              selectedOptions.length === 0 && styles.togglePlaceholder,
            )}
          >
            {selectedOptions.length === 0 ? (placeholder ?? toggleLabel) : toggleLabel}
          </span>
          <span className={styles.chevron}>{ChevronIcon}</span>
        </button>

        {open ? (
          // A group of native checkboxes — each is fully keyboard-operable and
          // self-labels; no listbox/option roles are layered over the checkboxes.
          <div
            className={styles.panel}
            id={listId}
            role="group"
            aria-labelledby={labelId}
          >
            {options.length === 0 ? (
              <p className={styles.empty}>{emptyLabel}</p>
            ) : (
              <ul className={styles.optionList}>
                {options.map((option) => {
                  const checked = selectedSet.has(option.value);
                  return (
                    <li key={option.value}>
                      <label className={styles.option}>
                        <input
                          type="checkbox"
                          className={styles.checkbox}
                          checked={checked}
                          onChange={() => toggle(option.value)}
                        />
                        <span className={styles.optionLabel}>
                          {option.label}
                        </span>
                      </label>
                    </li>
                  );
                })}
              </ul>
            )}
          </div>
        ) : null}
      </div>

      {hint && !invalid ? (
        <p id={hintId} className={styles.hint}>
          {hint}
        </p>
      ) : null}

      {invalid ? (
        <p id={errorId} className={styles.error} role="alert">
          {error}
        </p>
      ) : null}
    </div>
  );
}
