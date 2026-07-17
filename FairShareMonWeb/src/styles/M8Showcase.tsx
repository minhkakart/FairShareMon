import { useState, type ReactNode } from "react";
import {
  Alert,
  Badge,
  Button,
  Card,
  CardBody,
  DescriptionList,
  DescriptionRow,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogTrigger,
  EmptyState,
  FieldStack,
  Form,
  KpiRow,
  KpiTile,
  KpiValue,
  Money,
  Pagination,
  RankedBarChart,
  Select,
  Table,
  TableBody,
  TableCell,
  TableFoot,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextField,
  TierBadge,
  TimeSeriesBarChart,
} from "../components/ui";
import type { RankedBarItem, TimeSeriesBarItem } from "../components/ui";
import styles from "./M8Showcase.module.css";

/* =============================================================================
 * M8 — Admin suite: design showcase
 *
 * The FINAL milestone and the SECOND dataviz consumer, so it is the trigger to
 * extract the M6 KPI/bar primitives into the shared components/ui/charts module
 * (KpiTile · RankedBarChart) and to add the net-new TimeSeriesBarChart. This
 * showcase exercises those shared primitives AND specs the admin-only surfaces:
 *
 *   1. AdminLayout — a tabbed, high-privilege console shell (Bảng chỉ số ·
 *      Doanh thu · Người dùng), visually distinct from the member AppShell.
 *   2. Metrics + revenue dashboards — KPI rows + RankedBarChart distributions
 *      (tier/role/status) + TimeSeriesBarChart (signups / revenue buckets), each
 *      PAIRED with an accessible table. Revenue via <Money> (verbatim, R3).
 *   3. User administration — a filter bar + sortable table + the shared
 *      Pagination primitive (the first paged surface), and the user detail
 *      (metadata + grant history).
 *   4. Sensitive-action dialogs across THREE severity tiers: routine confirm
 *      (enable), danger (disable / revoke-tokens / role demote — Dialog
 *      tone="danger" + self/last-admin guard messaging), and the highest-severity
 *      one-time reset-password reveal (OQ3a: generate a strong temp password,
 *      reveal ONCE with copy-to-clipboard, "copy now — closing destroys this",
 *      value in component state only). Plus the tier grant/revoke dialog.
 *
 * Everything here is DESIGN spec: local state stands in for the API so layout,
 * markup, tokens, and a11y are reviewable in light AND dark. The implementer owns
 * data / hooks / routing / i18n and rebuilds the admin-local compositions; the
 * design layer owns the shared primitives + these specs. Copy is Vietnamese.
 *
 * PRIVACY BOUNDARY (R10): every surface below shows ONLY account metadata +
 * tier-grant/revenue data. No ledger field (members/expenses/events/shares/bank
 * accounts) appears anywhere — by design.
 *
 * dataviz compliance: RankedBarChart fills wear --fs-viz-cat-* by rank (relief
 * rule → direct label + value on every row + paired table). TimeSeriesBarChart
 * is one measure over time → ONE sequential hue (--fs-viz-seq-500, ≥ 3:1 on both
 * surfaces) + paired table. Marks/spacers/reduced-motion honored in the shared
 * primitives.
 * ========================================================================== */

/* ── Local glyphs (decorative — copy carries the meaning) ─────────────────── */
const ShieldIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M10 2.5l6 2.2v4.6c0 3.7-2.5 6.4-6 8.2-3.5-1.8-6-4.5-6-8.2V4.7l6-2.2z" strokeLinejoin="round" />
    <path d="M7.3 10l1.9 1.9L13 8" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const CheckIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.5" aria-hidden="true" width="1em" height="1em">
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const BanIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.8" aria-hidden="true" width="1em" height="1em">
    <circle cx="10" cy="10" r="7" />
    <path d="M5 5l10 10" strokeLinecap="round" />
  </svg>
);
const CopyIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <rect x="7" y="7" width="9" height="9" rx="1.5" />
    <path d="M4 13V4.5A1.5 1.5 0 015.5 3H13" strokeLinecap="round" />
  </svg>
);
const RefreshIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M16 4v4h-4M4 16v-4h4" strokeLinecap="round" strokeLinejoin="round" />
    <path d="M15.5 8a6 6 0 00-10.9-1.5M4.5 12a6 6 0 0010.9 1.5" strokeLinecap="round" />
  </svg>
);
const WarnIcon = () => (
  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M12 3.2 22 20H2L12 3.2Z" strokeLinejoin="round" strokeLinecap="round" />
    <path d="M12 9.5v4.5" strokeLinecap="round" />
    <circle cx="12" cy="17" r="0.6" fill="currentColor" stroke="none" />
  </svg>
);
const UsersIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" width="28" height="28">
    <path d="M9 11a4 4 0 100-8 4 4 0 000 8zm7 0a3 3 0 100-6 3 3 0 000 6zM2 20a7 7 0 0114 0v1H2v-1zm15.5 1v-1a8.5 8.5 0 00-1.7-5.1A5 5 0 0122 20v1h-4.5z" />
  </svg>
);

/* ── Domain unions + thin status/role badges (feature-local per the plan) ─── */
type Tier = "FREE" | "PREMIUM";
type Role = "USER" | "ADMIN";
type Status = "ACTIVE" | "DISABLED";

/** ADMIN wears the accent + shield; USER is neutral. Icon + text, never color. */
function RoleBadge({ role }: { role: Role }) {
  return role === "ADMIN" ? (
    <Badge tone="info" icon={<ShieldIcon />}>
      Quản trị viên
    </Badge>
  ) : (
    <Badge tone="neutral">Người dùng</Badge>
  );
}
/** DISABLED wears danger + a ban glyph; ACTIVE is success + a check. */
function StatusBadge({ status }: { status: Status }) {
  return status === "DISABLED" ? (
    <Badge tone="danger" icon={<BanIcon />}>
      Đã khóa
    </Badge>
  ) : (
    <Badge tone="success" icon={<CheckIcon />}>
      Hoạt động
    </Badge>
  );
}

/* ── Demo data (metadata + tier-grant/revenue ONLY — no ledger fields) ────── */
const TOTAL_USERS = 1_284;

type Dist = { key: string; label: ReactNode; count: number };
const TIER_DIST: Dist[] = [
  { key: "FREE", label: <TierBadge tier="FREE" freeLabel="Free" premiumLabel="Premium" />, count: 1_104 },
  { key: "PREMIUM", label: <TierBadge tier="PREMIUM" freeLabel="Free" premiumLabel="Premium" />, count: 180 },
];
const ROLE_DIST: Dist[] = [
  { key: "USER", label: <RoleBadge role="USER" />, count: 1_281 },
  { key: "ADMIN", label: <RoleBadge role="ADMIN" />, count: 3 },
];
const STATUS_DIST: Dist[] = [
  { key: "ACTIVE", label: <StatusBadge status="ACTIVE" />, count: 1_247 },
  { key: "DISABLED", label: <StatusBadge status="DISABLED" />, count: 37 },
];

type Period = { key: string; label: string; count: number };
const SIGNUPS: Period[] = [
  { key: "2026-02", label: "02/26", count: 64 },
  { key: "2026-03", label: "03/26", count: 88 },
  { key: "2026-04", label: "04/26", count: 132 },
  { key: "2026-05", label: "05/26", count: 96 },
  { key: "2026-06", label: "06/26", count: 174 },
  { key: "2026-07", label: "07/26", count: 121 },
];

type RevenueBucket = { key: string; label: string; total: number; grantCount: number };
const REVENUE_BUCKETS: RevenueBucket[] = [
  { key: "2026-02", label: "02/26", total: 3_600_000, grantCount: 18 },
  { key: "2026-03", label: "03/26", total: 5_200_000, grantCount: 26 },
  { key: "2026-04", label: "04/26", total: 4_400_000, grantCount: 22 },
  { key: "2026-05", label: "05/26", total: 6_800_000, grantCount: 34 },
  { key: "2026-06", label: "06/26", total: 7_400_000, grantCount: 37 },
  { key: "2026-07", label: "07/26", total: 5_000_000, grantCount: 25 },
];
const REVENUE_TOTAL = 32_400_000; // API value — rendered verbatim, never client-summed
const REVENUE_GRANTS = 162;
const REFERENCES = [
  "VCB-20260716-8842 · nguyen.van.a",
  "MB-20260715-1190 · le.thi.b",
  "TCB-20260714-7731 · pham.c",
  "ACB-20260713-5567 · tran.d",
];

type AdminUserRow = {
  uuid: string;
  username: string;
  tier: Tier;
  role: Role;
  status: Status;
  createdAt: string;
  grantCount: number;
  lastGrantAt?: string;
};
const USER_ROWS: AdminUserRow[] = [
  { uuid: "u-9f2a…c410", username: "nguyen.van.a", tier: "PREMIUM", role: "USER", status: "ACTIVE", createdAt: "02/02/2026", grantCount: 2, lastGrantAt: "16/07/2026" },
  { uuid: "u-1b77…88de", username: "le.thi.b", tier: "FREE", role: "USER", status: "ACTIVE", createdAt: "11/03/2026", grantCount: 0 },
  { uuid: "u-33ce…0a12", username: "pham.admin", tier: "PREMIUM", role: "ADMIN", status: "ACTIVE", createdAt: "05/01/2026", grantCount: 1, lastGrantAt: "05/01/2026" },
  { uuid: "u-77a0…5b90", username: "tran.d", tier: "FREE", role: "USER", status: "DISABLED", createdAt: "20/05/2026", grantCount: 0 },
];

/* ── The showcase root — a tabbed admin console over the three surfaces ────── */
type AdminTab = "metrics" | "revenue" | "users";
const TABS: { value: AdminTab; label: string }[] = [
  { value: "metrics", label: "Bảng chỉ số" },
  { value: "revenue", label: "Doanh thu" },
  { value: "users", label: "Người dùng" },
];

export function M8Showcase() {
  return (
    <>
      <AdminConsoleSection />
      <ActionDialogsSection />
    </>
  );
}

/* ── 1–3. The console shell + the three surfaces ──────────────────────────── */
function AdminConsoleSection() {
  const [tab, setTab] = useState<AdminTab>("metrics");
  return (
    <Section
      title="Bảng điều khiển quản trị (AdminLayout — vỏ tab)"
      note="Vỏ console riêng cho khu vực đặc quyền: tiêu đề + eyebrow 'Quản trị' + hàng tab (Bảng chỉ số · Doanh thu · Người dùng), khác hẳn AppShell của người dùng để người vận hành luôn biết mình đang ở khu vực nhạy cảm. Trong ứng dụng mỗi tab là một router link mang aria-current; ở đây dùng state cục bộ để xem trước. Ranh giới riêng tư R10: mọi màn hình chỉ hiển thị metadata tài khoản + dữ liệu cấp Premium/doanh thu — không có dữ liệu sổ chi tiêu."
    >
      <div className={styles.console}>
        <div className={styles.consoleHeader}>
          <span className={styles.eyebrow}>
            <span className={styles.eyebrowIcon}>
              <ShieldIcon />
            </span>
            Quản trị
          </span>
          <h3 className={styles.consoleTitle}>Bảng điều khiển</h3>
          <p className={styles.consoleDesc}>
            Chỉ số hệ thống, doanh thu Premium và quản lý người dùng. Chỉ tài
            khoản có vai trò Quản trị viên mới truy cập được.
          </p>
          {/* In the app: a <nav> of router NavLinks; the active tab carries
              aria-current="page". */}
          <nav className={styles.tabs} aria-label="Khu vực quản trị">
            {TABS.map((t) => (
              <button
                key={t.value}
                type="button"
                className={`${styles.tab}${tab === t.value ? ` ${styles.tabActive}` : ""}`}
                aria-current={tab === t.value ? "page" : undefined}
                onClick={() => setTab(t.value)}
              >
                {t.label}
              </button>
            ))}
          </nav>
        </div>
        <div className={styles.consoleBody}>
          {tab === "metrics" ? <MetricsSurface /> : null}
          {tab === "revenue" ? <RevenueSurface /> : null}
          {tab === "users" ? <UsersSurface /> : null}
        </div>
      </div>
    </Section>
  );
}

/* ── Metrics dashboard ────────────────────────────────────────────────────── */
function MetricsSurface() {
  const maxSignup = Math.max(...SIGNUPS.map((s) => s.count));
  const signupItems: TimeSeriesBarItem[] = SIGNUPS.map((s) => ({
    key: s.key,
    periodLabel: s.label,
    ratio: maxSignup > 0 ? s.count / maxSignup : 0,
    value: s.count.toLocaleString("vi-VN"),
    title: `${s.label}: ${s.count.toLocaleString("vi-VN")} đăng ký`,
  }));

  return (
    <>
      <KpiRow>
        <KpiTile
          label="Tổng người dùng"
          value={<KpiValue>{TOTAL_USERS.toLocaleString("vi-VN")}</KpiValue>}
          hint="Tất cả tài khoản (mọi hạng, mọi trạng thái)"
        />
        <KpiTile
          label="Đang hoạt động"
          value={<KpiValue>{(1_247).toLocaleString("vi-VN")}</KpiValue>}
        />
        <KpiTile label="Đang tải (ví dụ)" loading />
      </KpiRow>

      <div className={styles.dashGrid}>
        <DistributionPanel
          title="Theo hạng tài khoản"
          ariaLabel="Phân bố theo hạng: Free dẫn đầu. Xem bảng bên dưới để biết chi tiết."
          items={TIER_DIST}
          total={TOTAL_USERS}
        />
        <DistributionPanel
          title="Theo vai trò"
          ariaLabel="Phân bố theo vai trò: hầu hết là Người dùng. Xem bảng bên dưới."
          items={ROLE_DIST}
          total={TOTAL_USERS}
        />
        <DistributionPanel
          title="Theo trạng thái"
          ariaLabel="Phân bố theo trạng thái: đa số Hoạt động. Xem bảng bên dưới."
          items={STATUS_DIST}
          total={TOTAL_USERS}
        />
      </div>

      <Card>
        <CardBody>
          <h4 className={styles.panelTitle}>Đăng ký theo thời gian</h4>
          <TimeSeriesBarChart
            items={signupItems}
            ariaLabel="Số lượt đăng ký mỗi tháng trong 6 tháng qua, cao nhất là tháng 06/26. Xem bảng bên dưới."
          />
          <p className={styles.subhead}>Bảng số đi kèm</p>
          <Table caption="Đăng ký theo tháng" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Kỳ</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số lượt đăng ký
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {SIGNUPS.map((s) => (
                <TableRow key={s.key}>
                  <TableHeaderCell scope="row">{s.label}</TableHeaderCell>
                  <TableCell numeric>{s.count.toLocaleString("vi-VN")}</TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </CardBody>
      </Card>
    </>
  );
}

/** A distribution panel: a RankedBarChart above its paired accessible table.
 *  The label SLOT carries a Badge/TierBadge so identity + state never rest on
 *  the bar color; the bar fill is decorative (--fs-viz-cat-* by rank). */
function DistributionPanel({
  title,
  ariaLabel,
  items,
  total,
}: {
  title: string;
  ariaLabel: string;
  items: Dist[];
  total: number;
}) {
  const max = Math.max(...items.map((d) => d.count));
  const barItems: RankedBarItem[] = items.map((d) => ({
    key: d.key,
    label: d.label,
    value: d.count.toLocaleString("vi-VN"),
    ratio: max > 0 ? d.count / max : 0,
    meta: total > 0 ? `${Math.round((d.count / total) * 100)}%` : "0%",
  }));
  return (
    <Card>
      <CardBody>
        <div className={styles.panelStack}>
          <h4 className={styles.panelTitle}>{title}</h4>
          <RankedBarChart items={barItems} ariaLabel={ariaLabel} compact />
          <Table caption={title} captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Nhóm</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số lượng
                </TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Tỷ trọng
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {items.map((d) => (
                <TableRow key={d.key}>
                  <TableHeaderCell scope="row">{d.label}</TableHeaderCell>
                  <TableCell numeric>{d.count.toLocaleString("vi-VN")}</TableCell>
                  <TableCell numeric>
                    {total > 0 ? Math.round((d.count / total) * 100) : 0}%
                  </TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFoot>
              <TableRow total>
                <TableHeaderCell scope="row">Tổng</TableHeaderCell>
                <TableCell numeric>{total.toLocaleString("vi-VN")}</TableCell>
                <TableCell numeric>100%</TableCell>
              </TableRow>
            </TableFoot>
          </Table>
        </div>
      </CardBody>
    </Card>
  );
}

/* ── Revenue dashboard ────────────────────────────────────────────────────── */
function RevenueSurface() {
  const maxTotal = Math.max(...REVENUE_BUCKETS.map((b) => b.total));
  const revItems: TimeSeriesBarItem[] = REVENUE_BUCKETS.map((b) => ({
    key: b.key,
    periodLabel: b.label,
    ratio: maxTotal > 0 ? b.total / maxTotal : 0,
    value: <Money amount={b.total} size="sm" />,
    title: `${b.label}: ${b.total.toLocaleString("vi-VN")} ₫ · ${b.grantCount} lượt cấp`,
  }));

  return (
    <>
      <KpiRow>
        <KpiTile
          label="Tổng doanh thu"
          value={<Money amount={REVENUE_TOTAL} size="xl" />}
          hint="Tổng các lượt CẤP Premium (REVOKE không tính) — giá trị từ API"
        />
        <KpiTile
          label="Số lượt cấp"
          value={<KpiValue>{REVENUE_GRANTS.toLocaleString("vi-VN")}</KpiValue>}
        />
      </KpiRow>

      <Card>
        <CardBody>
          <h4 className={styles.panelTitle}>Doanh thu theo thời gian</h4>
          <TimeSeriesBarChart
            items={revItems}
            ariaLabel="Doanh thu Premium mỗi tháng trong 6 tháng qua, cao nhất là tháng 06/26. Xem bảng bên dưới."
          />
          <p className={styles.subhead}>Bảng số đi kèm</p>
          <Table caption="Doanh thu theo tháng" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Kỳ</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Doanh thu
                </TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số lượt cấp
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {REVENUE_BUCKETS.map((b) => (
                <TableRow key={b.key}>
                  <TableHeaderCell scope="row">{b.label}</TableHeaderCell>
                  <TableCell numeric>
                    <Money amount={b.total} />
                  </TableCell>
                  <TableCell numeric>{b.grantCount}</TableCell>
                </TableRow>
              ))}
            </TableBody>
            <TableFoot>
              <TableRow total>
                <TableHeaderCell scope="row">Tổng</TableHeaderCell>
                <TableCell numeric>
                  <Money amount={REVENUE_TOTAL} />
                </TableCell>
                <TableCell numeric>{REVENUE_GRANTS}</TableCell>
              </TableRow>
            </TableFoot>
          </Table>
        </CardBody>
      </Card>

      <Card>
        <CardBody>
          <h4 className={styles.panelTitle}>Mã tham chiếu thanh toán (mới nhất)</h4>
          <ul className={styles.refList}>
            {REFERENCES.map((r) => (
              <li key={r} className={styles.refItem}>
                <span className={styles.refText}>{r}</span>
              </li>
            ))}
          </ul>
        </CardBody>
      </Card>
    </>
  );
}

/* ── User administration ──────────────────────────────────────────────────── */
function UsersSurface() {
  const [page, setPage] = useState(3);
  return (
    <>
      <p className={styles.note}>
        Bộ lọc (hạng · trạng thái · vai trò · tìm theo tên) đồng bộ URL; tiêu đề
        cột sắp xếp được (mặc định Ngày tạo giảm dần); phân trang là primitive
        Pagination dùng chung. Chỉ hiển thị metadata tài khoản + số lượt cấp — R10.
      </p>

      {/* Filter bar */}
      <div className={styles.filterBar}>
        <div className={styles.filterField}>
          <Select
            label="Hạng"
            value="all"
            onValueChange={() => {}}
            options={[
              { value: "all", label: "Tất cả hạng" },
              { value: "FREE", label: "Free" },
              { value: "PREMIUM", label: "Premium" },
            ]}
          />
        </div>
        <div className={styles.filterField}>
          <Select
            label="Trạng thái"
            value="all"
            onValueChange={() => {}}
            options={[
              { value: "all", label: "Tất cả trạng thái" },
              { value: "ACTIVE", label: "Hoạt động" },
              { value: "DISABLED", label: "Đã khóa" },
            ]}
          />
        </div>
        <div className={styles.filterField}>
          <Select
            label="Vai trò"
            value="all"
            onValueChange={() => {}}
            options={[
              { value: "all", label: "Tất cả vai trò" },
              { value: "USER", label: "Người dùng" },
              { value: "ADMIN", label: "Quản trị viên" },
            ]}
          />
        </div>
        <div className={styles.filterSearch}>
          <TextField
            label="Tìm theo tên đăng nhập"
            placeholder="vd: nguyen.van.a"
          />
        </div>
      </div>

      {/* User table + pagination */}
      <div className={styles.listStack}>
        <Table caption="Danh sách người dùng">
          <TableHead>
            <TableRow>
              <TableHeaderCell scope="col">Tên đăng nhập</TableHeaderCell>
              <TableHeaderCell scope="col">Hạng</TableHeaderCell>
              <TableHeaderCell scope="col">Vai trò</TableHeaderCell>
              <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
              <TableHeaderCell scope="col">Ngày tạo</TableHeaderCell>
              <TableHeaderCell scope="col" numeric>
                Số lượt cấp
              </TableHeaderCell>
              <TableHeaderCell scope="col">Cấp gần nhất</TableHeaderCell>
              <TableHeaderCell scope="col">
                <span className={styles.srOnly}>Thao tác</span>
              </TableHeaderCell>
            </TableRow>
          </TableHead>
          <TableBody>
            {USER_ROWS.map((u) => (
              <TableRow key={u.uuid} deleted={u.status === "DISABLED"}>
                <TableHeaderCell scope="row">{u.username}</TableHeaderCell>
                <TableCell>
                  <TierBadge tier={u.tier} freeLabel="Free" premiumLabel="Premium" />
                </TableCell>
                <TableCell>
                  <RoleBadge role={u.role} />
                </TableCell>
                <TableCell>
                  <StatusBadge status={u.status} />
                </TableCell>
                <TableCell>
                  <span className={styles.mono}>{u.createdAt}</span>
                </TableCell>
                <TableCell numeric>{u.grantCount}</TableCell>
                <TableCell>
                  <span className={styles.mono}>{u.lastGrantAt ?? "—"}</span>
                </TableCell>
                <TableCell actions>
                  <Button variant="ghost" size="sm" aria-label={`Xem ${u.username}`}>
                    Xem
                  </Button>
                </TableCell>
              </TableRow>
            ))}
          </TableBody>
        </Table>

        <Pagination
          page={page}
          pageCount={12}
          onPageChange={setPage}
          label="Phân trang người dùng"
          prevLabel="Trang trước"
          nextLabel="Trang sau"
          pageInfo={(p, n) => `Trang ${p} / ${n}`}
          pageLabel={(n) => `Trang ${n}`}
        />
      </div>

      <UserDetailPreview />
    </>
  );
}

/** The user-detail layout: metadata DescriptionList + grant-history table +
 *  the sensitive-action bar. A 14000 miss → an admin-local not-found (the admin
 *  scope MAY confirm a user exists, so this is not the ledger existence-hiding
 *  404). */
function UserDetailPreview() {
  return (
    <>
      <p className={styles.subhead}>Chi tiết người dùng</p>
      <Card>
        <CardBody>
          <DescriptionList>
            <DescriptionRow term="Tên đăng nhập">nguyen.van.a</DescriptionRow>
            <DescriptionRow term="Mã người dùng">
              <span className={styles.mono}>u-9f2a…c410</span>
            </DescriptionRow>
            <DescriptionRow term="Hạng">
              <TierBadge tier="PREMIUM" freeLabel="Free" premiumLabel="Premium" />
            </DescriptionRow>
            <DescriptionRow term="Vai trò">
              <RoleBadge role="USER" />
            </DescriptionRow>
            <DescriptionRow term="Trạng thái">
              <StatusBadge status="ACTIVE" />
            </DescriptionRow>
            <DescriptionRow term="Tham gia từ">
              <span className={styles.mono}>02/02/2026</span>
            </DescriptionRow>
          </DescriptionList>
        </CardBody>
      </Card>

      <p className={styles.subhead}>Lịch sử cấp/thu hồi Premium</p>
      <Card>
        <CardBody>
          <Table caption="Lịch sử cấp Premium của nguyen.van.a" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Hành động</TableHeaderCell>
                <TableHeaderCell scope="col">Hạng</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số tiền
                </TableHeaderCell>
                <TableHeaderCell scope="col">Tham chiếu</TableHeaderCell>
                <TableHeaderCell scope="col">Ghi chú</TableHeaderCell>
                <TableHeaderCell scope="col">Người thực hiện</TableHeaderCell>
                <TableHeaderCell scope="col">Thời điểm</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableRow>
                <TableCell>
                  <Badge tone="success" icon={<CheckIcon />}>
                    Cấp
                  </Badge>
                </TableCell>
                <TableCell>Premium</TableCell>
                <TableCell numeric>
                  <Money amount={200_000} />
                </TableCell>
                <TableCell>
                  <span className={styles.mono}>VCB-20260716-8842</span>
                </TableCell>
                <TableCell>Gia hạn 1 năm</TableCell>
                <TableCell>pham.admin</TableCell>
                <TableCell>
                  <span className={styles.mono}>16/07/2026</span>
                </TableCell>
              </TableRow>
              <TableRow>
                <TableCell>
                  <Badge tone="neutral" icon={<BanIcon />}>
                    Thu hồi
                  </Badge>
                </TableCell>
                <TableCell>Free</TableCell>
                <TableCell numeric>—</TableCell>
                <TableCell>—</TableCell>
                <TableCell>Yêu cầu hoàn tiền</TableCell>
                <TableCell>pham.admin</TableCell>
                <TableCell>
                  <span className={styles.mono}>10/01/2026</span>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </CardBody>
      </Card>

      <p className={styles.subhead}>Trạng thái không tìm thấy (14000, cục bộ admin)</p>
      <Card>
        <CardBody>
          <EmptyState
            icon={<UsersIcon />}
            title="Không tìm thấy người dùng"
            description="Người dùng này không tồn tại hoặc đã bị xóa. Kiểm tra lại đường dẫn hoặc quay về danh sách."
            action={
              <Button variant="secondary" size="sm">
                Về danh sách
              </Button>
            }
          />
        </CardBody>
      </Card>
    </>
  );
}

/* ── 4. Sensitive-action dialogs — three severity tiers ───────────────────── */
function ActionDialogsSection() {
  return (
    <Section
      title="Hộp thoại hành động nhạy cảm — ba mức độ"
      note="Ba mức nghiêm trọng: (1) xác nhận thường (Mở khóa) — Dialog mặc định; (2) nguy hiểm (Khóa · Đăng xuất mọi thiết bị · Hạ vai trò) — Dialog tone='danger' + nút danger + thông điệp hậu quả, và bị VÔ HIỆU kèm tooltip khi mục tiêu là chính mình (14001) hoặc một Quản trị viên khác (14002); (3) tiết-lộ-một-lần đặt lại mật khẩu — mức cao nhất. Ngoài ra: cấp/thu hồi Premium (nhập tiền + tham chiếu/ghi chú)."
    >
      <div className={styles.actionBar}>
        <EnableUserDialogDemo />
        <DisableUserDialogDemo />
        <RevokeTokensDialogDemo />
        <DemoteRoleDialogDemo />
        <ResetPasswordDialogDemo />
        <TierGrantDialogDemo />
        <TierRevokeDialogDemo />
      </div>

      <p className={styles.subhead}>Hành động bị chặn (self 14001 / admin khác 14002)</p>
      <p className={styles.note}>
        Với mục tiêu là chính mình hoặc một Quản trị viên khác, các hành động
        nguy hiểm (Khóa · Đăng xuất · Đặt lại mật khẩu · Hạ vai trò) hiển thị VÔ
        HIỆU và bọc trong một phần tử mang tooltip giải thích; cấp/thu hồi Premium
        và thăng vai trò vẫn bật. Client vẫn xử lý 14001/14002 nếu server từ chối.
      </p>
      <div className={styles.actionBar}>
        <span title="Không thể tự khóa tài khoản của mình.">
          <Button variant="danger" size="sm" disabled aria-disabled="true">
            Khóa (chính mình — bị chặn)
          </Button>
        </span>
        <span title="Không thể thao tác lên một Quản trị viên khác.">
          <Button variant="danger" size="sm" disabled aria-disabled="true">
            Đặt lại mật khẩu (admin khác — bị chặn)
          </Button>
        </span>
      </div>
    </Section>
  );
}

/* Tier 1 — routine confirm. */
function EnableUserDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="secondary" size="sm">
          Mở khóa (thường)
        </Button>
      </DialogTrigger>
      <DialogContent
        title="Mở khóa tài khoản?"
        description="Người dùng sẽ đăng nhập lại được. Không ảnh hưởng dữ liệu."
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="ghost">Hủy</Button>
          </DialogClose>
          <DialogClose asChild>
            <Button variant="primary">Mở khóa</Button>
          </DialogClose>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* Tier 2 — danger confirm (disable). */
function DisableUserDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="danger" size="sm">
          Khóa (nguy hiểm)
        </Button>
      </DialogTrigger>
      <DialogContent
        tone="danger"
        title="Khóa tài khoản này?"
        description="Người dùng sẽ bị đăng xuất khỏi mọi thiết bị và không đăng nhập được cho tới khi được mở khóa."
      >
        <Alert tone="warning" title="Hậu quả">
          Khóa sẽ thu hồi toàn bộ phiên đăng nhập và chặn đăng nhập (14003). Dữ
          liệu của người dùng không bị xóa.
        </Alert>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="ghost">Hủy</Button>
          </DialogClose>
          <Button variant="danger">Khóa tài khoản</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* Tier 2 — danger confirm (revoke tokens). */
function RevokeTokensDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="danger" size="sm">
          Đăng xuất mọi thiết bị
        </Button>
      </DialogTrigger>
      <DialogContent
        tone="danger"
        title="Đăng xuất khỏi mọi thiết bị?"
        description="Mọi phiên đăng nhập hiện tại của người dùng sẽ bị thu hồi ngay. Họ vẫn đăng nhập lại được."
      >
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="ghost">Hủy</Button>
          </DialogClose>
          <Button variant="danger">Thu hồi phiên</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* Tier 2 — danger confirm (role demote), with last-admin 14002 messaging. */
function DemoteRoleDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="danger" size="sm">
          Hạ vai trò
        </Button>
      </DialogTrigger>
      <DialogContent
        tone="danger"
        title="Hạ về Người dùng?"
        description="Tài khoản này sẽ mất quyền quản trị và không truy cập được khu vực này nữa."
      >
        <Alert tone="warning" title="Lưu ý quản trị">
          Không thể hạ vai trò của Quản trị viên cuối cùng (14002). Server là bên
          quyết định — thông báo lỗi sẽ hiển thị nguyên văn nếu bị từ chối.
        </Alert>
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="ghost">Hủy</Button>
          </DialogClose>
          <Button variant="danger">Hạ vai trò</Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* Tier 3 — the one-time reset-password reveal (OQ3a, highest severity). */
function ResetPasswordDialogDemo() {
  const [open, setOpen] = useState(false);
  return (
    <Dialog open={open} onOpenChange={setOpen}>
      <DialogTrigger asChild>
        <Button variant="danger" size="sm">
          Đặt lại mật khẩu (tiết lộ 1 lần)
        </Button>
      </DialogTrigger>
      {open ? <ResetPasswordDialogBody onDone={() => setOpen(false)} /> : null}
    </Dialog>
  );
}

/** Strong random temp password (12–16 chars, ≥1 of each class). Demonstrates the
 *  pattern: generated client-side, held in component state only, never persisted
 *  or logged. */
function generateTempPassword(len = 14): string {
  const sets = [
    "ABCDEFGHJKLMNPQRSTUVWXYZ",
    "abcdefghijkmnpqrstuvwxyz",
    "23456789",
    "!@#$%^&*?",
  ];
  const all = sets.join("");
  const rand = (n: number) => {
    const b = new Uint32Array(1);
    crypto.getRandomValues(b);
    return b[0] % n;
  };
  const chars = sets.map((s) => s[rand(s.length)]);
  for (let i = chars.length; i < len; i++) chars.push(all[rand(all.length)]);
  // Fisher–Yates shuffle so the guaranteed chars aren't always in front.
  for (let i = chars.length - 1; i > 0; i--) {
    const j = rand(i + 1);
    [chars[i], chars[j]] = [chars[j], chars[i]];
  }
  return chars.join("");
}

function ResetPasswordDialogBody({ onDone }: { onDone: () => void }) {
  // Phase: "form" (choose/generate) → "reveal" (one-time secret). The temp
  // password lives ONLY here in component state — never in a query cache, never
  // persisted, never logged; cleared when the dialog unmounts (parent gates on
  // `open`, so closing unmounts this body).
  const [phase, setPhase] = useState<"form" | "reveal">("form");
  const [temp, setTemp] = useState(() => generateTempPassword());
  const [revealed, setRevealed] = useState("");
  const [copied, setCopied] = useState(false);

  const submit = () => {
    // In the app: useResetPassword({ newPassword: temp }); the response echoes
    // the password ONCE. Here we simply carry the generated value into reveal.
    setRevealed(temp);
    setPhase("reveal");
  };
  const copy = async () => {
    try {
      await navigator.clipboard.writeText(revealed);
      setCopied(true);
    } catch {
      setCopied(false);
    }
  };

  if (phase === "form") {
    return (
      <DialogContent
        tone="danger"
        title="Đặt lại mật khẩu"
        description="Tạo mật khẩu tạm mạnh cho người dùng. Mật khẩu sẽ hiển thị MỘT LẦN sau khi xác nhận — hãy chuẩn bị sao chép và bàn giao ngoài luồng."
      >
        <Form onSubmit={(e) => e.preventDefault()}>
          <FieldStack>
            <div className={styles.genRow}>
              <div className={styles.genField}>
                <TextField
                  label="Mật khẩu tạm (tạo tự động)"
                  value={temp}
                  onChange={(e) => setTemp(e.target.value)}
                  hint="12–16 ký tự, đủ loại. Có thể tạo lại hoặc chỉnh trước khi xác nhận."
                />
              </div>
              <Button
                type="button"
                variant="secondary"
                iconStart={<RefreshIcon />}
                onClick={() => setTemp(generateTempPassword())}
              >
                Tạo lại
              </Button>
            </div>
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="ghost">Hủy</Button>
            </DialogClose>
            <Button variant="danger" onClick={submit}>
              Đặt lại mật khẩu
            </Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    );
  }

  return (
    <DialogContent
      tone="danger"
      showClose={false}
      title="Mật khẩu tạm — chỉ hiển thị một lần"
      description="Sao chép ngay bây giờ. Khi đóng hộp thoại, giá trị này sẽ biến mất và không thể xem lại."
    >
      <div className={styles.secretPanel}>
        <span className={styles.secretLabel}>Mật khẩu tạm cho nguyen.van.a</span>
        <div className={styles.secretRow}>
          <code className={styles.secretValue}>{revealed}</code>
          <Button
            type="button"
            variant="secondary"
            iconStart={copied ? <CheckIcon /> : <CopyIcon />}
            onClick={copy}
          >
            {copied ? "Đã sao chép" : "Sao chép"}
          </Button>
        </div>
        <p className={styles.secretWarning}>
          <span className={styles.secretWarningIcon}>
            <WarnIcon />
          </span>
          Sao chép ngay — đóng hộp thoại sẽ hủy giá trị này. Không được lưu hay ghi
          log ở bất kỳ đâu.
        </p>
        {/* Live region: announces the copy to assistive tech. */}
        <span className={styles.srOnly} role="status" aria-live="polite">
          {copied ? "Đã sao chép mật khẩu tạm vào bộ nhớ tạm." : ""}
        </span>
        {copied ? (
          <span className={styles.copiedNote}>
            <CheckIcon /> Đã sao chép vào bộ nhớ tạm
          </span>
        ) : null}
      </div>
      <DialogFooter>
        <Button variant="primary" onClick={onDone}>
          Tôi đã sao chép — Đóng
        </Button>
      </DialogFooter>
    </DialogContent>
  );
}

/* Tier grant — money input + reference/note. */
function TierGrantDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="premium" size="sm">
          Cấp Premium
        </Button>
      </DialogTrigger>
      <DialogContent
        title="Cấp Premium"
        description="Ghi nhận một lượt cấp Premium (số tiền tính vào doanh thu)."
      >
        <Form onSubmit={(e) => e.preventDefault()}>
          <FieldStack>
            <TextField label="Số tiền (VND)" placeholder="200.000" inputMode="numeric" />
            <TextField label="Mã tham chiếu (tùy chọn)" placeholder="VCB-20260716-8842" />
            <TextField label="Ghi chú (tùy chọn)" placeholder="Gia hạn 1 năm" />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="ghost">Hủy</Button>
            </DialogClose>
            <Button variant="primary">Cấp Premium</Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}

/* Tier revoke — lightweight danger confirm + optional note. */
function TierRevokeDialogDemo() {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant="secondary" size="sm">
          Thu hồi Premium
        </Button>
      </DialogTrigger>
      <DialogContent
        tone="danger"
        title="Thu hồi Premium?"
        description="Tài khoản trở về hạng Free. Lượt thu hồi KHÔNG tính vào doanh thu."
      >
        <Form onSubmit={(e) => e.preventDefault()}>
          <FieldStack>
            <TextField label="Ghi chú (tùy chọn)" placeholder="Lý do thu hồi" />
          </FieldStack>
          <DialogFooter>
            <DialogClose asChild>
              <Button variant="ghost">Hủy</Button>
            </DialogClose>
            <Button variant="danger">Thu hồi</Button>
          </DialogFooter>
        </Form>
      </DialogContent>
    </Dialog>
  );
}

/* ── Section shell (mirrors M4/M5/M6/M7) ──────────────────────────────────── */
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
