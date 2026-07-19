import type { ReactNode } from "react";
import type { ComboboxOption } from "@/components/ui";
import type { AppTFunction } from "@/i18n/useT";
import type { Bank } from "../api/banksApi";
import { BankLogo } from "./BankLogo";
import styles from "./bankOptions.module.css";

/**
 * Dedupe a bank directory by BIN, FIRST-wins. The directory can list two entries
 * under one caiValue (e.g. 970452 UMEE then KLB), but the stored value is only the
 * BIN, so a single canonical bank per BIN is correct. This is the ONE dedup rule —
 * both the picker (`buildBankOptions`) and the accounts-table lookup must derive
 * from it so the logo/legal-name they show for a duplicate BIN can never diverge.
 */
export function dedupeBanksByBin(banks: Bank[]): Bank[] {
  const seen = new Set<string>();
  const deduped: Bank[] = [];
  for (const bank of banks) {
    if (seen.has(bank.bin)) continue;
    seen.add(bank.bin);
    deduped.push(bank);
  }
  return deduped;
}

/**
 * Combobox options for the bank directory: `value = bin`, `label = shortName`,
 * `keywords = [name, bin, code]` (so "techcom" / "ky thuong" / "970407" / "tcb"
 * all match), `meta = bank`. Deduped by BIN (FIRST-wins via `dedupeBanksByBin`),
 * so a single option per BIN is correct.
 */
export function buildBankOptions(
  banks: Bank[],
): ComboboxOption<Bank>[] {
  return dedupeBanksByBin(banks).map((bank) => ({
    value: bank.bin,
    label: bank.shortName,
    keywords: [bank.name, bank.bin, bank.code],
    meta: bank,
  }));
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
    option: ComboboxOption<Bank>,
  ): ReactNode {
    const meta = option.meta;
    const bin = meta?.bin ?? option.value;
    const binText = t("wallet:table.bin", { bin });
    return (
      <span className={styles.row}>
        <BankLogo logoUrl={meta?.logoUrl} name={option.label} alt="" size="md" />
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
