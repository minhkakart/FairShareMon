import type { ReactNode } from "react";
import { useState } from "react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogTrigger,
  EmptyState,
  Money,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextField,
} from "../components/ui";
import styles from "./M5Showcase.module.css";

/* =============================================================================
 * M5 — Events (lifecycle + closed-event UI): design showcase
 *
 * The four net-new surfaces the web-implementer wires with real data:
 *   1. the §3.7 debt-balance table (advanced / owed / balance, sum-to-zero
 *      footer, color-independent signed balance, owner-rep + "(đã xóa)" markers),
 *   2. the one-way-close IRREVERSIBLE confirm (danger-tone Dialog + a deliberate
 *      acknowledgment affordance — distinct from an ordinary delete confirm),
 *   3. the open / closed event status Badge (icon + text, never color alone),
 *   4. the assign-expense picker Dialog (searchable single-select list of
 *      eligible loose, in-range expenses, with loading + empty states).
 *
 * Everything here is DESIGN spec: local state stands in for the API so the
 * layout, markup, tokens, and a11y are reviewable in light AND dark. The
 * implementer owns data / routing / i18n / hooks. Copy is Vietnamese-authoritative
 * (the domain terms: đợt, phiếu chi tiêu, đã ứng, phải gánh, cân bằng).
 * ========================================================================== */

// ── Local glyphs (decorative — labels/copy carry the meaning) ──────────────
const LockIcon = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true" width="1em" height="1em">
    <path d="M6 8V6a4 4 0 118 0v2h1v9H5V8h1zm2 0h4V6a2 2 0 10-4 0v2z" />
  </svg>
);
const OpenIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <circle cx="10" cy="10" r="6.4" />
    <path d="M10 6.4v3.6l2.4 1.6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const SearchIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1.05em" height="1.05em">
    <circle cx="9" cy="9" r="5.2" />
    <path d="M13 13l4 4" strokeLinecap="round" />
  </svg>
);

// ── Demo domain data ───────────────────────────────────────────────────────
type BalanceRow = {
  uuid: string;
  name: string;
  ownerRep?: boolean;
  deleted?: boolean;
  advanced: number;
  owed: number;
  balance: number; // = advanced − owed (API-computed; never client-summed)
};

// The API guarantees the row set sums to zero on `balance`. These verbatim
// figures mirror that invariant (they are display-only here).
const BALANCE_ROWS: BalanceRow[] = [
  { uuid: "m-minh", name: "Minh", ownerRep: true, advanced: 900_000, owed: 300_000, balance: 600_000 },
  { uuid: "m-an", name: "An Nguyễn", advanced: 300_000, owed: 350_000, balance: -50_000 },
  { uuid: "m-ngoc", name: "Trần Thị Bích Ngọc", advanced: 0, owed: 400_000, balance: -400_000 },
  { uuid: "m-bao", name: "Bảo", deleted: true, advanced: 100_000, owed: 250_000, balance: -150_000 },
];

// Footer totals are the API-provided column sums, rendered verbatim (never
// re-added on the client). advanced/owed tie out; balance nets to 0.
const BALANCE_TOTAL = { advanced: 1_300_000, owed: 1_300_000, balance: 0 };

type LooseExpense = {
  uuid: string;
  name: string;
  date: string; // pre-formatted for the demo (implementer uses formatDate)
  total: number;
};

const LOOSE_IN_RANGE: LooseExpense[] = [
  { uuid: "e-1", name: "Vé xe khách Đà Lạt", date: "12/07/2026", total: 480_000 },
  { uuid: "e-2", name: "Ăn tối quán nướng", date: "13/07/2026", total: 1_250_000 },
  { uuid: "e-3", name: "Vé vào vườn hoa thành phố", date: "14/07/2026", total: 160_000 },
  { uuid: "e-4", name: "Cà phê sáng cả nhóm", date: "15/07/2026", total: 235_000 },
];

export function M5Showcase() {
  return (
    <>
      <BalanceTableSection />
      <CloseConfirmSection />
      <StatusBadgeSection />
      <AssignPickerSection />
    </>
  );
}

/* ── 1. Debt-balance table ────────────────────────────────────────────────── */
function BalanceTableSection() {
  return (
    <Section
      title="Bảng cân đối công nợ (đã ứng · phải gánh · cân bằng)"
      note="Dựng trên họ Table sẵn có — không phải bảng mới. Mỗi hàng là một thành viên tham gia (kèm đại diện chủ ví ở 0đ và thành viên đã xóa). Tiền hiển thị verbatim qua Money, canh phải + tabular. Cột cân bằng dùng Money variant=balance: dấu +/− là tín hiệu KHÔNG phụ thuộc màu, kèm nhãn chữ (được nhận / phải trả). Hàng tổng nằm trong TableFoot: đã ứng và phải gánh khớp nhau, cân bằng luôn bằng 0 (API là nguồn đúng — client không tự cộng)."
    >
      <Card>
        <CardBody>
          <BalanceTable rows={BALANCE_ROWS} total={BALANCE_TOTAL} />
        </CardBody>
      </Card>

      <p className={styles.subhead}>Trạng thái rỗng (đợt chưa có phiếu nào)</p>
      <Card>
        <CardBody>
          <BalanceTable rows={[]} total={{ advanced: 0, owed: 0, balance: 0 }} />
        </CardBody>
      </Card>
    </Section>
  );
}

function BalanceTable({
  rows,
  total,
}: {
  rows: BalanceRow[];
  total: { advanced: number; owed: number; balance: number };
}) {
  return (
    <Table caption="Bảng cân đối công nợ của đợt" captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell>Thành viên</TableHeaderCell>
          <TableHeaderCell numeric>Đã ứng</TableHeaderCell>
          <TableHeaderCell numeric>Phải gánh</TableHeaderCell>
          <TableHeaderCell numeric>Cân bằng</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.length === 0 ? (
          <TableEmpty colSpan={4}>
            <EmptyState
              title="Chưa có phiếu nào trong đợt"
              description="Số dư sẽ xuất hiện khi đợt có phiếu chi tiêu. Gán phiếu để bắt đầu."
            />
          </TableEmpty>
        ) : (
          rows.map((r) => (
            <TableRow key={r.uuid} deleted={r.deleted}>
              <TableHeaderCell scope="row">
                <span className={styles.memberCell}>
                  <span className={styles.memberName}>{r.name}</span>
                  {r.ownerRep ? (
                    <span className={styles.repTag}>đại diện</span>
                  ) : null}
                  {r.deleted ? (
                    <span className={styles.deletedTag}>(đã xóa)</span>
                  ) : null}
                </span>
              </TableHeaderCell>
              <TableCell numeric>
                <Money amount={r.advanced} />
              </TableCell>
              <TableCell numeric>
                <Money amount={r.owed} />
              </TableCell>
              <TableCell numeric>
                <BalanceAmount amount={r.balance} />
              </TableCell>
            </TableRow>
          ))
        )}
      </TableBody>
      {rows.length > 0 ? (
        <TableFoot>
          <TableRow total>
            <TableHeaderCell scope="row">
              Tổng
              <span className={styles.sumHint}>Cân bằng luôn bằng 0</span>
            </TableHeaderCell>
            <TableCell numeric>
              <Money amount={total.advanced} />
            </TableCell>
            <TableCell numeric>
              <Money amount={total.owed} />
            </TableCell>
            <TableCell numeric>
              <BalanceAmount amount={total.balance} />
            </TableCell>
          </TableRow>
        </TableFoot>
      ) : null}
    </Table>
  );
}

/**
 * The signed balance figure: Money variant="balance" (sign glyph + color) PLUS a
 * short polarity word so the meaning never rests on color alone (CVD-safe). The
 * label is stacked under the figure and stays right-aligned with the column.
 */
function BalanceAmount({ amount }: { amount: number }) {
  const label =
    amount > 0 ? "được nhận lại" : amount < 0 ? "phải trả" : "đã cân bằng";
  return (
    <span className={styles.balanceCell}>
      <Money amount={amount} variant="balance" />
      <span className={styles.polarity}>{label}</span>
    </span>
  );
}

/* ── 2. One-way close: the irreversible confirm ───────────────────────────── */
function CloseConfirmSection() {
  return (
    <Section
      title="Xác nhận chốt đợt — hành động một chiều (không thể hoàn tác)"
      note="Tái sử dụng Dialog với tone=danger: viền đỉnh + biểu tượng cảnh báo tách nó khỏi hộp xác nhận thường (xóa dùng tone mặc định). Copy nói rõ 'không thể hoàn tác'; một Alert cảnh báo hệ quả (khóa mọi thay đổi phiếu/phần gánh, trừ trạng thái đã trả); và một affordance chủ ý — ô xác nhận phải tick mới bật được nút nguy hiểm. Nút chính variant=danger."
    >
      <CloseEventDialogDemo />
    </Section>
  );
}

function CloseEventDialogDemo() {
  const [open, setOpen] = useState(false);
  const [ack, setAck] = useState(false);

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        setOpen(o);
        if (!o) setAck(false);
      }}
    >
      <DialogTrigger asChild>
        <Button variant="danger" iconStart={<LockIcon />}>
          Chốt đợt
        </Button>
      </DialogTrigger>
      <DialogContent
        tone="danger"
        size="sm"
        title="Chốt đợt “Đà Lạt 07/2026”?"
        description="Chốt đợt là hành động MỘT CHIỀU — không thể mở lại."
        closeLabel="Hủy"
      >
        <Alert tone="warning" title="Sau khi chốt, đợt bị khóa">
          Mọi thay đổi phiếu chi tiêu và phần gánh của đợt sẽ bị khóa — chỉ còn
          có thể xem, xuất CSV và bật/tắt trạng thái “đã trả”. Bạn không thể thêm,
          gỡ hay sửa phiếu trong đợt nữa.
        </Alert>

        <label className={styles.ack}>
          <input
            type="checkbox"
            className={styles.ackBox}
            checked={ack}
            onChange={(e) => setAck(e.target.checked)}
          />
          <span>Tôi hiểu rằng không thể mở lại đợt sau khi chốt.</span>
        </label>

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              Hủy
            </Button>
          </DialogClose>
          <Button type="button" variant="danger" disabled={!ack}>
            Chốt đợt — không thể hoàn tác
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ── 3. Event status badge ────────────────────────────────────────────────── */
function StatusBadgeSection() {
  return (
    <Section
      title="Nhãn trạng thái đợt (đang mở · đã chốt)"
      note="Tái dùng Badge. Đang mở: tone success + biểu tượng đồng hồ; đã chốt: tone neutral + ổ khóa. Cả hai đều mang nghĩa qua CHỮ + biểu tượng, không dựa vào màu. Nhãn 'đã chốt' bắt cặp với việc implementer vô hiệu mọi nút ghi (trừ toggle đã trả)."
    >
      <div className={styles.statusRow}>
        <Badge tone="success" icon={<OpenIcon />}>
          Đang mở
        </Badge>
        <Badge tone="neutral" icon={<LockIcon />}>
          Đã chốt
        </Badge>
      </div>
      <p className={styles.note}>
        Trong ngữ cảnh — ví dụ trên header trang chi tiết đợt:
      </p>
      <div className={styles.statusInContext}>
        <span className={styles.eventTitle}>Đà Lạt 07/2026</span>
        <Badge tone="neutral" icon={<LockIcon />}>
          Đã chốt
        </Badge>
        <span className={styles.closedAt}>Chốt lúc 16/07/2026 21:40</span>
      </div>
    </Section>
  );
}

/* ── 4. Assign-expense picker dialog ──────────────────────────────────────── */
function AssignPickerSection() {
  return (
    <Section
      title="Hộp thoại gán phiếu vào đợt"
      note="Tái dùng Dialog. Danh sách phiếu ĐỦ ĐIỀU KIỆN (loose + trong khoảng ngày của đợt) để chọn một — dùng radio gốc trong fieldset nên bàn phím + trình đọc màn hình chạy đúng, mỗi dòng được đặt tên bằng tên phiếu + ngày + tổng tiền. Có ô tìm kiếm, trạng thái đang tải (Skeleton) và rỗng (EmptyState)."
    >
      <AssignExpenseDialogDemo />

      <p className={styles.subhead}>Trạng thái đang tải</p>
      <div className={styles.pickerPanel}>
        <div className={styles.pickerList} aria-hidden="true">
          {[0, 1, 2].map((i) => (
            <div key={i} className={styles.pickerSkeletonRow}>
              <Skeleton width="1.1rem" height="1.1rem" circle />
              <div className={styles.pickerSkeletonText}>
                <Skeleton width="60%" height="0.9rem" />
                <Skeleton width="35%" height="0.75rem" />
              </div>
              <Skeleton width="5rem" height="0.9rem" />
            </div>
          ))}
        </div>
      </div>

      <p className={styles.subhead}>Trạng thái rỗng (không có phiếu đủ điều kiện)</p>
      <div className={styles.pickerPanel}>
        <EmptyState
          title="Không có phiếu nào để gán"
          description="Chỉ những phiếu chưa thuộc đợt nào và có thời điểm nằm trong khoảng ngày của đợt mới hiện ở đây. Điều chỉnh khoảng ngày của đợt hoặc tạo phiếu mới."
        />
      </div>
    </Section>
  );
}

function AssignExpenseDialogDemo() {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [selected, setSelected] = useState<string | null>(null);

  const filtered = LOOSE_IN_RANGE.filter((e) =>
    e.name.toLowerCase().includes(query.trim().toLowerCase()),
  );

  return (
    <Dialog
      open={open}
      onOpenChange={(o) => {
        setOpen(o);
        if (!o) {
          setQuery("");
          setSelected(null);
        }
      }}
    >
      <DialogTrigger asChild>
        <Button variant="secondary">Gán phiếu</Button>
      </DialogTrigger>
      <DialogContent
        size="md"
        title="Gán phiếu vào đợt “Đà Lạt 07/2026”"
        description="Chọn một phiếu chưa thuộc đợt nào, có thời điểm nằm trong khoảng ngày của đợt."
        closeLabel="Hủy"
      >
        <TextField
          label="Tìm phiếu"
          hideLabelVisually
          placeholder="Tìm theo tên phiếu…"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          addonEnd={<SearchIcon />}
        />

        {filtered.length === 0 ? (
          <div className={styles.pickerPanel}>
            <EmptyState
              title="Không tìm thấy phiếu phù hợp"
              description="Thử từ khóa khác."
            />
          </div>
        ) : (
          <fieldset className={styles.pickerFieldset}>
            <legend className={styles.srOnly}>Phiếu đủ điều kiện để gán</legend>
            <div className={styles.pickerList}>
              {filtered.map((e) => (
                <label key={e.uuid} className={styles.pickerRow}>
                  <input
                    type="radio"
                    name="assign-expense"
                    className={styles.pickerRadio}
                    value={e.uuid}
                    checked={selected === e.uuid}
                    onChange={() => setSelected(e.uuid)}
                  />
                  <span className={styles.pickerText}>
                    <span className={styles.pickerName}>{e.name}</span>
                    <span className={styles.pickerDate}>{e.date}</span>
                  </span>
                  <Money amount={e.total} className={styles.pickerTotal} />
                </label>
              ))}
            </div>
          </fieldset>
        )}

        <DialogFooter>
          <DialogClose asChild>
            <Button type="button" variant="ghost">
              Hủy
            </Button>
          </DialogClose>
          <Button type="button" variant="primary" disabled={selected === null}>
            Gán phiếu
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* ── Section shell (local to the M5 showcase, mirrors the M4 one) ──────────── */
function Section({
  title,
  note,
  children,
}: {
  title: string;
  note?: string;
  children: ReactNode;
}) {
  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{title}</h2>
      {note ? <p className={styles.note}>{note}</p> : null}
      {children}
    </section>
  );
}
