import type { ReactNode, Ref } from "react";
import { useId } from "react";
import * as RadixSelect from "@radix-ui/react-select";
import { cx } from "../utils/cx";
import styles from "./Select.module.css";

/**
 * A single option. `value` is the emitted id; `label` is the text used for the
 * accessible name, typeahead, and the trigger fallback. `meta` carries arbitrary
 * per-option data the `renderOption` slot reads (e.g. a category's color/icon, a
 * member's owner-rep / "(đã xóa)" flags).
 */
export type SelectOption<Meta = unknown> = {
  value: string;
  label: string;
  disabled?: boolean;
  meta?: Meta;
};

export type SelectProps<Meta = unknown> = {
  /** Current value (controlled). `undefined` shows the placeholder. */
  value: string | undefined;
  /** Emits the chosen option's `value`. */
  onValueChange: (value: string) => void;
  options: SelectOption<Meta>[];
  /** Visible label — required for accessibility (associated via aria-labelledby). */
  label: ReactNode;
  placeholder?: string;
  /** Helper text under the control (secondary ink). */
  hint?: ReactNode;
  /**
   * Error message. Sets the invalid styling + `aria-describedby`. Pass the
   * server `error.fields[field]` or the RHF field error straight through.
   */
  error?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  /**
   * Visually hide the label while keeping it for assistive tech (still associated
   * via aria-labelledby). Use only when an adjacent visible header labels the
   * control for sighted users (e.g. a share-editor row).
   */
  hideLabelVisually?: boolean;
  /** Native form name (RHF/uncontrolled interop; Radix renders a hidden input). */
  name?: string;
  /**
   * Custom renderer for an option's inner content. It is rendered inside Radix's
   * `ItemText`, so Radix mirrors the SELECTED option's rendered content into the
   * trigger automatically — one renderer drives both the list and the trigger.
   * Keep the node non-interactive (a marker/label), and make sure it still
   * contains the readable text so typeahead + screen readers work.
   */
  renderOption?: (option: SelectOption<Meta>) => ReactNode;
  id?: string;
  className?: string;
  /** Forwarded to the trigger button. */
  ref?: Ref<HTMLButtonElement>;
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

const CheckIcon = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <path
      d="M4 10l4 4 8-9"
      strokeWidth="2.25"
      strokeLinecap="round"
      strokeLinejoin="round"
    />
  </svg>
);

/**
 * Themed single-select built on Radix Select — inherits keyboard operation
 * (arrows, Home/End, typeahead), roving focus, portalled positioning, and full
 * ARIA (combobox/listbox). The visual layer is ours; behavior is Radix's.
 *
 * Presentational + controlled: the parent owns `value`/`onValueChange` and
 * passes localized strings. Reused by the M4 payer/category/settled pickers and
 * by M5/M6/M7. See `renderOption` for the CategoryMarker / member-metadata slot.
 */
export function Select<Meta = unknown>({
  value,
  onValueChange,
  options,
  label,
  placeholder,
  hint,
  error,
  required,
  disabled,
  hideLabelVisually,
  name,
  renderOption,
  id,
  className,
  ref,
}: SelectProps<Meta>) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const labelId = `${fieldId}-label`;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const invalid = Boolean(error);

  const describedBy =
    cx(hint && !invalid ? hintId : undefined, invalid ? errorId : undefined) ||
    undefined;

  return (
    <div className={cx(styles.field, className)}>
      <span
        className={cx(styles.label, hideLabelVisually && styles.labelHidden)}
        id={labelId}
      >
        <span>{label}</span>
        {required ? (
          <span className={styles.required} aria-hidden="true">
            *
          </span>
        ) : null}
      </span>

      <RadixSelect.Root
        value={value}
        onValueChange={onValueChange}
        disabled={disabled}
        name={name}
        required={required}
      >
        <RadixSelect.Trigger
          ref={ref}
          id={fieldId}
          className={cx(styles.trigger, invalid && styles.triggerInvalid)}
          aria-labelledby={labelId}
          aria-describedby={describedBy}
          aria-invalid={invalid || undefined}
        >
          <span className={styles.value}>
            <RadixSelect.Value placeholder={placeholder} />
          </span>
          <RadixSelect.Icon className={styles.chevron}>
            {ChevronIcon}
          </RadixSelect.Icon>
        </RadixSelect.Trigger>

        <RadixSelect.Portal>
          <RadixSelect.Content
            className={styles.content}
            position="popper"
            sideOffset={4}
            collisionPadding={8}
          >
            <RadixSelect.ScrollUpButton className={styles.scrollButton}>
              <span className={styles.scrollChevronUp}>{ChevronIcon}</span>
            </RadixSelect.ScrollUpButton>

            <RadixSelect.Viewport className={styles.viewport}>
              {options.map((option) => (
                <RadixSelect.Item
                  key={option.value}
                  value={option.value}
                  disabled={option.disabled}
                  className={styles.item}
                >
                  <span className={styles.itemBody}>
                    <RadixSelect.ItemText>
                      {renderOption ? renderOption(option) : option.label}
                    </RadixSelect.ItemText>
                  </span>
                  <RadixSelect.ItemIndicator className={styles.itemIndicator}>
                    {CheckIcon}
                  </RadixSelect.ItemIndicator>
                </RadixSelect.Item>
              ))}
            </RadixSelect.Viewport>

            <RadixSelect.ScrollDownButton className={styles.scrollButton}>
              {ChevronIcon}
            </RadixSelect.ScrollDownButton>
          </RadixSelect.Content>
        </RadixSelect.Portal>
      </RadixSelect.Root>

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
