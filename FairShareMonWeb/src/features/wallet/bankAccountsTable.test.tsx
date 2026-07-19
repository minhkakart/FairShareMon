import { afterEach, beforeEach, describe, expect, it } from "vitest";
import { screen } from "@testing-library/react";
import { renderWithProviders } from "@/test/utils";
import { setActiveLocale } from "@/lib/api/runtime";
import i18n from "@/i18n";
import { BankAccountsTable } from "./components/BankAccountsTable";
import type { BankAccountResponse } from "./api/types";

/**
 * BankAccountsTable — the display re-derivation (R5). The bank cell shows the
 * short name re-derived from the stored BIN via the cached `/v1/banks` directory
 * query, falling back to the stored `bankName` for a BIN not in the directory.
 * Rendered against the real `useBanks` query (mocked at the client boundary by the
 * shared MSW `/api/v1/banks` handler → 970436/970407/970418/970422). vi-VN pinned.
 */

const noop = () => {};

/** 970407 is in the directory (→ "Techcombank"); the stored name is deliberately stale. */
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
  it("BankAccountsTable_KnownBin_ShowsReDerivedShortName", async () => {
    renderWithProviders(
      <BankAccountsTable
        accounts={[KNOWN]}
        mode="free"
        onSetDefault={noop}
        onEdit={noop}
        onDelete={noop}
      />,
    );

    // Once the directory query resolves, the re-derived short name replaces the
    // stale stored bankName (the BIN secondary line is preserved).
    expect(
      await screen.findByRole("rowheader", { name: /Techcombank/ }),
    ).toBeInTheDocument();
    expect(
      screen.queryByRole("rowheader", { name: /TÊN CŨ ĐÃ LƯU/ }),
    ).not.toBeInTheDocument();
    expect(screen.getByText("BIN 970407")).toBeInTheDocument();
  });

  it("BankAccountsTable_UnknownBin_FallsBackToStoredBankName", async () => {
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
      await screen.findByRole("rowheader", { name: /Ngân hàng Cũ/ }),
    ).toBeInTheDocument();
    expect(screen.getByText("BIN 999999")).toBeInTheDocument();
  });
});
