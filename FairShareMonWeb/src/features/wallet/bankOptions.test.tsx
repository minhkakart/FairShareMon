import { describe, expect, it } from "vitest";
import { buildBankOptions } from "./components/bankOptions";
import type { VietqrBank } from "./api/vietqrDirectoryApi";

/**
 * buildBankOptions — maps VietqrBank[] → ComboboxOption<VietqrBank>[]
 * (value=bin, label=shortName, keywords=[name, bin, code]) and DEDUPES by BIN. The
 * live directory can list two entries under one caiValue (e.g. 970452 KienLongBank)
 * while the stored value is only the BIN, so one option per BIN is correct. Pure
 * function — no network, no i18n.
 */

const KLB: VietqrBank = {
  bin: "970452",
  code: "KLB",
  name: "Ngân hàng TMCP Kiên Long",
  shortName: "KienLongBank",
  imageId: "img-klb",
};
const KLB_DUP: VietqrBank = {
  bin: "970452",
  code: "UMEE",
  name: "Ngân hàng số Umee – Kiên Long Bank",
  shortName: "KienLongBank",
  imageId: "img-umee",
};
const TCB: VietqrBank = {
  bin: "970407",
  code: "TCB",
  name: "Ngân hàng TMCP Kỹ thương Việt Nam",
  shortName: "Techcombank",
  imageId: "img-tcb",
};

describe("buildBankOptions", () => {
  it("BuildBankOptions_DuplicateBin_KeepsOnlyTheFirst", () => {
    const options = buildBankOptions([KLB, KLB_DUP, TCB]);
    expect(options).toHaveLength(2);
    const dup = options.filter((o) => o.value === "970452");
    expect(dup).toHaveLength(1);
    // The first-seen entry wins.
    expect(dup[0].meta?.imageId).toBe("img-klb");
  });

  it("BuildBankOptions_MapsValueLabelKeywordsAndMeta", () => {
    const [tcb] = buildBankOptions([TCB]);
    expect(tcb.value).toBe("970407");
    expect(tcb.label).toBe("Techcombank");
    expect(tcb.keywords).toEqual([
      "Ngân hàng TMCP Kỹ thương Việt Nam",
      "970407",
      "TCB",
    ]);
    expect(tcb.meta).toBe(TCB);
  });
});
