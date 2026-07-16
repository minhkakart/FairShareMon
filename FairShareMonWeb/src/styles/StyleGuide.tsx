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
  LanguageToggle,
  LimitNotice,
  Money,
  NavItem,
  PageHeader,
  Skeleton,
  Spinner,
  Stack,
  TextField,
  ThemeToggle,
  TierBadge,
  UpgradePrompt,
} from "../components/ui";
import type { Locale, ThemePreference } from "../components/ui";
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

  const actions = (
    <>
      <LanguageToggle
        value={locale}
        onChange={setLocale}
        groupLabel="Ngôn ngữ"
        labels={{ "vi-VN": "Tiếng Việt", "en-US": "English" }}
      />
      <ThemeToggle
        value={theme}
        onChange={setTheme}
        groupLabel="Giao diện"
        labels={{ light: "Sáng", system: "Theo hệ thống", dark: "Tối" }}
      />
    </>
  );

  return (
    <AppShell
      brand={brand}
      actions={actions}
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
const Receipt = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true">
    <path d="M6 2h12v20l-3-2-3 2-3-2-3 2V2zm3 5h6a1 1 0 010 2H9a1 1 0 010-2zm0 4h6a1 1 0 010 2H9a1 1 0 010-2z" />
  </svg>
);
