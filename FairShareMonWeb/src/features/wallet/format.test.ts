import { describe, expect, it } from "vitest";
import { groupAccount, maskAccount } from "./format";

/**
 * Pure presentation helpers behind the OQ5a masked-number + reveal. `maskAccount`
 * shows a fixed dot run + last 4; `groupAccount` inserts a space every 4 chars for
 * the revealed form. Never numeric math on the account number (it is an identifier).
 */
describe("maskAccount", () => {
  it("MaskAccount_LongNumber_ShowsDotsAndLastFour", () => {
    expect(maskAccount("0071001234567")).toBe("•••• 4567");
  });

  it("MaskAccount_DifferentNumber_ShowsItsOwnLastFour", () => {
    expect(maskAccount("19024681012345")).toBe("•••• 2345");
  });
});

describe("groupAccount", () => {
  it("GroupAccount_Number_InsertsSpaceEveryFourChars", () => {
    expect(groupAccount("0071001234567")).toBe("0071 0012 3456 7");
  });

  it("GroupAccount_ExactMultipleOfFour_HasNoTrailingSpace", () => {
    expect(groupAccount("19024681012345")).toBe("1902 4681 0123 45");
  });
});
