import type { CSSProperties, ReactNode } from "react";
import { useState } from "react";
import {
  Button,
  Card,
  CardBody,
  CategoryMarker,
  EmptyState,
  ErrorState,
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
import styles from "./M6Showcase.module.css";

/* =============================================================================
 * M6 — Stats & Home dashboard: design showcase
 *
 * The first exercise of the reserved, CVD-validated --fs-viz-* palette. Four
 * net-new surfaces the web-implementer wires with real data (feature-local,
 * OQ5a — NOT extracted to components/ui/charts until M8):
 *
 *   1. StatTile + OverviewKpiRow — label + big Money/count value + optional
 *      sub-label; the two /stats/overview scalars and the this-month home tiles.
 *   2. CategoryBarChart — a hand-rolled horizontal bar breakdown (ranked, longest
 *      first, API order verbatim). Bars are the ONLY thing wearing --fs-viz-cat-*
 *      (slots assigned 1..8 in order, a 9th+ folds to a muted neutral). Each bar
 *      ships a DIRECT label (CategoryMarker + name + Money value + % share) so the
 *      light-mode relief rule is satisfied and identity never rests on bar color.
 *      Paired with an always-present accessible table (spec of the pairing).
 *   3. StatsRangeControl — preset chips (This month · Last 30 days · This year ·
 *      All time) + a Custom two-date mode, with an inline invalid-range message.
 *   4. Home composition — this-month KPI row + a compact breakdown + a recent-
 *      expenses card + quick actions + the existing role-filtered quick links.
 *
 * Everything here is DESIGN spec: local state stands in for the API so layout,
 * markup, tokens, and a11y are reviewable in light AND dark. The implementer owns
 * data / hooks / routing / i18n. Copy is Vietnamese-authoritative.
 *
 * dataviz compliance: the categorical palette was re-validated this cycle —
 * light slots 3/4/5 (#e87ba4 2.69 · #eda100 2.17 · #1baf7a 2.82) sit < 3:1 on
 * white → the RELIEF RULE applies, satisfied by the direct value labels + the
 * paired table. Marks: bar ≤ 12px (inside the 24px cap), 4px rounded data-end
 * square at the baseline, ≥ 2px surface gap between rows, recessive track, labels
 * in TEXT tokens (never the bar color), prefers-reduced-motion honored.
 * ========================================================================== */

// ── Local glyphs (decorative — copy carries the meaning) ───────────────────
const PlusIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M10 4v12M4 10h12" strokeLinecap="round" />
  </svg>
);
const EventIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <rect x="3" y="4.5" width="14" height="12" rx="2" />
    <path d="M3 8h14M7 3v3M13 3v3" strokeLinecap="round" />
  </svg>
);
const ReceiptIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" width="28" height="28">
    <path d="M6 2h12v20l-3-2-3 2-3-2-3 2V2zm3 5h6a1 1 0 010 2H9a1 1 0 010-2zm0 4h6a1 1 0 010 2H9a1 1 0 010-2z" />
  </svg>
);

// ── Demo domain data ───────────────────────────────────────────────────────
// A CategoryStatRow mirror. `color` is the category's OWN identity color (shown
// by CategoryMarker, consistent with the rest of the app); the BAR fill uses the
// systematic --fs-viz-cat-* slot by rank. Money + counts are API-computed
// (rendered verbatim); `total` drives display-only ratios only.
type CatRow = {
  uuid: string;
  name: string;
  color: string;
  icon?: string | null;
  isDeleted?: boolean;
  total: number;
  count: number;
};

// Sorted total DESC (the API order — rendered verbatim, no client re-sort).
const CAT_ROWS: CatRow[] = [
  { uuid: "c-food", name: "Ăn uống", color: "#F97316", icon: "🍜", total: 4_850_000, count: 18 },
  { uuid: "c-move", name: "Đi lại", color: "#3B82F6", icon: "🚗", total: 2_300_000, count: 12 },
  { uuid: "c-stay", name: "Khách sạn", color: "#8B5CF6", icon: "🏨", total: 1_800_000, count: 4 },
  { uuid: "c-fun", name: "Giải trí", color: "#EC4899", icon: "🎉", total: 1_200_000, count: 7 },
  { uuid: "c-shop", name: "Mua sắm", color: "#10B981", icon: "🛍️", total: 900_000, count: 5 },
  { uuid: "c-coffee", name: "Cà phê", color: "#A16207", icon: "☕", total: 450_000, count: 9 },
  // Soft-deleted category with history (§4.7) — still shown, "(đã xóa)".
  { uuid: "c-old", name: "Đồ lưu niệm", color: "#64748B", icon: "🎁", isDeleted: true, total: 150_000, count: 2 },
];
// The authoritative overview total for the SAME range (API value, verbatim). The
// % share denominator + the table footer echo this — never a client sum.
const OVERVIEW_TOTAL = 11_650_000;
const OVERVIEW_COUNT = 57;

// A "many categories" set to demonstrate 9th+ folding to a muted neutral slot.
const MANY_ROWS: CatRow[] = [
  ...CAT_ROWS.filter((r) => !r.isDeleted),
  { uuid: "m-health", name: "Sức khỏe", color: "#0EA5E9", icon: "💊", total: 380_000, count: 3 },
  { uuid: "m-gift", name: "Quà tặng", color: "#D946EF", icon: "🎀", total: 220_000, count: 2 },
  { uuid: "m-misc", name: "Khác", color: "#64748B", icon: "📦", total: 90_000, count: 4 },
];
const MANY_TOTAL = MANY_ROWS.reduce((s, r) => s + r.total, 0); // demo denom only

type RecentExpense = {
  uuid: string;
  name: string;
  cat: CatRow;
  date: string; // pre-formatted for the demo (implementer uses formatDate)
  total: number;
};
const RECENT: RecentExpense[] = [
  { uuid: "e-1", name: "Ăn tối quán nướng", cat: CAT_ROWS[0], date: "16/07/2026", total: 1_250_000 },
  { uuid: "e-2", name: "Vé xe khách Đà Lạt", cat: CAT_ROWS[1], date: "15/07/2026", total: 480_000 },
  { uuid: "e-3", name: "Cà phê sáng cả nhóm", cat: CAT_ROWS[5], date: "15/07/2026", total: 235_000 },
  { uuid: "e-4", name: "Vé vào vườn hoa thành phố", cat: CAT_ROWS[3], date: "14/07/2026", total: 160_000 },
  { uuid: "e-5", name: "Đặt phòng homestay 2 đêm", cat: CAT_ROWS[2], date: "13/07/2026", total: 1_800_000 },
];

export function M6Showcase() {
  return (
    <>
      <KpiSection />
      <BarChartSection />
      <RangeControlSection />
      <HomeCompositionSection />
    </>
  );
}

/* ── 1. KPI stat tiles ────────────────────────────────────────────────────── */
function KpiSection() {
  return (
    <Section
      title="Ô chỉ số KPI (StatTile · OverviewKpiRow)"
      note="Mỗi ô: nhãn nhỏ + giá trị lớn (Money cho tiền, số đã định dạng cho số đếm) + chú thích tùy chọn. Hàng KPI co giãn (auto-fit) — cạnh nhau khi rộng, xếp chồng khi hẹp. Chữ số tabular để giá trị không nhảy chiều rộng khi đổi khoảng. KHÔNG có ô 'trung bình mỗi phiếu' — đó là phép tính trên tiền (R3). Dùng chung cho trang Thống kê và trang chủ (nhãn 'Tháng này')."
    >
      <p className={styles.subhead}>Có dữ liệu</p>
      <OverviewKpiRow total={OVERVIEW_TOTAL} count={OVERVIEW_COUNT} />

      <p className={styles.subhead}>Khoảng rỗng — giá trị 0 (hợp lệ, không phải trạng thái rỗng)</p>
      <OverviewKpiRow total={0} count={0} />

      <p className={styles.subhead}>Đang tải</p>
      <div className={styles.kpiRow}>
        <StatTile label="Tổng chi tiêu" loading />
        <StatTile label="Số phiếu chi tiêu" loading />
      </div>

      <p className={styles.subhead}>Lỗi (compact)</p>
      <Card>
        <ErrorState title="Không tải được số liệu tổng quan" />
      </Card>
    </Section>
  );
}

/** Presentational KPI row from an overview response. Total → Money; count →
 *  localized number. Loading → skeleton tiles; zero → 0 tiles (not empty). */
function OverviewKpiRow({ total, count }: { total: number; count: number }) {
  return (
    <div className={styles.kpiRow}>
      <StatTile label="Tổng chi tiêu" value={<Money amount={total} size="xl" />} />
      <StatTile
        label="Số phiếu chi tiêu"
        value={<span className={styles.statValue}>{count.toLocaleString("vi-VN")}</span>}
        hint="Gồm cả phiếu lẻ và phiếu trong đợt"
      />
    </div>
  );
}

/** A single stat tile: label + big value + optional hint. Built on Card. */
function StatTile({
  label,
  value,
  hint,
  loading,
}: {
  label: ReactNode;
  value?: ReactNode;
  hint?: ReactNode;
  loading?: boolean;
}) {
  return (
    <Card>
      <div className={styles.statTile}>
        <span className={styles.statLabel}>{label}</span>
        {loading ? (
          <span className={styles.statValueSkeleton}>
            <Skeleton width="7rem" height="1.75rem" />
          </span>
        ) : (
          value
        )}
        {hint && !loading ? <span className={styles.statHint}>{hint}</span> : null}
      </div>
    </Card>
  );
}

/* ── 2. Category horizontal bar chart ─────────────────────────────────────── */
function BarChartSection() {
  return (
    <Section
      title="Biểu đồ cột ngang theo danh mục (CategoryBarChart)"
      note="Cột ngang xếp hạng — dài nhất trước, theo đúng thứ tự API (total DESC, không sắp lại ở client). Độ dài cột = total / total_lớn_nhất (chuẩn hóa theo cột dài nhất). Mỗi cột kèm NHÃN TRỰC TIẾP: CategoryMarker (ô màu danh mục + emoji + tên) + Money + % tỷ trọng — nên tuân thủ luật relief (slot màu sáng 3/4/5 < 3:1) và nghĩa không phụ thuộc màu cột. CHỈ phần tô của cột dùng --fs-viz-cat-* (gán slot 1..8 theo thứ hạng; hạng 9+ dồn về màu trung tính). Vùng biểu đồ là role=img có aria-label tóm tắt; dữ liệu cho trình đọc màn hình nằm ở BẢNG đi kèm bên dưới."
    >
      <Card>
        <CardBody>
          <CategoryBarChart rows={CAT_ROWS} overviewTotal={OVERVIEW_TOTAL} />
        </CardBody>
      </Card>

      <p className={styles.subhead}>Bảng số đi kèm (luôn hiển thị — nguồn dữ liệu cho trợ năng)</p>
      <p className={styles.note}>
        Biểu đồ (role=img, trang trí) BẮT CẶP với bảng này: bảng mang caption + 4
        cột, mỗi hàng có Money verbatim và % tỷ trọng (total / tổng chi tiêu của
        cùng khoảng). Hàng tổng lặp lại đúng overview.totalSpending — client không
        tự cộng. Danh mục đã xóa vẫn hiện với "(đã xóa)".
      </p>
      <Card>
        <CardBody>
          <CategoryStatsTable rows={CAT_ROWS} overviewTotal={OVERVIEW_TOTAL} />
        </CardBody>
      </Card>

      <p className={styles.subhead}>Nhiều danh mục — hạng 9+ dồn về màu trung tính</p>
      <p className={styles.note}>
        Màu cột không còn phân biệt được sau slot 8, nhưng tên + giá trị vẫn mang
        nghĩa (không phụ thuộc màu), nên biểu đồ vẫn đọc được với số lượng lớn.
      </p>
      <Card>
        <CardBody>
          <CategoryBarChart rows={MANY_ROWS} overviewTotal={MANY_TOTAL} />
        </CardBody>
      </Card>

      <div className={styles.homeGrid}>
        <div>
          <p className={styles.subhead}>Một danh mục</p>
          <Card>
            <CardBody>
              <CategoryBarChart rows={[CAT_ROWS[0]]} overviewTotal={CAT_ROWS[0].total} />
            </CardBody>
          </Card>
        </div>
        <div>
          <p className={styles.subhead}>Rỗng</p>
          <Card>
            <CardBody>
              <EmptyState
                icon={<ReceiptIcon />}
                title="Chưa có chi tiêu trong khoảng này"
                description="Chọn khoảng thời gian khác hoặc thêm phiếu chi tiêu để thấy phân tích theo danh mục."
              />
            </CardBody>
          </Card>
        </div>
      </div>
    </Section>
  );
}

/**
 * Hand-rolled horizontal bar chart. Bars are decorative (the region is role=img);
 * the paired table carries the data for AT. `maxTotal` (the longest bar) is the
 * bar-length denominator; `overviewTotal` is the % share denominator.
 */
function CategoryBarChart({
  rows,
  overviewTotal,
}: {
  rows: CatRow[];
  overviewTotal: number;
}) {
  if (rows.length === 0) {
    return (
      <EmptyState
        icon={<ReceiptIcon />}
        title="Chưa có chi tiêu trong khoảng này"
        description="Chọn khoảng thời gian khác hoặc thêm phiếu chi tiêu."
      />
    );
  }
  const maxTotal = Math.max(...rows.map((r) => r.total)); // longest-bar normalization
  const top = rows[0];
  const ariaLabel = `Chi tiêu theo danh mục: ${rows.length} danh mục, dẫn đầu là ${top.name}. Xem bảng số bên dưới để biết chi tiết.`;

  return (
    <div className={styles.chart} role="img" aria-label={ariaLabel}>
      {rows.map((row, i) => {
        // Bar fill: --fs-viz-cat-1..8 by rank; 9th+ → muted neutral (color no
        // longer distinguishes, name + value carry identity).
        const barColor =
          i < 8 ? `var(--fs-viz-cat-${i + 1})` : "var(--fs-viz-ink-muted)";
        const widthPct = maxTotal > 0 ? (row.total / maxTotal) * 100 : 0;
        const sharePct =
          overviewTotal > 0 ? Math.round((row.total / overviewTotal) * 100) : 0;
        return (
          <div key={row.uuid} className={styles.barRow} aria-hidden="true">
            <div className={styles.barHeader}>
              <span className={styles.barLabel}>
                <CategoryMarker
                  color={row.color}
                  icon={row.icon}
                  name={row.name}
                  showLabel
                  size="sm"
                />
                {row.isDeleted ? (
                  <span className={styles.barDeletedTag}>(đã xóa)</span>
                ) : null}
              </span>
              <span className={styles.barValue}>
                <Money amount={row.total} size="sm" />
                <span className={styles.barShare}>{sharePct}%</span>
              </span>
            </div>
            <div className={styles.barTrack}>
              <div
                className={styles.barFill}
                style={
                  { width: `${widthPct}%`, "--bar-color": barColor } as CSSProperties
                }
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

/**
 * The always-present accessible table alternative. Reuses the Table primitive.
 * Columns: Danh mục (row header) · Tổng (Money) · Số phiếu · Tỷ trọng (%). The
 * footer echoes the authoritative overview total — never a client sum.
 */
function CategoryStatsTable({
  rows,
  overviewTotal,
}: {
  rows: CatRow[];
  overviewTotal: number;
}) {
  return (
    <Table caption="Chi tiêu theo danh mục">
      <TableHead>
        <TableRow>
          <TableHeaderCell scope="col">Danh mục</TableHeaderCell>
          <TableHeaderCell scope="col" numeric>Tổng</TableHeaderCell>
          <TableHeaderCell scope="col" numeric>Số phiếu</TableHeaderCell>
          <TableHeaderCell scope="col" numeric>Tỷ trọng</TableHeaderCell>
        </TableRow>
      </TableHead>
      <TableBody>
        {rows.length === 0 ? (
          <TableEmpty colSpan={4}>
            <EmptyState
              title="Chưa có chi tiêu trong khoảng này"
              description="Chọn khoảng thời gian khác hoặc thêm phiếu chi tiêu."
            />
          </TableEmpty>
        ) : (
          rows.map((row) => {
            const sharePct =
              overviewTotal > 0
                ? Math.round((row.total / overviewTotal) * 100)
                : 0;
            return (
              <TableRow key={row.uuid} deleted={row.isDeleted}>
                <TableHeaderCell scope="row">
                  <span className={styles.barLabel}>
                    <CategoryMarker
                      color={row.color}
                      icon={row.icon}
                      name={row.name}
                      showLabel
                      size="sm"
                    />
                    {row.isDeleted ? (
                      <span className={styles.barDeletedTag}>(đã xóa)</span>
                    ) : null}
                  </span>
                </TableHeaderCell>
                <TableCell numeric>
                  <Money amount={row.total} />
                </TableCell>
                <TableCell numeric>
                  <span className={styles.shareCell}>
                    {row.count.toLocaleString("vi-VN")}
                  </span>
                </TableCell>
                <TableCell numeric>
                  <span className={styles.shareCell}>{sharePct}%</span>
                </TableCell>
              </TableRow>
            );
          })
        )}
      </TableBody>
      {rows.length > 0 ? (
        <TableFoot>
          <TableRow total>
            <TableHeaderCell scope="row">Tổng chi tiêu</TableHeaderCell>
            <TableCell numeric>
              <Money amount={overviewTotal} />
            </TableCell>
            <TableCell numeric>
              <span className={styles.shareCell}>{OVERVIEW_COUNT}</span>
            </TableCell>
            <TableCell numeric>
              <span className={styles.shareCell}>100%</span>
            </TableCell>
          </TableRow>
        </TableFoot>
      ) : null}
    </Table>
  );
}

/* ── 3. Date-range control ────────────────────────────────────────────────── */
type RangePreset = "thisMonth" | "last30Days" | "thisYear" | "allTime" | "custom";
const PRESETS: { value: RangePreset; label: string }[] = [
  { value: "thisMonth", label: "Tháng này" },
  { value: "last30Days", label: "30 ngày qua" },
  { value: "thisYear", label: "Năm nay" },
  { value: "allTime", label: "Tất cả" },
  { value: "custom", label: "Tùy chỉnh" },
];

function RangeControlSection() {
  return (
    <Section
      title="Bộ chọn khoảng thời gian (StatsRangeControl)"
      note="Chip preset (Tháng này · 30 ngày qua · Năm nay · Tất cả) + chế độ Tùy chỉnh mở hai ô ngày. Mặc định 'Tháng này' (trang chủ dùng cùng mặc định). role=group + aria-label; chip đang chọn mang aria-pressed (trạng thái không chỉ dựa vào màu). Khi from > to hiện thông báo lỗi inline và chặn trước khi gọi API (mã 1001)."
    >
      <StatsRangeControlDemo />
    </Section>
  );
}

function StatsRangeControlDemo() {
  const [preset, setPreset] = useState<RangePreset>("thisMonth");
  const [from, setFrom] = useState("2026-07-01");
  const [to, setTo] = useState("2026-06-01"); // seeded invalid to show the message
  const invalid = preset === "custom" && from !== "" && to !== "" && from > to;

  return (
    <div
      className={styles.rangeControl}
      role="group"
      aria-label="Khoảng thời gian thống kê"
    >
      <span className={styles.rangeLabel}>Khoảng thời gian</span>
      <div className={styles.presetChips}>
        {PRESETS.map((p) => (
          <button
            key={p.value}
            type="button"
            className={styles.chip}
            aria-pressed={preset === p.value}
            onClick={() => setPreset(p.value)}
          >
            {p.label}
          </button>
        ))}
      </div>

      {preset === "custom" ? (
        <>
          <div className={styles.customRow}>
            <TextField
              className={styles.customField}
              label="Từ ngày"
              type="date"
              value={from}
              onChange={(e) => setFrom(e.target.value)}
            />
            <TextField
              className={styles.customField}
              label="Đến ngày"
              type="date"
              value={to}
              onChange={(e) => setTo(e.target.value)}
              error={invalid ? "“Đến ngày” phải sau hoặc bằng “Từ ngày”." : undefined}
            />
          </div>
          {invalid ? (
            <p className={styles.rangeInvalid} role="alert">
              Khoảng thời gian không hợp lệ — hãy chọn lại.
            </p>
          ) : null}
        </>
      ) : null}
    </div>
  );
}

/* ── 4. Home dashboard composition ────────────────────────────────────────── */
function HomeCompositionSection() {
  return (
    <Section
      title="Bố cục trang chủ (rich home)"
      note="Trang chủ được làm giàu: hàng KPI 'Tháng này' + phân tích danh mục rút gọn + thẻ hoạt động gần đây (5 phiếu mới nhất, mỗi dòng dẫn tới chi tiết) + hành động nhanh + các thẻ liên kết theo vai trò (giữ từ M1). Lưới sạch, dễ quét, co giãn. Tất cả dùng lại hook Thống kê + hook phiếu M4 — không thêm API."
    >
      <div className={styles.home}>
        <div>
          <div className={styles.cardHeadRow}>
            <span className={styles.cardTitle}>Tháng này</span>
            <a className={styles.viewAll} href="#stats">
              Xem thống kê →
            </a>
          </div>
          <OverviewKpiRow total={OVERVIEW_TOTAL} count={OVERVIEW_COUNT} />
        </div>

        <div className={styles.homeGrid}>
          {/* Compact category breakdown (top 5) → /stats */}
          <Card>
            <div className={styles.cardHeadRow}>
              <span className={styles.cardTitle}>Chi tiêu theo danh mục</span>
              <a className={styles.viewAll} href="#stats">
                Xem tất cả →
              </a>
            </div>
            <CompactBreakdown rows={CAT_ROWS.slice(0, 5)} overviewTotal={OVERVIEW_TOTAL} />
          </Card>

          {/* Recent activity + quick actions */}
          <Card>
            <div className={styles.cardHeadRow}>
              <span className={styles.cardTitle}>Hoạt động gần đây</span>
              <a className={styles.viewAll} href="#expenses">
                Xem tất cả →
              </a>
            </div>
            <div className={styles.recentList}>
              {RECENT.map((e) => (
                <a key={e.uuid} className={styles.recentRow} href={`#expense-${e.uuid}`}>
                  <span className={styles.recentMain}>
                    <span className={styles.recentName}>{e.name}</span>
                    <span className={styles.recentMeta}>
                      <CategoryMarker
                        color={e.cat.color}
                        icon={e.cat.icon}
                        name={e.cat.name}
                        showLabel
                        size="sm"
                      />
                      <span className={styles.recentDate}>{e.date}</span>
                    </span>
                  </span>
                  <Money amount={e.total} className={styles.recentAmount} />
                </a>
              ))}
            </div>
            <div className={styles.quickActions} style={{ marginTop: "var(--fs-space-4)" }}>
              <Button variant="primary" size="sm" iconStart={<PlusIcon />}>
                Thêm phiếu chi tiêu
              </Button>
              <Button variant="secondary" size="sm" iconStart={<EventIcon />}>
                Tạo đợt mới
              </Button>
            </div>
          </Card>
        </div>

        {/* Existing role-filtered quick links (kept from M1). */}
        <div>
          <p className={styles.subhead}>Truy cập nhanh</p>
          <div className={styles.quickLinks}>
            <QuickLink title="Phiếu chi tiêu" desc="Xem và tạo phiếu, chia phần gánh." />
            <QuickLink title="Đợt" desc="Nhóm phiếu theo chuyến đi, chốt & cân đối công nợ." />
            <QuickLink title="Danh mục & thẻ" desc="Sắp xếp chi tiêu theo danh mục và thẻ." />
            <QuickLink title="Thành viên" desc="Quản lý người tham gia chia tiền." />
          </div>
        </div>
      </div>
    </Section>
  );
}

/** The home's compact breakdown — same ranked bar rows, tighter spacing. */
function CompactBreakdown({
  rows,
  overviewTotal,
}: {
  rows: CatRow[];
  overviewTotal: number;
}) {
  if (rows.length === 0) {
    return (
      <EmptyState
        title="Chưa có chi tiêu tháng này"
        description="Thêm phiếu để thấy phân tích theo danh mục."
      />
    );
  }
  const maxTotal = Math.max(...rows.map((r) => r.total));
  const ariaLabel = `Chi tiêu theo danh mục tháng này: ${rows.length} danh mục hàng đầu, dẫn đầu là ${rows[0].name}.`;
  return (
    <div className={`${styles.chart} ${styles.compactChart}`} role="img" aria-label={ariaLabel}>
      {rows.map((row, i) => {
        const barColor = i < 8 ? `var(--fs-viz-cat-${i + 1})` : "var(--fs-viz-ink-muted)";
        const widthPct = maxTotal > 0 ? (row.total / maxTotal) * 100 : 0;
        const sharePct = overviewTotal > 0 ? Math.round((row.total / overviewTotal) * 100) : 0;
        return (
          <div key={row.uuid} className={styles.barRow} aria-hidden="true">
            <div className={styles.barHeader}>
              <span className={styles.barLabel}>
                <CategoryMarker color={row.color} icon={row.icon} name={row.name} showLabel size="sm" />
              </span>
              <span className={styles.barValue}>
                <Money amount={row.total} size="sm" />
                <span className={styles.barShare}>{sharePct}%</span>
              </span>
            </div>
            <div className={styles.barTrack}>
              <div
                className={styles.barFill}
                style={{ width: `${widthPct}%`, "--bar-color": barColor } as CSSProperties}
              />
            </div>
          </div>
        );
      })}
    </div>
  );
}

function QuickLink({ title, desc }: { title: string; desc: string }) {
  return (
    <Card>
      <a className={styles.quickLink} href="#">
        <span className={styles.quickLinkTitle}>{title}</span>
        <span className={styles.quickLinkDesc}>{desc}</span>
      </a>
    </Card>
  );
}

/* ── Section shell (local to the M6 showcase, mirrors M4/M5) ───────────────── */
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
