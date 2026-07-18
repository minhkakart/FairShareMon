import { useEffect, useState } from "react";
import {
  Alert,
  AppShell,
  AuthLayout,
  Badge,
  Button,
  Card,
  CardBody,
  CardHeader,
  CategoryMarker,
  ColorPicker,
  Dialog,
  DialogContent,
  DialogFooter,
  DialogTrigger,
  DescriptionList,
  DescriptionRow,
  EmptyState,
  FieldStack,
  Form,
  FormActions,
  FormError,
  IconPicker,
  LanguageToggle,
  LimitNotice,
  Money,
  NavItem,
  PageHeader,
  Skeleton,
  Spinner,
  Stack,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
  TextField,
  ThemeToggle,
  TierBadge,
  UpgradePrompt,
} from "../components/ui";
import type { Locale, ThemePreference } from "../components/ui";
import { M4Showcase } from "./M4Showcase";
import { M5Showcase } from "./M5Showcase";
import { M6Showcase } from "./M6Showcase";
import { M7Showcase } from "./M7Showcase";
import { M8Showcase } from "./M8Showcase";
import styles from "./StyleGuide.module.css";

/**
 * Living style guide — a reviewable showcase of the FairShareMon design system:
 * every token group and primitive, in light AND dark. This is a DESIGN review
 * harness, not app plumbing; it owns a local theme/locale toggle purely to
 * demonstrate the attribute contract the implementer will wire for real.
 */
export function StyleGuide() {
  const [theme, setTheme] = useState<ThemePreference>("system");
  const [locale, setLocale] = useState<Locale>("vi-VN");

  useEffect(() => {
    const root = document.documentElement;
    if (theme === "system") root.removeAttribute("data-theme");
    else root.setAttribute("data-theme", theme);
  }, [theme]);

  const brand = (
    <span>
      <span aria-hidden="true">◆</span> FairShareMon
    </span>
  );

  const languageToggle = (
    <LanguageToggle
      value={locale}
      onChange={setLocale}
      groupLabel="Ngôn ngữ"
      labels={{ "vi-VN": "Tiếng Việt", "en-US": "English" }}
    />
  );
  const themeToggle = (
    <ThemeToggle
      value={theme}
      onChange={setTheme}
      groupLabel="Giao diện"
      labels={{ light: "Sáng", system: "Theo hệ thống", dark: "Tối" }}
    />
  );
  const actions = (
    <>
      {languageToggle}
      {themeToggle}
    </>
  );
  // Below `lg` the header keeps brand + hamburger only; the same controls move
  // into the drawer footer via `secondaryActions` (OQ5a). Open the mobile menu
  // (narrow the window or use the hamburger) to review the footer.
  const secondaryActions = (
    <>
      {languageToggle}
      {themeToggle}
      <Button variant="ghost" size="sm" fullWidth>
        Tài khoản
      </Button>
      <Button variant="secondary" size="sm" fullWidth>
        Đăng xuất
      </Button>
    </>
  );

  return (
    <AppShell
      brand={brand}
      actions={actions}
      secondaryActions={secondaryActions}
      mobileMenuLabel="Menu"
      mobileMenuCloseLabel="Đóng"
      nav={
        <>
          <NavItem active>Tổng quan</NavItem>
          <NavItem>Phiếu chi tiêu</NavItem>
          <NavItem>Đợt</NavItem>
          <NavItem>Thống kê</NavItem>
        </>
      }
    >
      <div className={styles.page}>
        <header className={styles.intro}>
          <h1>Hệ thống thiết kế FairShareMon</h1>
          <p>
            Nền tảng giao diện cho sổ ghi nợ chi tiêu — chính xác, đáng tin, mà
            vẫn gần gũi. Sắc chủ đạo ngọc bích gợi "tiền". Chuyển sáng/tối và
            ngôn ngữ bằng nút ở góc trên.
          </p>
        </header>

        {/* COLOR TOKENS */}
        <Section title="Màu ngữ nghĩa (semantic tokens)">
          <div className={styles.swatchGrid}>
            <Swatch token="--fs-color-primary" label="primary" onDark />
            <Swatch token="--fs-color-accent" label="accent" onDark />
            <Swatch token="--fs-color-surface" label="surface" />
            <Swatch token="--fs-color-bg" label="bg" />
            <Swatch token="--fs-color-success" label="success" onDark />
            <Swatch token="--fs-color-warning" label="warning" onDark />
            <Swatch token="--fs-color-danger" label="danger" onDark />
            <Swatch token="--fs-color-info" label="info" onDark />
            <Swatch token="--fs-color-premium" label="premium" onDark />
            <Swatch token="--fs-color-credit-text" label="credit" onDark />
            <Swatch token="--fs-color-debit-text" label="debit" onDark />
            <Swatch token="--fs-color-settled-text" label="settled" onDark />
          </div>
        </Section>

        {/* DATA-VIZ PALETTE */}
        <Section title="Bảng màu biểu đồ (data-viz — dành cho Thống kê/Admin)">
          <p className={styles.note}>
            Thứ tự categorical là cơ chế an toàn CVD — gán tuần tự, không lặp
            vòng. Trên nền sáng ba slot 3/4/5 dưới 3:1 nên biểu đồ phải kèm nhãn
            trực tiếp hoặc bảng số.
          </p>
          <div className={styles.vizRow}>
            {[1, 2, 3, 4, 5, 6, 7, 8].map((i) => (
              <span
                key={i}
                className={styles.vizChip}
                style={{ background: `var(--fs-viz-cat-${i})` }}
                title={`cat-${i}`}
              />
            ))}
          </div>
          <div className={styles.vizRow}>
            {[100, 200, 300, 400, 500, 600, 700].map((s) => (
              <span
                key={s}
                className={styles.vizChip}
                style={{ background: `var(--fs-viz-seq-${s})` }}
                title={`seq-${s}`}
              />
            ))}
          </div>
        </Section>

        {/* TYPOGRAPHY */}
        <Section title="Typography (chịu được tiếng Việt dài)">
          <div className={styles.typeStack}>
            <p className={styles.t3xl}>2.25rem — Số tiền lớn</p>
            <p className={styles.t2xl}>1.75rem — Tiêu đề trang</p>
            <p className={styles.txl}>1.375rem — Tiêu đề mục</p>
            <p className={styles.tlg}>1.125rem — Đoạn mở đầu / tiêu đề thẻ</p>
            <p className={styles.tmd}>
              1rem — Nội dung chính. Khoản chi được chia cho các thành viên tham
              gia, mỗi người gánh một phần gánh khác nhau tùy theo thỏa thuận.
            </p>
            <p className={styles.tsm}>
              0.875rem — Phụ, chú thích, giao diện dày đặc
            </p>
            <p className={styles.txs}>0.75rem — Nhãn, badge</p>
          </div>
        </Section>

        {/* BUTTONS */}
        <Section title="Button">
          <div className={styles.row}>
            <Button variant="primary">Lưu phiếu</Button>
            <Button variant="secondary">Hủy</Button>
            <Button variant="ghost">Xem thêm</Button>
            <Button variant="danger">Xóa phiếu</Button>
            <Button variant="premium">Nâng cấp Premium</Button>
          </div>
          <div className={styles.row}>
            <Button variant="primary" size="sm">
              Nhỏ
            </Button>
            <Button variant="primary" size="md">
              Vừa
            </Button>
            <Button variant="primary" size="lg">
              Lớn
            </Button>
            <Button variant="primary" loading>
              Đang lưu
            </Button>
            <Button variant="secondary" disabled>
              Vô hiệu
            </Button>
          </div>
        </Section>

        {/* MONEY & STATES */}
        <Section title="Tiền (VND) & trạng thái cân bằng">
          <div className={styles.row}>
            <Money amount={2_450_000} size="lg" />
            <Money amount={300_000} variant="balance" size="lg" />
            <Money amount={-500_000} variant="balance" size="lg" />
            <Money amount={0} variant="balance" size="lg" />
          </div>
          <div className={styles.row}>
            <Badge tone="success" icon={<Dot />}>
              Đợt đang mở
            </Badge>
            <Badge tone="neutral" icon={<Lock />}>
              Đã chốt
            </Badge>
            <Badge tone="settled" icon={<Check />}>
              Đã trả
            </Badge>
            <Badge tone="premium" icon={<Star />}>
              Premium
            </Badge>
            <Badge tone="free">Free</Badge>
          </div>
        </Section>

        {/* FORM */}
        <Section title="Form & input">
          <Card>
            <Form onSubmit={(e) => e.preventDefault()}>
              <FieldStack>
                <TextField
                  label="Tên đăng nhập"
                  hint="3–32 ký tự, chỉ chữ thường, số và . _ -"
                  placeholder="an.nguyen"
                  required
                />
                <TextField
                  label="Mật khẩu"
                  type="password"
                  error="Mật khẩu phải có ít nhất 8 ký tự."
                  required
                />
              </FieldStack>
              <FormError>Tên đăng nhập hoặc mật khẩu không đúng.</FormError>
              <FormActions>
                <Button variant="ghost">Hủy</Button>
                <Button variant="primary" type="submit">
                  Đăng nhập
                </Button>
              </FormActions>
            </Form>
          </Card>
        </Section>

        {/* FEEDBACK */}
        <Section title="Thông báo & trạng thái">
          <div className={styles.stack}>
            <Alert tone="info" title="Mẹo">
              Không chọn người trả thì mặc định là bạn (thành viên đại diện).
            </Alert>
            <Alert tone="success" title="Đã lưu">
              Phiếu chi tiêu đã được tạo.
            </Alert>
            <Alert tone="warning" title="Đợt sắp kết thúc">
              Nhớ chốt đợt trước khi tạo mã QR.
            </Alert>
            <Alert
              tone="danger"
              title="Đợt đã chốt"
              action={
                <Button size="sm" variant="secondary">
                  Xem đợt
                </Button>
              }
            >
              Không thể sửa phiếu trong đợt đã chốt.
            </Alert>
          </div>
        </Section>

        <Section title="Premium & hạn mức">
          <div className={styles.row}>
            <TierBadge tier="FREE" freeLabel="Free" premiumLabel="Premium" />
            <TierBadge tier="PREMIUM" freeLabel="Free" premiumLabel="Premium" />
          </div>
          <div className={styles.stack}>
            <UpgradePrompt
              title="Tính năng Premium"
              description="Ví & mã QR chuyển khoản chỉ dành cho tài khoản Premium."
              action={
                <Button variant="premium" size="sm">
                  Nâng cấp
                </Button>
              }
            />
            <UpgradePrompt
              variant="info"
              title="Nâng cấp Premium"
              description="Premium được cấp thủ công bởi quản trị viên — chưa có mua tự phục vụ. Liên hệ người vận hành để được mở khóa ví, mã QR và các định dạng xuất mở rộng."
            />
            <UpgradePrompt
              variant="active"
              title="Bạn đang dùng Premium"
              description="Tài khoản của bạn đã mở khóa toàn bộ tính năng Premium."
            />
            <LimitNotice
              title="Đã đạt hạn mức đợt đang mở (Free)"
              description="Bạn có thể chốt một đợt hiện có hoặc nâng cấp để tạo thêm. Dữ liệu cũ không bị ảnh hưởng."
              action={
                <Button variant="premium" size="sm">
                  Nâng cấp Premium
                </Button>
              }
            />
          </div>
        </Section>

        <Section title="Bố cục trang & danh sách mô tả (settings)">
          <PageHeader
            title="Cài đặt"
            description="Hồ sơ, tuỳ chọn giao diện & ngôn ngữ, bảo mật và hạng tài khoản."
          />
          <Stack gap="4">
            <Card>
              <CardHeader title="Hồ sơ" />
              <CardBody>
                <DescriptionList>
                  <DescriptionRow term="Tên đăng nhập">
                    an.nguyen
                  </DescriptionRow>
                  <DescriptionRow term="Hạng">
                    <TierBadge
                      tier="PREMIUM"
                      freeLabel="Free"
                      premiumLabel="Premium"
                    />
                  </DescriptionRow>
                  <DescriptionRow term="Vai trò">
                    <Badge tone="neutral">Người dùng</Badge>
                  </DescriptionRow>
                  <DescriptionRow term="Tham gia từ">
                    <Skeleton width="8rem" />
                  </DescriptionRow>
                </DescriptionList>
              </CardBody>
            </Card>
          </Stack>
        </Section>

        {/* TABLE */}
        <Section title="Bảng (Table) — danh sách, hành động & dòng đã xóa">
          <p className={styles.note}>
            Bảng ngữ nghĩa: tiêu đề cột <code>scope="col"</code>, tên có thể đặt{" "}
            <code>scope="row"</code>. Cột số căn phải + chữ số đều
            (tabular-nums). Ô hành động ở cuối dòng. Dòng đã xóa được làm mờ và
            luôn kèm badge chữ — không chỉ dựa vào màu.
          </p>
          <Table caption="Danh sách thành viên (ví dụ)" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Tên thành viên</TableHeaderCell>
                <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số dư
                </TableHeaderCell>
                <TableHeaderCell scope="col">
                  <span className={styles.srOnly}>Hành động</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {/* Owner-representative: renamable, never deletable. */}
              <TableRow>
                <TableHeaderCell scope="row">Minh (bạn)</TableHeaderCell>
                <TableCell>
                  <Badge tone="info" icon={<Star />}>
                    Đại diện chủ sổ
                  </Badge>
                </TableCell>
                <TableCell numeric>
                  <Money amount={0} variant="balance" size="sm" />
                </TableCell>
                <TableCell actions>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Đổi tên Minh (bạn)"
                  >
                    Đổi tên
                  </Button>
                </TableCell>
              </TableRow>
              {/* Normal members: rename + delete. */}
              <TableRow>
                <TableHeaderCell scope="row">An Nguyễn</TableHeaderCell>
                <TableCell>
                  <Badge tone="neutral">Thành viên</Badge>
                </TableCell>
                <TableCell numeric>
                  <Money amount={320_000} variant="balance" size="sm" />
                </TableCell>
                <TableCell actions>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Đổi tên An Nguyễn"
                  >
                    Đổi tên
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Xóa An Nguyễn"
                  >
                    Xóa
                  </Button>
                </TableCell>
              </TableRow>
              <TableRow>
                <TableHeaderCell scope="row">
                  Trần Thị Bích Ngọc
                </TableHeaderCell>
                <TableCell>
                  <Badge tone="neutral">Thành viên</Badge>
                </TableCell>
                <TableCell numeric>
                  <Money amount={-150_000} variant="balance" size="sm" />
                </TableCell>
                <TableCell actions>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Đổi tên Trần Thị Bích Ngọc"
                  >
                    Đổi tên
                  </Button>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Xóa Trần Thị Bích Ngọc"
                  >
                    Xóa
                  </Button>
                </TableCell>
              </TableRow>
              {/* Soft-deleted: muted row + "(đã xóa)" badge, read-only. */}
              <TableRow deleted>
                <TableHeaderCell scope="row">Bảo (cũ)</TableHeaderCell>
                <TableCell>
                  <Badge tone="neutral">Đã xóa</Badge>
                </TableCell>
                <TableCell numeric>
                  <Money amount={0} variant="balance" size="sm" />
                </TableCell>
                <TableCell actions>
                  <span className={styles.note}>—</span>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>

          <p className={styles.note}>Trạng thái rỗng (phòng thủ):</p>
          <Table caption="Bảng rỗng (ví dụ)" captionHidden dense>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Tên</TableHeaderCell>
                <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Số dư
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableEmpty colSpan={3}>
                <EmptyState
                  icon={<Receipt />}
                  title="Chưa có dữ liệu"
                  description="Khi có bản ghi, chúng sẽ hiển thị ở đây."
                />
              </TableEmpty>
            </TableBody>
          </Table>
        </Section>

        {/* RESPONSIVE — breakpoint ladder + opt-in mobile card-stack table */}
        <Section title="Responsive: thang breakpoint + bảng dồn thẻ trên di động">
          <p className={styles.note}>
            Thang breakpoint (mobile-first, min-width):{" "}
            <code>sm 30rem / 480px</code> · <code>md 48rem / 768px</code> ·{" "}
            <code>lg 64rem / 1024px</code>. Bảng dưới đây bật{" "}
            <code>stackOnMobile</code>: dưới <code>sm</code> mỗi dòng dồn thành
            một thẻ nhãn:giá trị (nhãn lấy từ <code>data-label</code> của ô);
            từ <code>sm</code> trở lên là bảng bình thường. Thu hẹp cửa sổ dưới
            480px để xem. Mặc định (không bật) vẫn là cuộn ngang — không dòng nào
            khác bị ảnh hưởng.
          </p>
          <Table
            caption="Phiếu chi tiêu (ví dụ) — dồn thẻ trên di động"
            captionHidden
            stackOnMobile
          >
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Tên phiếu</TableHeaderCell>
                <TableHeaderCell scope="col">Người trả</TableHeaderCell>
                <TableHeaderCell scope="col" numeric>
                  Tổng
                </TableHeaderCell>
                <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
                <TableHeaderCell scope="col">
                  <span className={styles.srOnly}>Hành động</span>
                </TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              <TableRow>
                <TableHeaderCell scope="row">
                  Ăn tối nhà hàng Ngọc Sương
                </TableHeaderCell>
                <TableCell data-label="Người trả">An Nguyễn</TableCell>
                <TableCell data-label="Tổng" numeric>
                  <Money amount={1_250_000} size="sm" />
                </TableCell>
                <TableCell data-label="Trạng thái">
                  <Badge tone="settled" icon={<Check />}>
                    Đã trả
                  </Badge>
                </TableCell>
                <TableCell actions>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Xem Ăn tối nhà hàng Ngọc Sương"
                  >
                    Xem
                  </Button>
                </TableCell>
              </TableRow>
              <TableRow>
                <TableHeaderCell scope="row">Taxi sân bay</TableHeaderCell>
                <TableCell data-label="Người trả">
                  Trần Thị Bích Ngọc
                </TableCell>
                <TableCell data-label="Tổng" numeric>
                  <Money amount={385_000} size="sm" />
                </TableCell>
                <TableCell data-label="Trạng thái">
                  <Badge tone="neutral">Chưa trả</Badge>
                </TableCell>
                <TableCell actions>
                  <Button
                    variant="ghost"
                    size="sm"
                    aria-label="Xem Taxi sân bay"
                  >
                    Xem
                  </Button>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
          <p className={styles.note}>
            Mục tiêu chạm (touch target): trên con trỏ thô (<code>pointer:
            coarse</code>) nút <code>size="sm"</code> và các nút gạt phân đoạn
            (theme/ngôn ngữ) mở rộng vùng chạm tới ≥44px; chuột trên desktop giữ
            kích thước gọn. Bật giả lập cảm ứng trong DevTools để kiểm chứng.
          </p>
        </Section>

        {/* LINK-STYLED-AS-BUTTON (asChild) */}
        <Section title="Liên kết dạng nút (Button asChild)">
          <p className={styles.note}>
            <code>asChild</code> hợp nhất kiểu nút vào phần tử con — một{" "}
            <code>&lt;a&gt;</code> duy nhất, không lồng nút trong liên kết. Ở đây
            dùng thẻ <code>&lt;a&gt;</code> minh họa; trong ứng dụng là{" "}
            <code>&lt;Link&gt;</code> của router.
          </p>
          <div className={styles.row}>
            <Button asChild variant="secondary">
              <a href="#top">Về đầu trang</a>
            </Button>
            <Button asChild variant="ghost" size="sm">
              <a href="#top" aria-label="Mở cài đặt">
                Cài đặt
              </a>
            </Button>
          </div>
        </Section>

        <Section title="Empty / Loading / Dialog">
          <div className={styles.twoCol}>
            <Card>
              <EmptyState
                icon={<Receipt />}
                title="Chưa có phiếu nào"
                description="Tạo phiếu đầu tiên để bắt đầu chia tiền."
                action={
                  <Button variant="primary" size="sm">
                    Tạo phiếu
                  </Button>
                }
              />
            </Card>
            <Card>
              <CardHeader title="Đang tải" />
              <CardBody>
                <Skeleton width="60%" />
                <Skeleton width="90%" />
                <Skeleton width="40%" />
                <div className={styles.row}>
                  <Spinner label="Đang tải" />
                  <span className={styles.note}>Spinner</span>
                </div>
              </CardBody>
            </Card>
          </div>
          <Dialog>
            <DialogTrigger asChild>
              <Button variant="secondary">Mở hộp thoại</Button>
            </DialogTrigger>
            <DialogContent
              title="Xóa phiếu chi tiêu?"
              description="Hành động này xóa phiếu cùng toàn bộ phần gánh và không thể hoàn tác."
            >
              <DialogFooter>
                <DialogTrigger asChild>
                  <Button variant="ghost">Hủy</Button>
                </DialogTrigger>
                <Button variant="danger">Xóa</Button>
              </DialogFooter>
            </DialogContent>
          </Dialog>
        </Section>

        {/* M3 — CATEGORY PICKERS & MARKER */}
        <Section title="Danh mục — bảng màu, bảng biểu tượng & thẻ (M3)">
          <p className={styles.note}>
            Biểu tượng danh mục là emoji, backend lưu thẳng glyph (🍜 🚗 …).{" "}
            <code>IconPicker</code> chọn emoji, <code>ColorPicker</code> chọn hex
            (bảng màu + hex tùy chỉnh), <code>CategoryMarker</code> ghép ô màu +
            emoji + tên. Màu không bao giờ là tín hiệu duy nhất — luôn kèm emoji
            và tên; danh mục mặc định có sao + badge chữ "Mặc định".
          </p>
          <CategoryPickersDemo />
        </Section>

        {/* M4 — EXPENSES & SHARES: pickers + complex surfaces */}
        <M4Showcase />

        {/* M5 — EVENTS: balance table, close confirm, status badge, assign picker */}
        <M5Showcase />

        {/* M6 — STATS & HOME: KPI tiles, category bar chart, range control, home */}
        <M6Showcase />

        {/* M7 — WALLET & QR: QrDialog composite + wallet list (mask/reveal, gate) */}
        <M7Showcase />

        {/* M8 — ADMIN: shared charts (KpiTile/RankedBarChart/TimeSeriesBarChart),
            tabbed console, dashboards, user admin + Pagination, action dialogs */}
        <M8Showcase />

        <Section title="Bố cục xác thực (auth)">
          <div className={styles.authPreview}>
            <AuthLayout
              header={<h2>Đăng nhập</h2>}
              footer={<span>Chưa có tài khoản? Đăng ký</span>}
            >
              <Card>
                <Form onSubmit={(e) => e.preventDefault()}>
                  <FieldStack>
                    <TextField label="Tên đăng nhập" placeholder="an.nguyen" />
                    <TextField label="Mật khẩu" type="password" />
                  </FieldStack>
                  <Button variant="primary" type="submit" fullWidth>
                    Đăng nhập
                  </Button>
                </Form>
              </Card>
            </AuthLayout>
          </div>
        </Section>
      </div>
    </AppShell>
  );
}

function Section({
  title,
  children,
}: {
  title: string;
  children: React.ReactNode;
}) {
  return (
    <section className={styles.section}>
      <h2 className={styles.sectionTitle}>{title}</h2>
      {children}
    </section>
  );
}

function Swatch({
  token,
  label,
  onDark,
}: {
  token: string;
  label: string;
  onDark?: boolean;
}) {
  return (
    <div className={styles.swatch}>
      <span
        className={styles.swatchColor}
        style={{
          background: `var(${token})`,
          color: onDark ? "#fff" : "var(--fs-color-text)",
        }}
      />
      <code className={styles.swatchLabel}>{label}</code>
    </div>
  );
}

/**
 * M3 showcase — the category pickers wired to local state, a live marker
 * preview, and the categories-list treatment (markers, default star + badge,
 * set-default affordance, soft-deleted read-only row). This mirrors the shape
 * the web-implementer will build; the design layer owns only the primitives.
 */
function CategoryPickersDemo() {
  const [color, setColor] = useState("#F97316");
  const [icon, setIcon] = useState<string | null>("🍜");

  return (
    <div className={styles.stack}>
      <div className={styles.twoCol}>
        <Card>
          <CardHeader title="ColorPicker" />
          <CardBody>
            <ColorPicker
              value={color}
              onChange={setColor}
              label="Màu danh mục"
              hexLabel="Mã màu"
              invalidHexMessage="Mã màu phải có dạng #RRGGBB."
              required
            />
          </CardBody>
        </Card>
        <Card>
          <CardHeader title="IconPicker" />
          <CardBody>
            <IconPicker
              value={icon}
              onChange={setIcon}
              label="Biểu tượng (tùy chọn)"
              noIconLabel="Không có biểu tượng"
            />
          </CardBody>
        </Card>
      </div>

      <Card>
        <CardHeader title="Xem trước CategoryMarker" />
        <CardBody>
          <div className={styles.row}>
            <CategoryMarker
              color={color}
              icon={icon}
              name="Danh mục xem trước"
              showLabel
            />
            <CategoryMarker color={color} icon={icon} name="Chỉ biểu tượng" />
            <CategoryMarker color={color} icon={icon} name="Nhỏ" size="sm" />
            <CategoryMarker
              color={color}
              icon={icon}
              name="Danh mục mặc định"
              showLabel
              isDefault
              defaultLabel="mặc định"
            />
          </div>
        </CardBody>
      </Card>

      <Table caption="Danh mục (ví dụ)" captionHidden>
        <TableHead>
          <TableRow>
            <TableHeaderCell scope="col">Danh mục</TableHeaderCell>
            <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
            <TableHeaderCell scope="col">
              <span className={styles.srOnly}>Hành động</span>
            </TableHeaderCell>
          </TableRow>
        </TableHead>
        <TableBody>
          {/* Default category: star marker + "Mặc định" badge; no set-default,
              no delete — only edit. */}
          <TableRow>
            <TableHeaderCell scope="row">
              <CategoryMarker
                color="#F97316"
                icon="🍜"
                name="Ăn uống"
                showLabel
                isDefault
                defaultLabel="mặc định"
              />
            </TableHeaderCell>
            <TableCell>
              <Badge tone="warning" icon={<Star />}>
                Mặc định
              </Badge>
            </TableCell>
            <TableCell actions>
              <Button variant="ghost" size="sm" aria-label="Sửa Ăn uống">
                Sửa
              </Button>
            </TableCell>
          </TableRow>
          {/* Normal categories: edit + set-default + delete. */}
          <TableRow>
            <TableHeaderCell scope="row">
              <CategoryMarker
                color="#3B82F6"
                icon="🚗"
                name="Đi lại"
                showLabel
              />
            </TableHeaderCell>
            <TableCell>
              <Badge tone="neutral">Đang dùng</Badge>
            </TableCell>
            <TableCell actions>
              <Button variant="ghost" size="sm" aria-label="Sửa Đi lại">
                Sửa
              </Button>
              <Button
                variant="ghost"
                size="sm"
                aria-label="Đặt Đi lại làm mặc định"
              >
                <span className={styles.setDefaultBtn}>
                  <span className={styles.setDefaultIcon} aria-hidden="true">
                    <StarOutline />
                  </span>
                  Đặt mặc định
                </span>
              </Button>
              <Button variant="ghost" size="sm" aria-label="Xóa Đi lại">
                Xóa
              </Button>
            </TableCell>
          </TableRow>
          <TableRow>
            <TableHeaderCell scope="row">
              <CategoryMarker
                color="#8B5CF6"
                name="Khách sạn (không icon)"
                showLabel
              />
            </TableHeaderCell>
            <TableCell>
              <Badge tone="neutral">Đang dùng</Badge>
            </TableCell>
            <TableCell actions>
              <Button variant="ghost" size="sm" aria-label="Sửa Khách sạn">
                Sửa
              </Button>
              <Button
                variant="ghost"
                size="sm"
                aria-label="Đặt Khách sạn làm mặc định"
              >
                <span className={styles.setDefaultBtn}>
                  <span className={styles.setDefaultIcon} aria-hidden="true">
                    <StarOutline />
                  </span>
                  Đặt mặc định
                </span>
              </Button>
              <Button variant="ghost" size="sm" aria-label="Xóa Khách sạn">
                Xóa
              </Button>
            </TableCell>
          </TableRow>
          {/* Soft-deleted: muted, read-only, "Đã xóa" badge. */}
          <TableRow deleted>
            <TableHeaderCell scope="row">
              <CategoryMarker
                color="#EC4899"
                icon="🛍️"
                name="Mua sắm (cũ)"
                showLabel
              />
            </TableHeaderCell>
            <TableCell>
              <Badge tone="neutral">Đã xóa</Badge>
            </TableCell>
            <TableCell actions>
              <span className={styles.note}>—</span>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>
  );
}

/* Tiny inline glyphs for the showcase (feature icons live in feature code). */
const Dot = () => (
  <svg
    viewBox="0 0 8 8"
    width="8"
    height="8"
    fill="currentColor"
    aria-hidden="true"
  >
    <circle cx="4" cy="4" r="4" />
  </svg>
);
const Lock = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M6 8V6a4 4 0 118 0v2h1v9H5V8h1zm2 0h4V6a2 2 0 10-4 0v2z" />
  </svg>
);
const Check = () => (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="2.5"
    aria-hidden="true"
  >
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const Star = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);
const StarOutline = () => (
  <svg
    viewBox="0 0 20 20"
    fill="none"
    stroke="currentColor"
    strokeWidth="1.5"
    aria-hidden="true"
  >
    <path
      d="M10 2.6l2.15 4.5 4.95.55-3.7 3.35 1.05 4.9L10 13.9 5.5 16.3l1.05-4.9-3.7-3.35 4.95-.55z"
      strokeLinejoin="round"
    />
  </svg>
);
const Receipt = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path d="M6 2h12v20l-3-2-3 2-3-2-3 2V2zm3 5h6a1 1 0 010 2H9a1 1 0 010-2zm0 4h6a1 1 0 010 2H9a1 1 0 010-2z" />
  </svg>
);
