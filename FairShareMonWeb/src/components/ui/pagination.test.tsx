import { describe, expect, it, vi } from "vitest";
import { render, screen } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Pagination } from "./Pagination/Pagination";

/**
 * Pagination — the shared paged-list control (OQ4a), first used by the M8 admin
 * user list. Controlled/presentational. Proves: it renders nothing for a single
 * page; it is a `nav` landmark with an accessible name; prev is disabled on page 1
 * and next on the last page; the current page carries `aria-current="page"`; a
 * `role="status"` "Trang X / Y" summary is present; clicking prev/next/number and
 * keyboard activation emit the target page; the `disabled` prop disables the whole
 * control. Default (vi-VN) copy unless overridden.
 */

describe("Pagination", () => {
  it("Pagination_SinglePage_RendersNothing", () => {
    const { container } = render(
      <Pagination page={1} pageCount={1} onPageChange={() => {}} />,
    );
    expect(container.firstChild).toBeNull();
  });

  it("Pagination_MultiplePages_IsNavLandmarkWithName", () => {
    render(<Pagination page={1} pageCount={3} onPageChange={() => {}} />);
    expect(
      screen.getByRole("navigation", { name: "Phân trang" }),
    ).toBeInTheDocument();
  });

  it("Pagination_Page1_DisablesPrevEnablesNext", () => {
    render(<Pagination page={1} pageCount={3} onPageChange={() => {}} />);
    expect(screen.getByRole("button", { name: "Trang trước" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Trang sau" })).toBeEnabled();
  });

  it("Pagination_LastPage_DisablesNextEnablesPrev", () => {
    render(<Pagination page={3} pageCount={3} onPageChange={() => {}} />);
    expect(screen.getByRole("button", { name: "Trang sau" })).toBeDisabled();
    expect(screen.getByRole("button", { name: "Trang trước" })).toBeEnabled();
  });

  it("Pagination_CurrentPage_CarriesAriaCurrent", () => {
    render(<Pagination page={2} pageCount={3} onPageChange={() => {}} />);
    const current = screen.getByRole("button", { name: "Trang 2" });
    expect(current).toHaveAttribute("aria-current", "page");
    // A non-current numbered page does not.
    expect(screen.getByRole("button", { name: "Trang 1" })).not.toHaveAttribute(
      "aria-current",
    );
  });

  it("Pagination_Summary_ExposesPageStatus", () => {
    render(<Pagination page={2} pageCount={5} onPageChange={() => {}} />);
    const status = screen.getByRole("status");
    expect(status).toHaveTextContent("Trang 2 / 5");
  });

  it("Pagination_ClickNextAndPrev_EmitTargetPage", async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(<Pagination page={2} pageCount={5} onPageChange={onPageChange} />);

    await user.click(screen.getByRole("button", { name: "Trang sau" }));
    expect(onPageChange).toHaveBeenLastCalledWith(3);

    await user.click(screen.getByRole("button", { name: "Trang trước" }));
    expect(onPageChange).toHaveBeenLastCalledWith(1);
  });

  it("Pagination_ClickNumberedPage_EmitsThatPage", async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(<Pagination page={1} pageCount={4} onPageChange={onPageChange} />);
    await user.click(screen.getByRole("button", { name: "Trang 3" }));
    expect(onPageChange).toHaveBeenCalledWith(3);
  });

  it("Pagination_KeyboardActivation_EmitsTargetPage", async () => {
    const user = userEvent.setup();
    const onPageChange = vi.fn();
    render(<Pagination page={1} pageCount={4} onPageChange={onPageChange} />);
    const nextBtn = screen.getByRole("button", { name: "Trang sau" });
    nextBtn.focus();
    await user.keyboard("{Enter}");
    expect(onPageChange).toHaveBeenCalledWith(2);
  });

  it("Pagination_Disabled_DisablesAllControls", () => {
    render(
      <Pagination page={2} pageCount={5} onPageChange={() => {}} disabled />,
    );
    for (const btn of screen.getAllByRole("button")) {
      expect(btn).toBeDisabled();
    }
  });
});
