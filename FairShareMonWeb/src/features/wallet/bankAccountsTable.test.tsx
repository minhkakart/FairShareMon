import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { BankAccountsTable } from "./components/BankAccountsTable";
import { buildBankOptions } from "./components/bankOptions";
import { bankLogoUrl } from "./api/vietqrDirectoryApi";
import { VIETQR_BANKS_SNAPSHOT } from "./data/vietqrBanks";
import type { BankAccountResponse } from "./api/types";

/**
 * BankAccountsTable — the display re-derivation (R5). The bank cell shows the
 * short name re-derived from the stored BIN via the cached VietQR directory
 * (seeded instantly by the committed snapshot), falling back to the stored
 * `bankName` for a BIN not in the directory. Rendered against the real
 * `useVietqrBanks` query (snapshot as initialData → no network wait). vi-VN pinned.
 */

const noop = () => {};

/** 970407 is in the snapshot (→ "Techcombank"); the stored name is deliberately stale. */
const KNOWN: BankAccountResponse = {
  uuid: "ba-known",
  bankBin: "970407",
  bankName: "TÊN CŨ ĐÃ LƯU",
  accountNumber: "19024681012345",
  accountHolderName: "NGUYEN VAN MINH",
  isDefault: true,
  createdAt: "2026-01-01T00:00:00+00:00",
};

/** 999999 is not in the directory → the stored bankName is the fallback. */
const UNKNOWN: BankAccountResponse = {
  uuid: "ba-unknown",
  bankBin: "999999",
  bankName: "Ngân hàng Cũ",
  accountNumber: "123456",
  accountHolderName: "TRAN VAN B",
  isDefault: false,
  createdAt: "2026-01-02T00:00:00+00:00",
};

beforeEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

afterEach(async () => {
  setActiveLocale("vi-VN");
  await i18n.changeLanguage("vi-VN");
});

describe("BankAccountsTable bank re-derivation", () => {
  it("BankAccountsTable_KnownBin_ShowsReDerivedShortName", () => {
    renderWithProviders(
      <BankAccountsTable
        accounts={[KNOWN]}
        mode="free"
        onSetDefault={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );

    // Re-derived short name (from the directory), NOT the stale stored bankName.
    expect(
      screen.getByRole("rowheader", { name: /Techcombank/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("rowheader", { name: /TÊN CŨ ĐÃ LƯU/ }),
    ).not.toBeInTheDocument();
    // The BIN secondary line is preserved.
    expect(screen.getByText("BIN 970407")).toBeInTheDocument();
  });

  it("BankAccountsTable_DuplicateBin_MatchesPickerFirstWins", () => {
    // 970452 is listed TWICE in the snapshot (UMEE then KLB). The picker dedupes
    // first-wins; the table must re-derive the SAME bank so its logo/name can't
    // diverge from what the picker shows for the one BIN.
    const DUP: BankAccountResponse = {
      uuid: "ba-dup",
      bankBin: "970452",
      bankName: "KienLongBank",
      accountNumber: "0123456789",
      accountHolderName: "LE THI C",
      isDefault: false,
      createdAt: "2026-01-03T00:00:00+00:00",
    };
    const { container } = renderWithProviders(
      <BankAccountsTable
        accounts={[DUP]}
        mode="free"
        onSetDefault={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );

    const pickerMeta = buildBankOptions(VIETQR_BANKS_SNAPSHOT).find(
      (o) => o.value === "970452",
    )?.meta;
    if (!pickerMeta) throw new Error("expected a picker option for BIN 970452");

    // The table's logo is sourced from the SAME first-wins bank entry the picker
    // resolves — asserted via the logo image URL (which carries the imageId).
    const logo = container.querySelector("img");
    expect(logo?.getAttribute("src")).toBe(bankLogoUrl(pickerMeta.imageId));
  });

  it("BankAccountsTable_UnknownBin_FallsBackToStoredBankName", () => {
    renderWithProviders(
      <BankAccountsTable
        accounts={[UNKNOWN]}
        mode="free"
        onSetDefault={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );

    // No directory match → the stored bankName is shown as-is.
    expect(
      screen.getByRole("rowheader", { name: /Ngân hàng Cũ/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("BIN 999999")).toBeInTheDocument();
  });
});
