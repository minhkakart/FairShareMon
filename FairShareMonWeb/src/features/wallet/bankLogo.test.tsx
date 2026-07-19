import { describe, expect, it } from "vitest";
import { fireEvent, render, screen } from "@testing-library/react";
import { BankLogo } from "./components/BankLogo";

/**
 * BankLogo — a presentational logo plate: lazy `<img>` at the given `logoUrl`
 * (built server-side) that swaps to an initials/glyph fallback on error, or
 * renders the fallback directly when `logoUrl` is absent (the synthetic
 * unknown-BIN option). No network, no i18n lookups (strings arrive as props); the
 * `error` event is fired directly.
 */

const LOGO_VCB = "https://vietqr.vn/api/vietqr/images/img-vcb";
const LOGO_TCB = "https://vietqr.vn/api/vietqr/images/img-tcb";

describe("BankLogo", () => {
  it("BankLogo_WithLogoUrl_RendersLazyImgAtUrl", () => {
    render(<BankLogo logoUrl={LOGO_VCB} name="Vietcombank" alt="Vietcombank logo" />);

    const img = screen.getByRole("img", { name: "Vietcombank logo" });
    expect(img).toHaveAttribute("src", LOGO_VCB);
    expect(img).toHaveAttribute("loading", "lazy");
  });

  it("BankLogo_ImgError_SwapsToInitialsFallback", () => {
    render(<BankLogo logoUrl={LOGO_TCB} name="Techcombank" alt="Techcombank logo" />);

    const img = screen.getByRole("img", { name: "Techcombank logo" });
    fireEvent.error(img);

    // The broken image is replaced by the initials tile (never a broken-image icon).
    expect(
      screen.queryByRole("img", { name: "Techcombank logo" }),
    ).not.toBeInTheDocument();
    expect(screen.getByText("TE")).toBeInTheDocument();
  });

  it("BankLogo_NoLogoUrl_RendersInitialsFallbackDirectly", () => {
    // Diacritics preserved for display (initials are shown, not searched).
    const { container } = render(<BankLogo name="Đông Á" alt="" />);

    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(screen.getByText("ĐÁ")).toBeInTheDocument();
    expect(container.querySelector("img")).toBeNull();
  });

  it("BankLogo_NoLogoUrlNoName_RendersGlyphFallback", () => {
    const { container } = render(<BankLogo alt="" />);

    // No img and no initials — the neutral bank glyph (an svg) stands in.
    expect(screen.queryByRole("img")).not.toBeInTheDocument();
    expect(container.querySelector("svg")).not.toBeNull();
  });
});
