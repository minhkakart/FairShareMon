import type {
  HTMLAttributes,
  ReactNode,
  TableHTMLAttributes,
  TdHTMLAttributes,
  ThHTMLAttributes,
} from "react";
import { cx } from "../utils/cx";
import styles from "./Table.module.css";

/**
 * Table — the design-system list surface.
 *
 * A straightforward, accessible semantic table: `<table>/<thead>/<tbody>` with
 * `<th scope>` headers, an accessible name (visible `caption` OR `aria-label`),
 * zebra + hover rows, right-aligned tabular numeric columns (money later), a
 * trailing row-actions cell, an empty-state row, and a muted soft-deleted row
 * treatment. It is presentational — data, sorting, and actions are wired by the
 * feature (the implementer). A responsive stacked-card variant can layer on
 * later without a rewrite; it is deliberately NOT built now.
 *
 * Composition:
 *   <Table caption="Danh sách thành viên" captionHidden>
 *     <TableHead>
 *       <TableRow>
 *         <TableHeaderCell>Tên</TableHeaderCell>
 *         <TableHeaderCell>Trạng thái</TableHeaderCell>
 *         <TableHeaderCell numeric>Số dư</TableHeaderCell>
 *         <TableHeaderCell><span className="sr-only">Hành động</span></TableHeaderCell>
 *       </TableRow>
 *     </TableHead>
 *     <TableBody>
 *       <TableRow>
 *         <TableHeaderCell scope="row">An Nguyễn</TableHeaderCell>
 *         <TableCell><Badge>…</Badge></TableCell>
 *         <TableCell numeric><Money … /></TableCell>
 *         <TableCell actions>
 *           <Button variant="ghost" size="sm" aria-label="Đổi tên An Nguyễn">…</Button>
 *         </TableCell>
 *       </TableRow>
 *       <TableRow deleted>… muted row + a "(đã xóa)" Badge …</TableRow>
 *       {/* or, when there is nothing to show: *\/}
 *       <TableEmpty colSpan={4}><EmptyState … /></TableEmpty>
 *     </TableBody>
 *   </Table>
 */

export type TableProps = TableHTMLAttributes<HTMLTableElement> & {
  /**
   * Accessible name for the table. Pass a visible `<caption>` node here, OR omit
   * it and pass `aria-label` when the surface already has a heading. One of the
   * two MUST be present so the table is named for assistive tech.
   */
  caption?: ReactNode;
  /** Keep `caption` as the accessible name but hide it visually. */
  captionHidden?: boolean;
  /** Compact row height for dense lists. */
  dense?: boolean;
  children: ReactNode;
};

export function Table({
  caption,
  captionHidden = false,
  dense = false,
  className,
  children,
  ...rest
}: TableProps) {
  return (
    <div className={styles.scroll}>
      <table
        className={cx(styles.table, dense && styles.dense, className)}
        {...rest}
      >
        {caption != null ? (
          <caption
            className={cx(styles.caption, captionHidden && styles.srOnly)}
          >
            {caption}
          </caption>
        ) : null}
        {children}
      </table>
    </div>
  );
}

export function TableHead({
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLTableSectionElement>) {
  return (
    <thead className={cx(styles.head, className)} {...rest}>
      {children}
    </thead>
  );
}

export function TableBody({
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLTableSectionElement>) {
  return (
    <tbody className={cx(styles.body, className)} {...rest}>
      {children}
    </tbody>
  );
}

/**
 * `<tfoot>` — a summary section pinned below the body for total / sum rows
 * (e.g. the event debt-balance sum-to-zero row). Put a `<TableRow total>` inside
 * it. Semantically distinct from the body so assistive tech announces it as a
 * footer; visually it carries a heavier top rule and an emphasized weight.
 */
export function TableFoot({
  className,
  children,
  ...rest
}: HTMLAttributes<HTMLTableSectionElement>) {
  return (
    <tfoot className={cx(styles.foot, className)} {...rest}>
      {children}
    </tfoot>
  );
}

export type TableRowProps = HTMLAttributes<HTMLTableRowElement> & {
  /**
   * Muted styling for a soft-deleted / inactive row. Pair it with a visible
   * "(đã xóa)" Badge — the state is never carried by color alone. Also sets
   * `data-deleted` for a styling/test hook.
   */
  deleted?: boolean;
  /**
   * Emphasized summary / total row — a heavier top rule, a faint sunken tint and
   * semibold cells, opting out of zebra + hover. Typically the single row inside
   * a `<TableFoot>` (the balance sum-to-zero row); also usable in the body.
   */
  total?: boolean;
};

export function TableRow({
  deleted = false,
  total = false,
  className,
  children,
  ...rest
}: TableRowProps) {
  return (
    <tr
      className={cx(
        styles.row,
        deleted && styles.deleted,
        total && styles.total,
        className,
      )}
      data-deleted={deleted || undefined}
      data-total={total || undefined}
      {...rest}
    >
      {children}
    </tr>
  );
}

type CellAlign = "left" | "center" | "right";

function alignClass(align?: CellAlign): string | undefined {
  if (align === "center") return styles.alignCenter;
  if (align === "right") return styles.alignRight;
  return undefined;
}

export type TableHeaderCellProps = ThHTMLAttributes<HTMLTableCellElement> & {
  /** Right-align + tabular numerals (money / counts). */
  numeric?: boolean;
  align?: CellAlign;
  /**
   * `col` (default) for a column header in `<thead>`; `row` for a row header
   * (the primary label cell) inside `<tbody>` — improves screen-reader row
   * association. `scope` is REQUIRED semantics; the default keeps callers terse.
   */
  scope?: "col" | "row";
};

export function TableHeaderCell({
  numeric = false,
  align,
  scope = "col",
  className,
  children,
  ...rest
}: TableHeaderCellProps) {
  return (
    <th
      scope={scope}
      className={cx(
        styles.headerCell,
        numeric && styles.numeric,
        alignClass(align),
        className,
      )}
      {...rest}
    >
      {children}
    </th>
  );
}

export type TableCellProps = TdHTMLAttributes<HTMLTableCellElement> & {
  /** Right-align + tabular numerals (money / counts). */
  numeric?: boolean;
  align?: CellAlign;
  /**
   * Trailing row-actions cell: shrinks to content and lays its children out in a
   * right-aligned, evenly-gapped row. Put labeled action `Button`s inside
   * (each `aria-label` should name the row's subject, e.g. "Đổi tên An Nguyễn").
   */
  actions?: boolean;
};

export function TableCell({
  numeric = false,
  align,
  actions = false,
  className,
  children,
  ...rest
}: TableCellProps) {
  if (actions) {
    return (
      <td className={cx(styles.actionsCell, className)} {...rest}>
        <div className={styles.actions}>{children}</div>
      </td>
    );
  }
  return (
    <td
      className={cx(numeric && styles.numeric, alignClass(align), className)}
      {...rest}
    >
      {children}
    </td>
  );
}

export type TableEmptyProps = {
  /** Number of columns to span — must equal the table's column count. */
  colSpan: number;
  /** Empty-state content (typically an `<EmptyState />`). */
  children: ReactNode;
  className?: string;
};

/**
 * A single full-width row for the empty state, rendered inside `<TableBody>`.
 * Keeps the table's header visible while the body communicates "nothing here".
 */
export function TableEmpty({ colSpan, children, className }: TableEmptyProps) {
  return (
    <tr>
      <td colSpan={colSpan} className={cx(styles.empty, className)}>
        {children}
      </td>
    </tr>
  );
}
