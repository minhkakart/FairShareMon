import type { ReactNode } from "react";
import type { ComboboxOption } from "@/components/ui";
import type { AppTFunction } from "@/i18n/useT";
import type { VietqrBank } from "../api/vietqrDirectoryApi";
import { BankLogo } from "./BankLogo";
import styles from "./bankOptions.module.css";

/**
 * Combobox options for the bank directory: `value = bin`, `label = shortName`,
 * `keywords = [name, bin, code]` (so "techcom" / "ky thuong" / "970407" / "tcb"
 * all match), `meta = bank`. Deduped by BIN — the directory can list two entries
 * under one caiValue (e.g. 970452 KienLongBank), and the stored value is only the
 * BIN, so a single option per BIN is correct.
 */
export function buildBankOptions(
  banks: VietqrBank[],
): ComboboxOption<VietqrBank>[] {
  const seen = new Set<string>();
  const options: ComboboxOption<VietqrBank>[] = [];
  for (const bank of banks) {
    if (seen.has(bank.bin)) continue;
    seen.add(bank.bin);
    options.push({
      value: bank.bin,
      label: bank.shortName,
      keywords: [bank.name, bank.bin, bank.code],
      meta: bank,
    });
  }
  return options;
}

/**
 * Two-line bank row: logo + short name (primary) + full legal name · BIN
 * (secondary). Drives both the listbox row and the collapsed trigger — the
 * trigger's `data-combobox-value` slot collapses it to logo + short name via
 * `bankOptions.module.css`. The logo is decorative (`alt=""`) because the row
 * already shows the bank name as text.
 */
export function makeRenderBankOption(t: AppTFunction) {
  return function renderBankOption(
    option: ComboboxOption<VietqrBank>,
  ): ReactNode {
    const meta = option.meta;
    const bin = meta?.bin ?? option.value;
    const binText = t("wallet:table.bin", { bin });
    return (
      <span className={styles.row}>
        <BankLogo imageId={meta?.imageId} name={option.label} alt="" size="md" />
        <span className={styles.text}>
          <span className={styles.primary}>{option.label}</span>
          <span className={styles.secondary}>
            {meta?.name ? (
              <>
                {meta.name} · <span className={styles.bin}>{binText}</span>
              </>
            ) : (
              <span className={styles.bin}>{binText}</span>
            )}
          </span>
        </span>
      </span>
    );
  };
}
