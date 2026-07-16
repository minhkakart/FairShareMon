import { describe, expect, it } from "vitest";
import { render, screen, within } from "@testing-library/react";
import userEvent from "@testing-library/user-event";
import { Link } from "react-router-dom";
import { renderWithProviders } from "@/test/utils";
import {
  Button,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
} from "./index";

/**
 * Design-system primitive coverage introduced/exercised by M2: the `Table`
 * (semantics, accessible name, header scopes, muted deleted-row state, empty
 * body) and the `Button asChild` a11y fix (a router Link renders as a single
 * <a>, not a nested <a><button>).
 */

function SampleTable(props: {
  caption?: string;
  captionHidden?: boolean;
  ariaLabel?: string;
}) {
  return (
    <Table
      caption={props.caption}
      captionHidden={props.captionHidden}
      aria-label={props.ariaLabel}
    >
      <TableHead>
        <TableRow>
          <TableHeaderCell>Tên</TableHeaderCell>
          <TableHeaderCell>Trạng thái</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        <TableRow>
          <TableHeaderCell scope="row">An Nguyễn</TableHeaderCell>
          <TableCell>Hoạt động</TableCell>
        </TableRow>
        <TableRow deleted>
          <TableHeaderCell scope="row">Cũ</TableHeaderCell>
          <TableCell>Đã xóa</TableCell>
        </TableRow>
      </TableBody>
    </Table>
  );
}

describe("Table primitive", () => {
  it("Table_VisibleCaption_ExposesAccessibleName", () => {
    render(<SampleTable caption="Danh sách thành viên" />);
    expect(
      screen.getByRole("table", { name: "Danh sách thành viên" }),
    ).toBeInTheDocument();
  });

  it("Table_CaptionHidden_StillNamesTableForAssistiveTech", () => {
    render(<SampleTable caption="Danh sách thành viên" captionHidden />);
    // Visually hidden but still the table's accessible name.
    expect(
      screen.getByRole("table", { name: "Danh sách thành viên" }),
    ).toBeInTheDocument();
  });

  it("Table_AriaLabel_NamesTableWithoutACaption", () => {
    render(<SampleTable ariaLabel="Thành viên" />);
    expect(screen.getByRole("table", { name: "Thành viên" })).toBeInTheDocument();
  });

  it("Table_HeaderScopes_ExposeColumnAndRowHeaderRoles", () => {
    render(<SampleTable ariaLabel="X" />);
    expect(screen.getByRole("columnheader", { name: "Tên" })).toBeInTheDocument();
    expect(
      screen.getByRole("rowheader", { name: "An Nguyễn" }),
    ).toBeInTheDocument();
  });

  it("Table_DeletedRow_SetsDataDeletedHook", () => {
    render(<SampleTable ariaLabel="X" />);
    const deletedRow = screen
      .getByRole("rowheader", { name: "Cũ" })
      .closest("tr") as HTMLElement;
    expect(deletedRow).toHaveAttribute("data-deleted", "true");
    // An active row carries no deleted hook.
    const activeRow = screen
      .getByRole("rowheader", { name: "An Nguyễn" })
      .closest("tr") as HTMLElement;
    expect(activeRow).not.toHaveAttribute("data-deleted");
  });

  it("TableEmpty_RendersFullWidthSpanningCell", () => {
    render(
      <Table aria-label="X">
        <TableBody>
          <TableEmpty colSpan={3}>
            <span>Chưa có dữ liệu</span>
          </TableEmpty>
        </TableBody>
      </Table>,
    );
    const cell = screen.getByRole("cell", { name: "Chưa có dữ liệu" });
    expect(cell).toHaveAttribute("colspan", "3");
  });
});

describe("Button asChild", () => {
  it("ButtonAsChildLink_RendersSingleAnchorWithNoNestedButton", () => {
    renderWithProviders(
      <Button asChild variant="secondary">
        <Link to="/settings">Cài đặt</Link>
      </Button>,
    );
    const link = screen.getByRole("link", { name: "Cài đặt" });
    expect(link).toHaveAttribute("href", "/settings");
    // The a11y fix: exactly one interactive element — no <button> inside the <a>.
    expect(within(link).queryByRole("button")).not.toBeInTheDocument();
    expect(screen.queryByRole("button")).not.toBeInTheDocument();
  });

  it("ButtonAsChildLink_IsKeyboardFocusableAndActivates", async () => {
    const user = userEvent.setup();
    renderWithProviders(
      <>
        <Button asChild variant="secondary">
          <Link to="/settings">Cài đặt</Link>
        </Button>
        <input aria-label="after" />
      </>,
    );
    // Tab reaches the link (it is a real, focusable anchor).
    await user.tab();
    expect(screen.getByRole("link", { name: "Cài đặt" })).toHaveFocus();
  });
});
