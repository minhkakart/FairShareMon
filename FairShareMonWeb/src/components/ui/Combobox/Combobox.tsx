import type { ReactNode, Ref } from "react";
import { useEffect, useId, useRef, useState } from "react";
import { cx } from "../utils/cx";
import styles from "./Combobox.module.css";

/**
 * A single option. `value` is the emitted id; `label` is the accessible name, the
 * trigger fallback, and a search target. `keywords` add extra searchable text
 * (e.g. a bank's full name / BIN / code). `meta` carries per-option data the
 * `renderOption` slot reads.
 */
export type ComboboxOption<Meta = unknown> = {
  value: string;
  label: string;
  keywords?: string[];
  meta?: Meta;
  disabled?: boolean;
};

export type ComboboxProps<Meta = unknown> = {
  /** Current value (controlled). `undefined` shows the placeholder. */
  value: string | undefined;
  /** Emits the chosen option's `value`. */
  onValueChange: (value: string) => void;
  options: ComboboxOption<Meta>[];
  /** Visible label — required for accessibility (associated via aria-labelledby). */
  label: ReactNode;
  /** Trigger text when nothing is selected (muted). */
  placeholder?: string;
  /** Search input placeholder when open. */
  searchPlaceholder?: string;
  /** "No matches" copy shown when the query filters everything out. */
  emptyLabel?: string;
  /**
   * Subtle background-refresh hint; never blocks opening or empties the list.
   * `true` shows just the spinner; a string shows the spinner + that localized
   * copy (the primitive never hardcodes text).
   */
  loading?: boolean | string;
  hint?: ReactNode;
  /** Error message — sets invalid styling + `aria-describedby`. */
  error?: ReactNode;
  required?: boolean;
  disabled?: boolean;
  hideLabelVisually?: boolean;
  /** Native form name (RHF/uncontrolled interop). */
  name?: string;
  id?: string;
  className?: string;
  /**
   * Custom renderer for an option's inner content — drives BOTH the list row and
   * the filled trigger (Select parity). When absent, falls back to `option.label`.
   * In the collapsed trigger the node is wrapped in a slot carrying the
   * `data-combobox-value` attribute, so a caller-owned stylesheet can collapse a
   * multi-line row to a single line there (see BankLogo/bankOptions).
   */
  renderOption?: (option: ComboboxOption<Meta>) => ReactNode;
  /** Forwarded to the trigger button (autoFocus / RHF focus-on-error land here). */
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

const SearchIcon = (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" aria-hidden="true">
    <circle cx="9" cy="9" r="5.5" strokeWidth="1.75" />
    <path d="M13.5 13.5L17 17" strokeWidth="1.75" strokeLinecap="round" />
  </svg>
);

const Spinner = (
  <svg viewBox="0 0 20 20" fill="none" aria-hidden="true" className={styles.spinner}>
    <circle
      cx="10"
      cy="10"
      r="7"
      stroke="currentColor"
      strokeWidth="2"
      strokeLinecap="round"
      strokeDasharray="33"
      strokeDashoffset="10"
    />
  </svg>
);

/**
 * Normalize a string for searching: lowercase, strip combining diacritics, and
 * fold đ→d. NFD does NOT decompose "đ"/"Đ" (distinct Vietnamese letters, not
 * base+combining), so the explicit fold is required — without it typing "dong a"
 * would never match "Đông Á". The fold runs AFTER lowercasing.
 */
export function normalizeForSearch(input: string): string {
  return input
    .normalize("NFD")
    .replace(/[̀-ͯ]/g, "")
    .toLowerCase()
    .replace(/đ/g, "d");
}

function optionMatches(query: string, option: ComboboxOption): boolean {
  const q = normalizeForSearch(query.trim());
  if (q === "") return true;
  const haystacks = [option.label, ...(option.keywords ?? [])];
  return haystacks.some((h) => normalizeForSearch(h).includes(q));
}

/**
 * Searchable single-select — a themed, hand-rolled ARIA-1.2 "combobox with
 * listbox popup". The collapsed control is a `Select`-style trigger; opening
 * drops a popover with a pinned search input and a scrolling listbox. Focus stays
 * on the search input while an active option is tracked via
 * `aria-activedescendant` (NOT roving tabindex — the user keeps typing). Filtering
 * is case- and diacritic-insensitive (Vietnamese-aware, incl. đ→d).
 *
 * Presentational + controlled: the parent owns `value`/`onValueChange` and passes
 * localized strings. Mirrors `SelectProps` so it drops into the same form column.
 */
export function Combobox<Meta = unknown>({
  value,
  onValueChange,
  options,
  label,
  placeholder,
  searchPlaceholder,
  emptyLabel,
  loading,
  hint,
  error,
  required,
  disabled,
  hideLabelVisually,
  name,
  id,
  className,
  renderOption,
  ref,
}: ComboboxProps<Meta>) {
  const autoId = useId();
  const fieldId = id ?? autoId;
  const labelId = `${fieldId}-label`;
  const hintId = `${fieldId}-hint`;
  const errorId = `${fieldId}-error`;
  const listboxId = `${fieldId}-listbox`;
  const invalid = Boolean(error);

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(-1);

  const rootRef = useRef<HTMLDivElement>(null);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const searchRef = useRef<HTMLInputElement>(null);
  const optionRefs = useRef<Array<HTMLLIElement | null>>([]);

  const describedBy =
    cx(hint && !invalid ? hintId : undefined, invalid ? errorId : undefined) ||
    undefined;

  const filtered = options.filter((o) => optionMatches(query, o));
  const selectedOption = options.find((o) => o.value === value);

  const firstEnabled = (list: ComboboxOption<Meta>[]): number =>
    list.findIndex((o) => !o.disabled);

  const setTriggerRef = (node: HTMLButtonElement | null) => {
    triggerRef.current = node;
    if (typeof ref === "function") ref(node);
    else if (ref) (ref as { current: HTMLButtonElement | null }).current = node;
  };

  const openPanel = () => {
    if (disabled) return;
    setQuery("");
    // Active = the currently-selected option (if visible + enabled), else first.
    const selIdx = options.findIndex((o) => o.value === value && !o.disabled);
    setActiveIndex(selIdx >= 0 ? selIdx : firstEnabled(options));
    setOpen(true);
  };

  const closePanel = (returnFocus: boolean) => {
    setOpen(false);
    if (returnFocus) triggerRef.current?.focus();
  };

  const commit = (option: ComboboxOption<Meta> | undefined) => {
    if (!option || option.disabled) return;
    onValueChange(option.value);
    closePanel(true);
  };

  // Focus the search input when the panel opens.
  useEffect(() => {
    if (open) searchRef.current?.focus();
  }, [open]);

  // Keep the active option scrolled into view (auto, never kinetic).
  useEffect(() => {
    if (open && activeIndex >= 0) {
      optionRefs.current[activeIndex]?.scrollIntoView({ block: "nearest" });
    }
  }, [open, activeIndex]);

  // Close on outside pointerdown while open (Escape is handled on the input).
  useEffect(() => {
    if (!open) return;
    const onPointerDown = (event: PointerEvent) => {
      if (!rootRef.current?.contains(event.target as Node)) setOpen(false);
    };
    document.addEventListener("pointerdown", onPointerDown);
    return () => document.removeEventListener("pointerdown", onPointerDown);
  }, [open]);

  // Close when focus leaves the whole control (Tab / Shift+Tab out) so the
  // absolutely-positioned panel never overlaps the field below the trigger.
  const onRootFocusOut = (event: React.FocusEvent) => {
    if (!open) return;
    const next = event.relatedTarget as Node | null;
    if (!next || !rootRef.current?.contains(next)) setOpen(false);
  };

  const moveActive = (direction: 1 | -1) => {
    if (filtered.length === 0) return;
    let next = activeIndex;
    for (let step = 0; step < filtered.length; step += 1) {
      next = (next + direction + filtered.length) % filtered.length;
      if (!filtered[next]?.disabled) {
        setActiveIndex(next);
        return;
      }
    }
  };

  const moveToEdge = (edge: "first" | "last") => {
    if (edge === "first") {
      const idx = filtered.findIndex((o) => !o.disabled);
      if (idx >= 0) setActiveIndex(idx);
    } else {
      for (let i = filtered.length - 1; i >= 0; i -= 1) {
        if (!filtered[i]?.disabled) {
          setActiveIndex(i);
          return;
        }
      }
    }
  };

  const onTriggerKeyDown = (event: React.KeyboardEvent) => {
    if (event.key === "ArrowDown" || event.key === "ArrowUp") {
      event.preventDefault();
      openPanel();
    }
  };

  const onSearchKeyDown = (event: React.KeyboardEvent) => {
    switch (event.key) {
      case "ArrowDown":
        event.preventDefault();
        moveActive(1);
        break;
      case "ArrowUp":
        event.preventDefault();
        moveActive(-1);
        break;
      case "Home":
        event.preventDefault();
        moveToEdge("first");
        break;
      case "End":
        event.preventDefault();
        moveToEdge("last");
        break;
      case "Enter":
        event.preventDefault();
        if (activeIndex >= 0) commit(filtered[activeIndex]);
        break;
      case "Escape":
        event.preventDefault();
        closePanel(true);
        break;
      case "Tab":
        // Let focus advance naturally (no preventDefault) but close the
        // absolutely-positioned panel so it can't overlap the field below.
        setOpen(false);
        break;
      default:
        break;
    }
  };

  const onSearchChange = (raw: string) => {
    setQuery(raw);
    const next = options.filter((o) => optionMatches(raw, o));
    setActiveIndex(firstEnabled(next));
  };

  const activeOptionId =
    open && activeIndex >= 0 && filtered[activeIndex]
      ? `${fieldId}-opt-${activeIndex}`
      : undefined;

  return (
    <div
      className={cx(styles.field, className)}
      ref={rootRef}
      onBlur={onRootFocusOut}
    >
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

      {/* Hidden input keeps RHF/native form interop (the value the form submits). */}
      {name ? <input type="hidden" name={name} value={value ?? ""} /> : null}

      <button
        type="button"
        ref={setTriggerRef}
        id={fieldId}
        className={cx(styles.trigger, invalid && styles.triggerInvalid)}
        aria-haspopup="listbox"
        aria-expanded={open}
        aria-controls={open ? listboxId : undefined}
        aria-labelledby={`${labelId} ${fieldId}`}
        aria-describedby={describedBy}
        aria-invalid={invalid || undefined}
        data-state={open ? "open" : "closed"}
        disabled={disabled}
        onClick={() => (open ? closePanel(false) : openPanel())}
        onKeyDown={onTriggerKeyDown}
      >
        <span className={styles.value} data-combobox-value="">
          {selectedOption ? (
            renderOption ? (
              renderOption(selectedOption)
            ) : (
              selectedOption.label
            )
          ) : (
            <span className={styles.placeholder}>{placeholder}</span>
          )}
        </span>
        <span className={styles.chevron}>{ChevronIcon}</span>
      </button>

      {open ? (
        <div className={styles.panel}>
          <div className={styles.search}>
            <span className={styles.searchIcon} aria-hidden="true">
              {SearchIcon}
            </span>
            <input
              ref={searchRef}
              type="text"
              role="combobox"
              className={styles.searchInput}
              value={query}
              placeholder={searchPlaceholder}
              aria-expanded="true"
              aria-controls={listboxId}
              aria-autocomplete="list"
              aria-activedescendant={activeOptionId}
              aria-labelledby={labelId}
              autoComplete="off"
              spellCheck={false}
              onChange={(event) => onSearchChange(event.target.value)}
              onKeyDown={onSearchKeyDown}
            />
          </div>

          {loading ? (
            <p className={styles.loading} aria-live="polite">
              {Spinner}
              {typeof loading === "string" ? <span>{loading}</span> : null}
            </p>
          ) : null}

          <ul
            className={styles.listbox}
            id={listboxId}
            role="listbox"
            aria-label={typeof label === "string" ? label : undefined}
          >
            {filtered.length === 0 ? (
              // Announced politely so "no matching bank" is spoken when a query
              // filters everything out (design §1.4).
              <li className={styles.empty} role="presentation" aria-live="polite">
                {emptyLabel}
              </li>
            ) : (
              filtered.map((option, index) => {
                const selected = option.value === value;
                const active = index === activeIndex;
                return (
                  <li
                    key={option.value}
                    ref={(node) => {
                      optionRefs.current[index] = node;
                    }}
                    id={`${fieldId}-opt-${index}`}
                    role="option"
                    aria-selected={selected}
                    aria-disabled={option.disabled || undefined}
                    className={cx(
                      styles.option,
                      active && styles.optionActive,
                      selected && styles.optionSelected,
                    )}
                    onPointerDown={(event) => {
                      // Keep focus on the search input; select on pointer down.
                      event.preventDefault();
                      if (!option.disabled) commit(option);
                    }}
                    onPointerMove={() => {
                      if (!option.disabled && index !== activeIndex) {
                        setActiveIndex(index);
                      }
                    }}
                  >
                    <span className={styles.optionBody}>
                      {renderOption ? renderOption(option) : option.label}
                    </span>
                    {selected ? (
                      <span className={styles.optionCheck} aria-hidden="true">
                        {CheckIcon}
                      </span>
                    ) : null}
                  </li>
                );
              })
            )}
          </ul>
        </div>
      ) : null}

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
