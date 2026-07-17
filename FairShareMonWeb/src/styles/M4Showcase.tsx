import type { ReactNode } from "react";
import { useState } from "react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  CategoryMarker,
  DescriptionList,
  DescriptionRow,
  Money,
  MoneyInput,
  Select,
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeaderCell,
  TableRow,
  TagMultiSelect,
  TextField,
} from "../components/ui";
import type { SelectOption } from "../components/ui";
import styles from "./M4Showcase.module.css";

/* =============================================================================
 * M4 — Expenses & Shares: design showcase
 *
 * The net-new pickers (Select / TagMultiSelect / MoneyInput) plus the four
 * complex surfaces the web-implementer wires with real data: the share editor,
 * the filter bar, the expense detail layout (incl. the closed-event read-only
 * treatment), and the audit-history timeline. Everything here is DESIGN spec —
 * local state stands in for the API so the layout, markup, and token usage are
 * reviewable in light AND dark. The implementer owns data/routing/i18n/hooks.
 * ========================================================================== */

// ── Demo domain data (stands in for the M2/M3 hooks) ───────────────────────
type MemberMeta = { ownerRep?: boolean; deleted?: boolean };
type CategoryMeta = { color: string; icon: string | null; isDefault?: boolean };

const OWNER_REP_UUID = "m-minh";

const MEMBERS: SelectOption<MemberMeta>[] = [
  { value: OWNER_REP_UUID, label: "Minh (bạn)", meta: { ownerRep: true } },
  { value: "m-an", label: "An Nguyễn" },
  { value: "m-ngoc", label: "Trần Thị Bích Ngọc" },
  { value: "m-bao", label: "Bảo (cũ)", meta: { deleted: true } },
];

const CATEGORIES: SelectOption<CategoryMeta>[] = [
  {
    value: "c-food",
    label: "Ăn uống",
    meta: { color: "#F97316", icon: "🍜", isDefault: true },
  },
  { value: "c-move", label: "Đi lại", meta: { color: "#3B82F6", icon: "🚗" } },
  {
    value: "c-hotel",
    label: "Khách sạn",
    meta: { color: "#8B5CF6", icon: null },
  },
];

const TAGS = [
  { value: "t-dalat", label: "Du lịch Đà Lạt" },
  { value: "t-team", label: "Team building" },
  { value: "t-out", label: "Ăn ngoài" },
  { value: "t-shared", label: "Chi chung" },
];

const memberLabel = (uuid: string) =>
  MEMBERS.find((m) => m.value === uuid)?.label ?? uuid;

/** Renders a member option with the owner-rep marker + "(đã xóa)" treatment. */
function renderMember(option: SelectOption<MemberMeta>): ReactNode {
  return (
    <span className={styles.memberOption}>
      <span className={styles.memberName}>{option.label}</span>
      {option.meta?.ownerRep ? (
        <Badge tone="info" icon={<StarIcon />}>
          Đại diện
        </Badge>
      ) : null}
      {option.meta?.deleted ? <span className={styles.deletedTag}>(đã xóa)</span> : null}
    </span>
  );
}

/** Renders a category option as a CategoryMarker (color + emoji + name). */
function renderCategory(option: SelectOption<CategoryMeta>): ReactNode {
  const meta = option.meta;
  if (!meta) return option.label;
  return (
    <CategoryMarker
      color={meta.color}
      icon={meta.icon}
      name={option.label}
      showLabel
      isDefault={meta.isDefault}
      defaultLabel="mặc định"
    />
  );
}

export function M4Showcase() {
  return (
    <>
      <PickersSection />
      <ShareEditorSection />
      <FilterBarSection />
      <ExpenseDetailSection />
      <AuditTimelineSection />
    </>
  );
}

/* ── 1. The three net-new pickers ─────────────────────────────────────────── */
function PickersSection() {
  const [payer, setPayer] = useState<string | undefined>(OWNER_REP_UUID);
  const [category, setCategory] = useState<string | undefined>("c-food");
  const [amount, setAmount] = useState<number | null>(250000);
  const [tags, setTags] = useState<string[]>(["t-dalat", "t-out"]);

  return (
    <Section
      title="Pickers mới — Select · MoneyInput · TagMultiSelect"
      note="Ba primitive mới lấp khoảng trống của hệ thống. Select dựng trên Radix (bàn phím, typeahead, ARIA) với slot renderOption để hiện CategoryMarker hoặc dữ liệu thành viên; MoneyInput nhận VND nguyên, nhóm hàng nghìn kiểu vi-VN, phát ra số nguyên; TagMultiSelect hiện nhãn đã chọn dạng chip xóa được + danh sách checkbox."
    >
      <div className={styles.pickerGrid}>
        <Select
          label="Người trả"
          value={payer}
          onValueChange={setPayer}
          options={MEMBERS}
          renderOption={renderMember}
          hint="Không chọn thì mặc định là bạn (đại diện chủ sổ)."
        />
        <Select
          label="Danh mục"
          value={category}
          onValueChange={setCategory}
          options={CATEGORIES}
          renderOption={renderCategory}
          placeholder="Chọn danh mục"
        />
        <MoneyInput
          label="Số tiền phần gánh"
          value={amount}
          onChange={setAmount}
          hint="Chỉ nhập VND nguyên — không có phần lẻ."
        />
        <Select
          label="Trạng thái (ví dụ lỗi)"
          value={undefined}
          onValueChange={() => {}}
          options={CATEGORIES}
          renderOption={renderCategory}
          placeholder="Chọn danh mục"
          error="Vui lòng chọn danh mục."
          required
        />
      </div>
      <div className={styles.tagWrap}>
        <TagMultiSelect
          label="Nhãn"
          value={tags}
          onChange={setTags}
          options={TAGS}
          placeholder="Chưa gắn nhãn"
          toggleLabel="Chọn nhãn"
          removeLabel={(l) => `Bỏ nhãn ${l}`}
          emptyLabel="Chưa có nhãn nào"
          hint="Gắn nhiều nhãn để lọc và thống kê về sau."
        />
      </div>
    </Section>
  );
}

/* ── 2. Share editor ──────────────────────────────────────────────────────── */
type ShareRow = { id: string; memberUuid: string; amount: number | null; note: string };

let rowSeq = 0;
const nextRowId = () => `row-${rowSeq++}`;

function ShareEditorSection() {
  const [rows, setRows] = useState<ShareRow[]>([
    { id: nextRowId(), memberUuid: OWNER_REP_UUID, amount: 0, note: "" },
    { id: nextRowId(), memberUuid: "m-an", amount: 300000, note: "Vé xe" },
    { id: nextRowId(), memberUuid: "m-ngoc", amount: 300000, note: "" },
  ]);

  const chosen = new Set(rows.map((r) => r.memberUuid));
  const total = rows.reduce((sum, r) => sum + (r.amount ?? 0), 0);

  const update = (id: string, patch: Partial<ShareRow>) =>
    setRows((rs) => rs.map((r) => (r.id === id ? { ...r, ...patch } : r)));
  const remove = (id: string) => setRows((rs) => rs.filter((r) => r.id !== id));

  const availableFor = (row: ShareRow) =>
    MEMBERS.filter(
      (m) =>
        !m.meta?.deleted && (m.value === row.memberUuid || !chosen.has(m.value)),
    );

  const addRow = () => {
    const free = MEMBERS.find(
      (m) => !m.meta?.deleted && !chosen.has(m.value),
    );
    if (!free) return;
    setRows((rs) => [
      ...rs,
      { id: nextRowId(), memberUuid: free.value, amount: null, note: "" },
    ]);
  };

  const allChosen = MEMBERS.filter((m) => !m.meta?.deleted).every((m) =>
    chosen.has(m.value),
  );

  return (
    <Section
      title="Trình soạn phần gánh (share editor)"
      note="Mỗi dòng: thành viên + số tiền + ghi chú + nút xóa. Dòng đại diện chủ sổ 0đ luôn có mặt, ghim đầu, khóa thành viên và không xóa được (khớp auto-inject + bảo vệ 7002 của backend). Select mỗi dòng loại trừ thành viên đã chọn (khớp 7003). Dòng 'Tổng (tạm tính)' chỉ để tham khảo — tổng chính thức do API trả về sau khi lưu."
    >
      <Card>
        <CardBody>
          <div className={styles.shareEditor}>
            {rows.map((row) => {
              const locked = row.memberUuid === OWNER_REP_UUID;
              const name = memberLabel(row.memberUuid);
              return (
                <div
                  key={row.id}
                  className={cx(styles.shareRow, locked && styles.shareRowLocked)}
                >
                  <div className={styles.shareMember}>
                    {locked ? (
                      <div className={styles.lockedMember}>
                        <span className={styles.lockedMemberName}>{name}</span>
                        <Badge tone="info" icon={<LockIcon />}>
                          Đại diện · khóa
                        </Badge>
                      </div>
                    ) : (
                      <Select
                        label={`Thành viên — ${name}`}
                        hideLabelVisually
                        value={row.memberUuid}
                        onValueChange={(v) => update(row.id, { memberUuid: v })}
                        options={availableFor(row)}
                        renderOption={renderMember}
                      />
                    )}
                  </div>
                  <div className={styles.shareAmount}>
                    <MoneyInput
                      label={`Số tiền — ${name}`}
                      hideLabelVisually
                      value={row.amount}
                      onChange={(v) => update(row.id, { amount: v })}
                      disabled={locked}
                      placeholder="0"
                    />
                  </div>
                  <div className={styles.shareNote}>
                    <TextField
                      label={`Ghi chú — ${name}`}
                      hideLabelVisually
                      value={row.note}
                      placeholder="Ghi chú (tuỳ chọn)"
                      onChange={(e) => update(row.id, { note: e.target.value })}
                    />
                  </div>
                  <div className={styles.shareRemove}>
                    {locked ? (
                      <span className={styles.lockHint} aria-hidden="true">
                        <LockIcon />
                      </span>
                    ) : (
                      <Button
                        variant="ghost"
                        size="sm"
                        aria-label={`Xóa phần gánh của ${name}`}
                        onClick={() => remove(row.id)}
                      >
                        <TrashIcon />
                      </Button>
                    )}
                  </div>
                </div>
              );
            })}

            <div className={styles.shareFooter}>
              <Button
                variant="secondary"
                size="sm"
                onClick={addRow}
                disabled={allChosen}
              >
                <span className={styles.btnIcon}>
                  <PlusIcon />
                </span>
                Thêm phần gánh
              </Button>

              <div className={styles.runningTotal}>
                <span className={styles.runningTotalLabel}>
                  Tổng (tạm tính)
                  <span className={styles.runningTotalHint}>
                    {" "}
                    · tổng chính thức do hệ thống tính khi lưu
                  </span>
                </span>
                <Money amount={total} size="lg" />
              </div>
            </div>
          </div>
        </CardBody>
      </Card>
    </Section>
  );
}

/* ── 3. Filter bar ────────────────────────────────────────────────────────── */
const SETTLED_OPTIONS: SelectOption[] = [
  { value: "all", label: "Tất cả" },
  { value: "yes", label: "Đã trả" },
  { value: "no", label: "Chưa trả" },
];

function FilterBarSection() {
  const [category, setCategory] = useState<string | undefined>(undefined);
  const [settled, setSettled] = useState<string | undefined>("all");
  const [tags, setTags] = useState<string[]>([]);
  const [looseOnly, setLooseOnly] = useState(false);
  const [search, setSearch] = useState("");

  return (
    <Section
      title="Thanh lọc (filter bar)"
      note="Bộ lọc đầy đủ, kết hợp AND, xuống dòng linh hoạt theo bề rộng: khoảng ngày, danh mục, nhãn, trạng thái đã trả, chỉ phiếu lẻ, và tìm theo tên (lọc phía client trên danh sách đã tải). Bộ chọn đợt để dành cho M5."
    >
      <Card>
        <CardBody>
          <div className={styles.filterBar}>
            <TextField
              className={styles.filterDate}
              label="Từ ngày"
              type="date"
              defaultValue="2026-07-01"
            />
            <TextField
              className={styles.filterDate}
              label="Đến ngày"
              type="date"
              defaultValue="2026-07-31"
            />
            <Select
              className={styles.filterField}
              label="Danh mục"
              value={category ?? "all"}
              onValueChange={(v) => setCategory(v === "all" ? undefined : v)}
              options={[{ value: "all", label: "Tất cả danh mục" }, ...CATEGORIES]}
              renderOption={(o) =>
                o.value === "all"
                  ? o.label
                  : renderCategory(o as SelectOption<CategoryMeta>)
              }
            />
            <div className={styles.filterField}>
              <TagMultiSelect
                label="Nhãn"
                value={tags}
                onChange={setTags}
                options={TAGS}
                placeholder="Mọi nhãn"
                toggleLabel="Lọc theo nhãn"
                removeLabel={(l) => `Bỏ lọc ${l}`}
              />
            </div>
            <Select
              className={styles.filterField}
              label="Trạng thái"
              value={settled}
              onValueChange={setSettled}
              options={SETTLED_OPTIONS}
            />
            <TextField
              className={styles.filterField}
              label="Tìm theo tên"
              type="search"
              placeholder="Tên phiếu…"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
            />
            <div className={styles.filterToggle}>
              <Switch
                label="Chỉ phiếu lẻ"
                checked={looseOnly}
                onChange={setLooseOnly}
              />
            </div>
            <div className={styles.filterClear}>
              <Button variant="ghost" size="sm">
                Xóa lọc
              </Button>
            </div>
          </div>
        </CardBody>
      </Card>
    </Section>
  );
}

/* ── 4. Expense detail layout (+ closed-event read-only state) ─────────────── */
function ExpenseDetailSection() {
  const [settled, setSettled] = useState(false);

  const shares = [
    { uuid: "s1", member: "Minh (bạn)", ownerRep: true, amount: 0, note: "" },
    { uuid: "s2", member: "An Nguyễn", amount: 300000, note: "Vé xe" },
    { uuid: "s3", member: "Trần Thị Bích Ngọc", amount: 300000, note: "" },
    { uuid: "s4", member: "Bảo (cũ)", deleted: true, amount: 150000, note: "Đặt cọc" },
  ];
  const total = shares.reduce((s, r) => s + r.amount, 0);

  const SharesTable = ({ readOnly }: { readOnly?: boolean }) => (
    <Table caption="Phần gánh của phiếu" captionHidden>
      <TableHead>
        <TableRow>
          <TableHeaderCell scope="col">Thành viên</TableHeaderCell>
          <TableHeaderCell scope="col" numeric>
            Số tiền
          </TableHeaderCell>
          <TableHeaderCell scope="col">Ghi chú</TableHeaderCell>
          <TableHeaderCell scope="col">
            <span className={styles.srOnly}>Hành động</span>
          </TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {shares.map((row) => (
          <TableRow key={row.uuid} deleted={row.deleted}>
            <TableHeaderCell scope="row">
              <span className={styles.shareMemberCell}>
                {row.member}
                {row.ownerRep ? (
                  <Badge tone="info" icon={<StarIcon />}>
                    Đại diện
                  </Badge>
                ) : null}
                {row.deleted ? (
                  <span className={styles.deletedTag}>(đã xóa)</span>
                ) : null}
              </span>
            </TableHeaderCell>
            <TableCell numeric>
              <Money amount={row.amount} />
            </TableCell>
            <TableCell>{row.note || <span className={styles.muted}>—</span>}</TableCell>
            <TableCell actions>
              {row.ownerRep ? (
                <span className={styles.muted}>khóa</span>
              ) : (
                <>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={readOnly}
                    aria-label={`Sửa phần gánh của ${row.member}`}
                  >
                    Sửa
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    disabled={readOnly}
                    aria-label={`Xóa phần gánh của ${row.member}`}
                  >
                    Xóa
                  </Button>
                </>
              )}
            </TableCell>
          </TableRow>
        ))}
        <TableRow>
          <TableHeaderCell scope="row">Tổng</TableHeaderCell>
          <TableCell numeric>
            <Money amount={total} size="lg" />
          </TableCell>
          <TableCell />
          <TableCell />
        </TableRow>
      </TableBody>
    </Table>
  );

  const InfoList = () => (
    <DescriptionList>
      <DescriptionRow term="Mô tả">
        Chuyến đi Đà Lạt tháng 7 — chi phí đi lại chung.
      </DescriptionRow>
      <DescriptionRow term="Thời điểm chi">16/07/2026 19:30</DescriptionRow>
      <DescriptionRow term="Người trả">Minh (bạn)</DescriptionRow>
      <DescriptionRow term="Danh mục">
        <CategoryMarker color="#3B82F6" icon="🚗" name="Đi lại" showLabel />
      </DescriptionRow>
      <DescriptionRow term="Nhãn">
        <span className={styles.chipRow}>
          <Badge tone="neutral">Du lịch Đà Lạt</Badge>
          <Badge tone="neutral">Chi chung</Badge>
        </span>
      </DescriptionRow>
      <DescriptionRow term="Đợt">
        <span className={styles.muted}>Phiếu lẻ (không thuộc đợt nào)</span>
      </DescriptionRow>
      <DescriptionRow term="Tổng">
        <Money amount={total} size="lg" />
      </DescriptionRow>
    </DescriptionList>
  );

  return (
    <Section
      title="Bố cục trang chi tiết phiếu (expense detail)"
      note="Đầu trang có cụm hành động (Sửa · Xóa · Xuất CSV · công tắc Đã trả), tiếp theo là thông tin (DescriptionList), bảng phần gánh với tổng dẫn xuất, rồi dòng thời gian lịch sử. Biến thể 'đợt đã chốt' vô hiệu mọi nút ghi — riêng công tắc Đã trả vẫn bật (R4)."
    >
      {/* Normal (open) state */}
      <div className={styles.detailHeader}>
        <div className={styles.detailTitleBlock}>
          <h3 className={styles.detailTitle}>Thuê xe đi Đà Lạt</h3>
          <div className={styles.detailBadges}>
            <Badge
              tone={settled ? "settled" : "warning"}
              icon={settled ? <CheckIcon /> : <ClockIcon />}
            >
              {settled ? "Đã trả" : "Chưa trả"}
            </Badge>
            <Badge tone="neutral">Phiếu lẻ</Badge>
          </div>
        </div>
        <div className={styles.detailActions}>
          <Switch
            label="Đã trả"
            checked={settled}
            onChange={setSettled}
          />
          <Button variant="secondary" size="sm">
            <span className={styles.btnIcon}>
              <PencilIcon />
            </span>
            Sửa
          </Button>
          <Button variant="secondary" size="sm">
            <span className={styles.btnIcon}>
              <DownloadIcon />
            </span>
            Xuất CSV
          </Button>
          <Button variant="danger" size="sm">
            <span className={styles.btnIcon}>
              <TrashIcon />
            </span>
            Xóa
          </Button>
        </div>
      </div>

      <div className={styles.detailGrid}>
        <Card>
          <CardHeader title="Thông tin" />
          <CardBody>
            <InfoList />
          </CardBody>
        </Card>
        <Card>
          <CardHeader
            title="Phần gánh"
            action={
              <Button variant="secondary" size="sm">
                <span className={styles.btnIcon}>
                  <PlusIcon />
                </span>
                Thêm phần gánh
              </Button>
            }
          />
          <CardBody>
            <SharesTable />
          </CardBody>
        </Card>
      </div>

      {/* Closed-event read-only variant */}
      <p className={styles.subhead}>Biến thể: đợt đã chốt (chỉ đọc)</p>
      <Alert
        tone="warning"
        title="Đợt đã chốt"
        action={
          <Button variant="secondary" size="sm">
            Xem đợt
          </Button>
        }
      >
        Phiếu này thuộc một đợt đã chốt. Mọi thao tác chỉnh sửa bị khóa — chỉ còn
        đổi trạng thái đã trả.
      </Alert>
      <div className={styles.detailHeader}>
        <div className={styles.detailTitleBlock}>
          <h3 className={styles.detailTitle}>Tiệc tổng kết đợt</h3>
          <div className={styles.detailBadges}>
            <Badge tone="neutral" icon={<LockIcon />}>
              Đã chốt
            </Badge>
            <Badge tone="warning" icon={<ClockIcon />}>
              Chưa trả
            </Badge>
          </div>
        </div>
        <div className={styles.detailActions}>
          {/* The settled toggle stays enabled even when the event is closed. */}
          <Switch label="Đã trả" checked={false} onChange={() => {}} />
          <Button variant="secondary" size="sm" disabled>
            <span className={styles.btnIcon}>
              <PencilIcon />
            </span>
            Sửa
          </Button>
          <Button variant="secondary" size="sm">
            <span className={styles.btnIcon}>
              <DownloadIcon />
            </span>
            Xuất CSV
          </Button>
          <Button variant="danger" size="sm" disabled>
            <span className={styles.btnIcon}>
              <TrashIcon />
            </span>
            Xóa
          </Button>
        </div>
      </div>
      <Card>
        <CardHeader title="Phần gánh (chỉ đọc)" />
        <CardBody>
          <SharesTable readOnly />
        </CardBody>
      </Card>
    </Section>
  );
}

/* ── 5. Audit-history timeline ────────────────────────────────────────────── */
type DiffField = { label: string; before?: ReactNode; after?: ReactNode };
type AuditEntry = {
  id: string;
  action: "create" | "update" | "delete";
  entity: "expense" | "share";
  at: string;
  fields: DiffField[];
};

const AUDIT: AuditEntry[] = [
  {
    id: "a1",
    action: "create",
    entity: "expense",
    at: "16/07/2026 19:30",
    fields: [
      { label: "Tên", after: "Thuê xe đi Đà Lạt" },
      { label: "Danh mục", after: <CategoryMarker color="#3B82F6" icon="🚗" name="Đi lại" showLabel /> },
      { label: "Người trả", after: "Minh (bạn)" },
      { label: "Nhãn", after: <span className={styles.chipRow}><Badge tone="neutral">Du lịch Đà Lạt</Badge></span> },
    ],
  },
  {
    id: "a2",
    action: "create",
    entity: "share",
    at: "16/07/2026 19:30",
    fields: [
      { label: "Thành viên", after: "An Nguyễn" },
      { label: "Số tiền", after: <Money amount={250000} size="sm" /> },
    ],
  },
  {
    id: "a3",
    action: "update",
    entity: "share",
    at: "16/07/2026 20:05",
    fields: [
      {
        label: "Số tiền",
        before: <Money amount={250000} size="sm" tone="muted" />,
        after: <Money amount={300000} size="sm" />,
      },
      { label: "Ghi chú", before: <span className={styles.muted}>—</span>, after: "Vé xe" },
    ],
  },
  {
    id: "a4",
    action: "update",
    entity: "expense",
    at: "16/07/2026 20:10",
    fields: [
      {
        label: "Nhãn",
        before: <span className={styles.chipRow}><Badge tone="neutral">Du lịch Đà Lạt</Badge></span>,
        after: <span className={styles.chipRow}><Badge tone="neutral">Du lịch Đà Lạt</Badge><Badge tone="neutral">Chi chung</Badge></span>,
      },
      // An unknown/unmapped field falls back to a raw key/value line.
      { label: "internalRef", before: "r-0091", after: "r-0184" },
    ],
  },
  {
    id: "a5",
    action: "delete",
    entity: "share",
    at: "17/07/2026 08:12",
    fields: [
      { label: "Thành viên", before: "Bảo (cũ)" },
      { label: "Số tiền", before: <Money amount={150000} size="sm" tone="muted" /> },
    ],
  },
];

const ACTION_META: Record<
  AuditEntry["action"],
  { label: string; tone: "success" | "info" | "danger" }
> = {
  create: { label: "Tạo", tone: "success" },
  update: { label: "Cập nhật", tone: "info" },
  delete: { label: "Xóa", tone: "danger" },
};

function AuditTimelineSection() {
  return (
    <Section
      title="Dòng thời gian lịch sử (audit timeline)"
      note="Nhật ký thay đổi bất biến của phiếu + phần gánh, theo thứ tự thời gian, dạng danh sách có thứ tự (ol). Tạo = ảnh chụp mới; Cập nhật = chỉ các trường đổi (trước → sau); Xóa = ảnh chụp bị gỡ. Tiền qua Money, thời gian đã định dạng, nhãn dạng chip. Trường lạ (vd internalRef) rơi về dòng key/value thô nên không bao giờ vỡ."
    >
      <Card>
        <CardBody>
          <ol className={styles.timeline}>
            {AUDIT.map((entry) => {
              const meta = ACTION_META[entry.action];
              return (
                <li key={entry.id} className={styles.timelineItem}>
                  <span
                    className={cx(styles.timelineDot, styles[`dot_${entry.action}`])}
                    aria-hidden="true"
                  />
                  <div className={styles.timelineBody}>
                    <div className={styles.timelineHead}>
                      <Badge tone={meta.tone}>{meta.label}</Badge>
                      <span className={styles.timelineEntity}>
                        {entry.entity === "expense" ? "Phiếu chi tiêu" : "Phần gánh"}
                      </span>
                      <time className={styles.timelineTime}>{entry.at}</time>
                    </div>
                    <dl className={styles.diff}>
                      {entry.fields.map((f, i) => (
                        <div key={i} className={styles.diffRow}>
                          <dt className={styles.diffLabel}>{f.label}</dt>
                          <dd className={styles.diffValue}>
                            {entry.action === "update" ? (
                              <span className={styles.diffChange}>
                                <span className={styles.diffBefore}>
                                  {f.before ?? <span className={styles.muted}>—</span>}
                                </span>
                                <span className={styles.diffArrow} aria-label="đổi thành">
                                  →
                                </span>
                                <span className={styles.diffAfter}>{f.after}</span>
                              </span>
                            ) : entry.action === "delete" ? (
                              <span className={styles.diffRemoved}>{f.before}</span>
                            ) : (
                              <span className={styles.diffAfter}>{f.after}</span>
                            )}
                          </dd>
                        </div>
                      ))}
                    </dl>
                  </div>
                </li>
              );
            })}
          </ol>
        </CardBody>
      </Card>
    </Section>
  );
}

/* ── Demo helpers ─────────────────────────────────────────────────────────── */
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

/**
 * Demo-only switch (settled / loose-only). The real SettledToggle is a feature
 * component (M4 B3); this shows the color-independent visual: role="switch" with
 * a text label + on/off track, never color alone.
 */
function Switch({
  label,
  checked,
  onChange,
}: {
  label: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      className={cx(styles.switch, checked && styles.switchOn)}
      onClick={() => onChange(!checked)}
    >
      <span className={styles.switchTrack} aria-hidden="true">
        <span className={styles.switchThumb} />
      </span>
      <span className={styles.switchLabel}>{label}</span>
    </button>
  );
}

function cx(...parts: Array<string | false | undefined>): string {
  return parts.filter(Boolean).join(" ");
}

/* Inline glyphs (feature icons live in feature code; these are showcase-local). */
const StarIcon = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);
const LockIcon = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M6 8V6a4 4 0 118 0v2h1v9H5V8h1zm2 0h4V6a2 2 0 10-4 0v2z" />
  </svg>
);
const CheckIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true">
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const ClockIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
    <circle cx="10" cy="10" r="7.2" />
    <path d="M10 6v4.2l2.8 1.8" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const PlusIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.9" aria-hidden="true">
    <path d="M10 4v12M4 10h12" strokeLinecap="round" />
  </svg>
);
const TrashIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
    <path d="M4 6h12M8 6V4h4v2M6 6l.7 10h6.6L14 6" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const PencilIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
    <path d="M13.5 3.5l3 3L7 16H4v-3l9.5-9.5z" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const DownloadIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true">
    <path d="M10 3v9m0 0l-3.5-3.5M10 12l3.5-3.5M4 15h12" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
