import type { ReactNode } from "react";
import { useState } from "react";
import {
  Alert,
  Badge,
  Button,
  Card,
  Dialog,
  DialogClose,
  DialogContent,
  DialogFooter,
  DialogTrigger,
  EmptyState,
  ErrorState,
  Money,
  PageHeader,
  Select,
  Skeleton,
  Table,
  TableBody,
  TableCell,
  TableEmpty,
  TableHead,
  TableHeaderCell,
  TableRow,
  TierBadge,
  UpgradePrompt,
} from "../components/ui";
import styles from "./M7Showcase.module.css";

/* =============================================================================
 * M7 — Wallet (bank accounts) & QR: design showcase
 *
 * Two net-new surfaces the web-implementer rebuilds under src/features/wallet/
 * (feature-local, like the M6 charts — NOT extracted to components/ui yet). The
 * design layer owns the visual/interaction contract; the implementer owns data,
 * hooks, routing, i18n, and the blob object-URL lifecycle.
 *
 *   1. QrDialog — THE genuinely new composite. A modal that shows a VietQR image
 *      (a PNG blob → object URL the implementer creates/revokes) paired with a
 *      human-readable account block (the ACCESSIBLE channel + copy source), an
 *      optional destination Select (OQ2a — shown only with ≥2 accounts), a
 *      Download action, a Copy-details action, and a full state machine:
 *        loading · ready · premium-gate (Free / 13003) · no-account (12001) ·
 *        no-debt (12003, event) · not-closed (12002, event, defensive) · error.
 *      Works for the per-expense QR (square) and the per-event composite QR
 *      (taller portrait). The QR frame is deliberately light in both themes — a
 *      QR needs dark modules on a light ground to scan.
 *
 *   2. Wallet list — the bank-account Table: bank + BIN, MASKED account number
 *      (•••• 1234) with a per-row click-to-reveal toggle (OQ5a), holder, a
 *      default marker, and the Free read-only / Premium-managed composition
 *      (OQ1a hybrid gate — proactive by session tier).
 *
 * Everything here is DESIGN spec: local state stands in for the API so layout,
 * markup, tokens, and a11y are reviewable in light AND dark. Copy is Vietnamese-
 * authoritative (the implementer routes it through the new `wallet` namespace).
 * ========================================================================== */

// ── Local glyphs (decorative — copy carries the meaning) ─────────────────────
const QrIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" width="1em" height="1em">
    <rect x="3" y="3" width="5" height="5" rx="1" />
    <rect x="12" y="3" width="5" height="5" rx="1" />
    <rect x="3" y="12" width="5" height="5" rx="1" />
    <path d="M12 12h2v2M16 12v5M12 16h2" strokeLinecap="round" />
  </svg>
);
const DownloadIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <path d="M10 3v9m0 0l-3.2-3.2M10 12l3.2-3.2M4 15.5h12" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const CopyIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.7" aria-hidden="true" width="1em" height="1em">
    <rect x="7" y="7" width="9" height="9" rx="1.5" />
    <path d="M4 13V5.5A1.5 1.5 0 015.5 4H13" strokeLinecap="round" />
  </svg>
);
const CheckIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2.2" aria-hidden="true" width="1em" height="1em">
    <path d="M4 10l4 4 8-9" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const EyeIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" className={styles.revealIcon}>
    <path d="M1.8 10S4.9 4.5 10 4.5 18.2 10 18.2 10 15.1 15.5 10 15.5 1.8 10 1.8 10z" strokeLinejoin="round" />
    <circle cx="10" cy="10" r="2.4" />
  </svg>
);
const EyeOffIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.6" aria-hidden="true" className={styles.revealIcon}>
    <path d="M2.5 3.5l14 13M8 5c.65-.15 1.32-.23 2-.23 5.1 0 8.2 5.23 8.2 5.23a13 13 0 01-2.2 2.66M5.3 6.3A13 13 0 001.8 10s3.1 5.5 8.2 5.5c.9 0 1.75-.17 2.55-.45" strokeLinecap="round" strokeLinejoin="round" />
  </svg>
);
const StarIcon = () => (
  <svg viewBox="0 0 20 20" fill="currentColor" aria-hidden="true">
    <path d="M10 2l2.4 5 5.6.6-4.2 3.8 1.2 5.6L10 14.8 5 17l1.2-5.6L2 7.6 7.6 7z" />
  </svg>
);
const StarOutlineIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="1.5" aria-hidden="true">
    <path d="M10 2.6l2.15 4.5 4.95.55-3.7 3.35 1.05 4.9L10 13.9 5.5 16.3l1.05-4.9-3.7-3.35 4.95-.55z" strokeLinejoin="round" />
  </svg>
);
const PlusIcon = () => (
  <svg viewBox="0 0 20 20" fill="none" stroke="currentColor" strokeWidth="2" aria-hidden="true" width="1em" height="1em">
    <path d="M10 4v12M4 10h12" strokeLinecap="round" />
  </svg>
);
const WalletIcon = () => (
  <svg viewBox="0 0 24 24" fill="currentColor" aria-hidden="true" width="28" height="28">
    <path d="M4 6a2 2 0 012-2h11a1 1 0 010 2H6v12h13v-3h-4a2 2 0 01-2-2v-2a2 2 0 012-2h4a2 2 0 012 2v7a2 2 0 01-2 2H6a2 2 0 01-2-2V6zm12 6v2h4v-2h-4z" />
  </svg>
);

// ── Demo domain data (a BankAccountResponse mirror; default-first order) ──────
type Account = {
  uuid: string;
  bankBin: string;
  bankName: string;
  accountNumber: string;
  accountHolderName: string;
  isDefault: boolean;
};

const ACCOUNTS: Account[] = [
  { uuid: "a-vcb", bankBin: "970436", bankName: "Vietcombank", accountNumber: "0071001234567", accountHolderName: "NGUYEN VAN MINH", isDefault: true },
  { uuid: "a-tcb", bankBin: "970407", bankName: "Techcombank", accountNumber: "19024681012345", accountHolderName: "NGUYEN VAN MINH", isDefault: false },
  { uuid: "a-mb", bankBin: "970422", bankName: "MB Bank", accountNumber: "0801234567", accountHolderName: "NGUYEN VAN MINH", isDefault: false },
];

// Presentation-only helpers (no money/number math — pure string display).
const maskAccount = (n: string) => `•••• ${n.slice(-4)}`;
const groupAccount = (n: string) => n.replace(/(.{4})(?=.)/g, "$1 ");

/* A deterministic faux-QR as an SVG data URI — a design stand-in for the real
   PNG blob so the "ready" state is reviewable. The real dialog sets
   <img src={objectUrl}>; this only fills that same <img>. */
function fauxQr(portrait: boolean, seed: number): string {
  const cols = 21;
  const rows = portrait ? 27 : 21;
  let s = seed >>> 0;
  const rand = () => {
    s = (s * 1664525 + 1013904223) >>> 0;
    return s / 0xffffffff;
  };
  const isFinder = (x: number, y: number) => {
    const inBox = (ox: number, oy: number) =>
      x >= ox && x < ox + 7 && y >= oy && y < oy + 7;
    return inBox(0, 0) || inBox(cols - 7, 0) || inBox(0, rows - 7);
  };
  let rects = "";
  for (let y = 0; y < rows; y++) {
    for (let x = 0; x < cols; x++) {
      let on: boolean;
      if (isFinder(x, y)) {
        const lx = x >= cols - 7 ? x - (cols - 7) : x;
        const ly = y >= rows - 7 ? y - (rows - 7) : y;
        const ring = lx === 0 || lx === 6 || ly === 0 || ly === 6;
        const core = lx >= 2 && lx <= 4 && ly >= 2 && ly <= 4;
        on = ring || core;
      } else {
        on = rand() > 0.55;
      }
      if (on) rects += `<rect x="${x}" y="${y}" width="1" height="1"/>`;
    }
  }
  const svg = `<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 ${cols} ${rows}" shape-rendering="crispEdges"><rect width="${cols}" height="${rows}" fill="#fff"/><g fill="#0d1211">${rects}</g></svg>`;
  return `data:image/svg+xml,${encodeURIComponent(svg)}`;
}
const EXPENSE_QR = fauxQr(false, 20260717);
const EVENT_QR = fauxQr(true, 77000913);

// ── QR state contract (discriminated union the implementer maps query state to)
type QrState =
  | { status: "loading" }
  | { status: "ready"; imageUrl: string }
  | { status: "premiumGate" } /* Free proactive OR reactive 403 13003 */
  | { status: "noAccount" } /* 400 12001 */
  | { status: "noDebt" } /* 400 12003 (event) */
  | { status: "notClosed" } /* 400 12002 (event, defensive) */
  | { status: "error" };

export function M7Showcase() {
  return (
    <>
      <QrDialogSection />
      <WalletListSection />
    </>
  );
}

/* =============================================================================
 * 1. QrDialog
 * ========================================================================== */
function QrDialogSection() {
  return (
    <Section
      title="Hộp thoại mã QR (QrDialog)"
      note="Thành phần mới duy nhất của M7. Một modal hiển thị ẢNH mã VietQR (PNG blob → object URL do implementer tạo và thu hồi) đi kèm KHỐI THÔNG TIN TÀI KHOẢN dạng chữ — đây là kênh trợ năng bắt cặp với ảnh QR và cũng là nguồn cho hành động 'Sao chép thông tin'. Có bộ chọn tài khoản nhận (OQ2a — chỉ hiện khi ví có ≥2 tài khoản), nút Tải ảnh, nút Sao chép, và đủ các trạng thái. Khung ảnh QR cố định tỉ lệ nên khung xương lúc tải, ảnh khi xong, và mọi placeholder chiếm CÙNG một khoảng — không giật bố cục. Dùng chung cho QR theo phiếu (vuông) và QR tổng hợp theo đợt (cao hơn)."
    >
      {/* Live modal — proves the real Radix Dialog composition + footer. */}
      <p className={styles.subhead}>Mở modal thật (Radix Dialog)</p>
      <div className={styles.qrFooter} style={{ justifyContent: "flex-start" }}>
        <LiveQrDialog
          kind="expense"
          title="Mã QR chuyển khoản — Ăn tối quán nướng"
          state={{ status: "ready", imageUrl: EXPENSE_QR }}
          amount={1_250_000}
          triggerLabel="Xem mã QR phiếu"
          triggerVariant="primary"
        />
        <LiveQrDialog
          kind="event"
          title="Mã QR quyết toán — Chuyến Đà Lạt 07/2026"
          state={{ status: "ready", imageUrl: EVENT_QR }}
          triggerLabel="Xem mã QR đợt"
          triggerVariant="secondary"
        />
        <LiveQrDialog
          kind="expense"
          title="Mã QR chuyển khoản"
          state={{ status: "premiumGate" }}
          triggerLabel="Xem mã QR (tài khoản Free)"
          triggerVariant="secondary"
        />
      </div>

      {/* Inline previews — every state at a glance, in light AND dark. */}
      <p className={styles.subhead}>Các trạng thái (xem trước nội bộ hộp thoại)</p>
      <div className={styles.previewGrid}>
        <QrPreview caption="Sẵn sàng · phiếu (vuông)" title="Mã QR chuyển khoản — Ăn tối quán nướng">
          <QrDialogBody kind="expense" state={{ status: "ready", imageUrl: EXPENSE_QR }} account={ACCOUNTS[0]} amount={1_250_000} destinations={ACCOUNTS} />
          <QrFooter state={{ status: "ready", imageUrl: EXPENSE_QR }} account={ACCOUNTS[0]} />
        </QrPreview>

        <QrPreview caption="Sẵn sàng · đợt (cao, tổng hợp)" title="Mã QR quyết toán — Chuyến Đà Lạt 07/2026">
          <QrDialogBody kind="event" state={{ status: "ready", imageUrl: EVENT_QR }} account={ACCOUNTS[0]} destinations={ACCOUNTS} />
          <QrFooter state={{ status: "ready", imageUrl: EVENT_QR }} account={ACCOUNTS[0]} />
        </QrPreview>

        <QrPreview caption="Đang tải (khung xương đúng khung ảnh)" title="Mã QR chuyển khoản">
          <QrDialogBody kind="expense" state={{ status: "loading" }} />
          <QrFooter state={{ status: "loading" }} />
        </QrPreview>

        <QrPreview caption="Premium (Free / 13003) — thân là UpgradePrompt info" title="Mã QR chuyển khoản">
          <QrDialogBody kind="expense" state={{ status: "premiumGate" }} />
          <QrFooter state={{ status: "premiumGate" }} />
        </QrPreview>

        <QrPreview caption="Chưa có tài khoản nhận (12001)" title="Mã QR chuyển khoản">
          <QrDialogBody kind="expense" state={{ status: "noAccount" }} />
          <QrFooter state={{ status: "noAccount" }} />
        </QrPreview>

        <QrPreview caption="Đợt chưa ai nợ (12003)" title="Mã QR quyết toán">
          <QrDialogBody kind="event" state={{ status: "noDebt" }} />
          <QrFooter state={{ status: "noDebt" }} />
        </QrPreview>

        <QrPreview caption="Đợt chưa chốt (12002 · phòng thủ)" title="Mã QR quyết toán">
          <QrDialogBody kind="event" state={{ status: "notClosed" }} />
          <QrFooter state={{ status: "notClosed" }} />
        </QrPreview>

        <QrPreview caption="Lỗi tải ảnh (có thử lại)" title="Mã QR chuyển khoản">
          <QrDialogBody kind="expense" state={{ status: "error" }} />
          <QrFooter state={{ status: "error" }} />
        </QrPreview>
      </div>
    </Section>
  );
}

/** A faux dialog panel wrapping the QR body so every state is reviewable inline
 *  without opening the modal one at a time. */
function QrPreview({
  caption,
  title,
  children,
}: {
  caption: string;
  title: string;
  children: ReactNode;
}) {
  return (
    <div>
      <p className={styles.previewCaption}>{caption}</p>
      <div className={styles.dialogPreview}>
        <p className={styles.previewTitle}>{title}</p>
        {children}
      </div>
    </div>
  );
}

/**
 * QrDialogBody — the state-machine body shared by the live dialog and the inline
 * previews. Presentational: the implementer passes `state` (mapped from the
 * TanStack Query status + the error code) and the account/destination props.
 */
function QrDialogBody({
  kind,
  state,
  account,
  amount,
  destinations,
}: {
  kind: "expense" | "event";
  state: QrState;
  account?: Account;
  amount?: number;
  destinations?: Account[];
}) {
  // Premium gate (OQ1a): the whole body IS an informational UpgradePrompt —
  // Premium is a manual admin grant, so there is NO navigating action.
  if (state.status === "premiumGate") {
    return (
      <div className={styles.qrBody}>
        <UpgradePrompt
          variant="info"
          title="Tính năng Premium"
          description="Tạo mã QR chuyển khoản chỉ dành cho tài khoản Premium. Premium được cấp thủ công bởi quản trị viên — hãy liên hệ người vận hành để mở khóa."
        />
      </div>
    );
  }

  // No receiving bank account (12001) — route the user to the wallet.
  if (state.status === "noAccount") {
    return (
      <div className={styles.qrBody}>
        <EmptyState
          icon={<WalletIcon />}
          title="Chưa có tài khoản nhận tiền"
          description="Thêm một tài khoản ngân hàng vào ví để tạo mã QR chuyển khoản."
          action={
            <Button asChild variant="primary" size="sm" iconStart={<PlusIcon />}>
              <a href="#wallet">Thêm tài khoản</a>
            </Button>
          }
        />
      </div>
    );
  }

  // Event: nobody owes (12003) — informational, not an error.
  if (state.status === "noDebt") {
    return (
      <div className={styles.qrBody}>
        <Alert tone="info" title="Không còn ai nợ trong đợt này">
          Mọi phần gánh đã được đánh dấu đã trả, nên không cần mã QR quyết toán.
        </Alert>
      </div>
    );
  }

  // Event: not closed (12002) — defensive; the button is normally hidden until
  // the event is closed, so this only appears on a stale click.
  if (state.status === "notClosed") {
    return (
      <div className={styles.qrBody}>
        <Alert tone="warning" title="Đợt chưa được chốt">
          Chỉ có thể tạo mã QR quyết toán sau khi chốt đợt. Hãy chốt đợt rồi thử lại.
        </Alert>
      </div>
    );
  }

  // Generic load failure — offer a retry (the implementer wires refetch).
  if (state.status === "error") {
    return (
      <div className={styles.qrBody}>
        <ErrorState
          title="Không tạo được mã QR"
          description="Đã xảy ra lỗi khi tạo mã QR. Vui lòng thử lại."
          action={
            <Button variant="secondary" size="sm">
              Thử lại
            </Button>
          }
        />
      </div>
    );
  }

  // loading | ready — same footprint (destination picker + fixed-aspect well).
  const showPicker = (destinations?.length ?? 0) >= 2;
  return (
    <div className={styles.qrBody}>
      {showPicker ? (
        <div className={styles.destination}>
          <Select
            label="Tài khoản nhận tiền"
            value={destinations![0].uuid}
            onValueChange={() => {}}
            options={destinations!.map((a) => ({
              value: a.uuid,
              label: `${a.bankName} · ${maskAccount(a.accountNumber)}`,
            }))}
          />
        </div>
      ) : null}

      <div className={styles.qrWell}>
        <div className={`${styles.qrFrame} ${styles[kind]}`}>
          {state.status === "loading" ? (
            <Skeleton className={styles.qrSkeleton} width="auto" height="auto" />
          ) : (
            <img
              className={styles.qrImage}
              src={state.imageUrl}
              alt={
                kind === "expense"
                  ? "Mã QR VietQR để chuyển khoản cho phiếu chi tiêu này. Thông tin tài khoản ở ngay bên dưới."
                  : "Mã QR VietQR tổng hợp để quyết toán công nợ của đợt. Thông tin tài khoản ở ngay bên dưới."
              }
            />
          )}
        </div>
      </div>

      {state.status === "ready" && account ? (
        <dl className={styles.accountCard}>
          <dt className={styles.accountTerm}>Ngân hàng</dt>
          <dd className={styles.accountValue}>{account.bankName}</dd>
          <dt className={styles.accountTerm}>Số tài khoản</dt>
          <dd className={`${styles.accountValue} ${styles.accountNumber}`}>
            {groupAccount(account.accountNumber)}
          </dd>
          <dt className={styles.accountTerm}>Chủ tài khoản</dt>
          <dd className={styles.accountValue}>{account.accountHolderName}</dd>
          {kind === "expense" && amount != null ? (
            <>
              <dt className={styles.accountTerm}>Số tiền</dt>
              <dd className={styles.accountValue}>
                <Money amount={amount} size="sm" />
              </dd>
            </>
          ) : null}
        </dl>
      ) : null}
    </div>
  );
}

/** The QR footer actions. Download + Copy-details only exist in the ready state;
 *  every other state shows nothing here (the live dialog still renders its Close).
 *  In the live modal these buttons sit in DialogFooter beside a Close button. */
function QrFooter({ state, account }: { state: QrState; account?: Account }) {
  if (state.status !== "ready") return null;
  return (
    <div className={styles.qrFooter}>
      <CopyDetailsButton account={account} />
      <Button variant="primary" size="sm" iconStart={<DownloadIcon />}>
        Tải ảnh QR
      </Button>
    </div>
  );
}

/** Copy holder name + account number (OQ4a — NOT the raw VietQR TLV string). */
function CopyDetailsButton({ account }: { account?: Account }) {
  const [copied, setCopied] = useState(false);
  const onCopy = () => {
    if (account && navigator.clipboard) {
      void navigator.clipboard.writeText(
        `${account.accountHolderName}\n${account.accountNumber}\n${account.bankName}`,
      );
    }
    setCopied(true);
    setTimeout(() => setCopied(false), 1600);
  };
  return (
    <Button
      variant="secondary"
      size="sm"
      onClick={onCopy}
      iconStart={copied ? <CheckIcon /> : <CopyIcon />}
    >
      {copied ? "Đã sao chép" : "Sao chép thông tin"}
    </Button>
  );
}

/** The real openable dialog — wraps QrDialogBody in DialogContent + DialogFooter
 *  (Download/Copy for ready; a Close always). This is the shape the implementer
 *  ships; the object-URL lifecycle is scoped to the dialog's mount. */
function LiveQrDialog({
  kind,
  title,
  state,
  amount,
  triggerLabel,
  triggerVariant,
}: {
  kind: "expense" | "event";
  title: string;
  state: QrState;
  amount?: number;
  triggerLabel: string;
  triggerVariant: "primary" | "secondary";
}) {
  return (
    <Dialog>
      <DialogTrigger asChild>
        <Button variant={triggerVariant} size="sm" iconStart={<QrIcon />}>
          {triggerLabel}
        </Button>
      </DialogTrigger>
      <DialogContent title={title} size="sm" closeLabel="Đóng">
        <QrDialogBody
          kind={kind}
          state={state}
          account={ACCOUNTS[0]}
          amount={amount}
          destinations={ACCOUNTS}
        />
        <DialogFooter>
          <DialogClose asChild>
            <Button variant="ghost">Đóng</Button>
          </DialogClose>
          {state.status === "ready" ? (
            <>
              <CopyDetailsButton account={ACCOUNTS[0]} />
              <Button variant="primary" iconStart={<DownloadIcon />}>
                Tải ảnh QR
              </Button>
            </>
          ) : null}
        </DialogFooter>
      </DialogContent>
    </Dialog>
  );
}

/* =============================================================================
 * 2. Wallet list — masked number + reveal, default marker, Free/Premium split
 * ========================================================================== */
function WalletListSection() {
  return (
    <Section
      title="Danh sách ví (bank accounts) — OQ5a mask/reveal, cổng Premium OQ1a"
      note="Ví liệt kê tài khoản nhận tiền của người dùng (mặc định trước, đúng thứ tự backend): ngân hàng + BIN, SỐ TÀI KHOẢN ĐƯỢC CHE (•••• 1234) kèm nút hiện/ẩn từng dòng, chủ tài khoản, và một dấu 'Mặc định' rõ ràng. ĐỌC ví là miễn phí; MỌI thao tác quản lý (thêm/sửa/đặt mặc định/xóa) là Premium. Cổng lai (OQ1a): người dùng Free thấy bảng CHỈ ĐỌC + banner UpgradePrompt info (không có nút thao tác); người dùng Premium thấy đủ nút. Số tài khoản dùng phông monospace + tabular để dễ đọc như một mã, và nút hiện/ẩn không nhảy vị trí khi đổi che ↔ hiện."
    >
      {/* Premium — full management. */}
      <p className={styles.subhead}>Premium — quản lý đầy đủ</p>
      <PageHeader
        title="Ví của tôi"
        description="Quản lý các tài khoản ngân hàng nhận tiền dùng để tạo mã QR chuyển khoản."
        actions={
          <div style={{ display: "flex", alignItems: "center", gap: "var(--fs-space-3)" }}>
            <TierBadge tier="PREMIUM" freeLabel="Free" premiumLabel="Premium" />
            <Button variant="primary" size="sm" iconStart={<PlusIcon />}>
              Thêm tài khoản
            </Button>
          </div>
        }
      />
      <WalletTable mode="premium" accounts={ACCOUNTS} />

      {/* Free — read-only with the informational gate banner. */}
      <p className={styles.subhead}>Free — chỉ đọc (đã có tài khoản từ trước)</p>
      <div className={styles.walletStack}>
        <UpgradePrompt
          variant="info"
          title="Quản lý phương thức nhận tiền là tính năng Premium"
          description="Bạn vẫn xem được các tài khoản đã lưu, nhưng thêm, sửa, đặt mặc định hay xóa cần tài khoản Premium. Premium được cấp thủ công bởi quản trị viên."
        />
        <WalletTable mode="free" accounts={ACCOUNTS} />
      </div>

      {/* Empty states — Free (Premium explainer) vs Premium (add first). */}
      <p className={styles.subhead}>Trạng thái rỗng</p>
      <div className={styles.twoUp}>
        <div>
          <p className={styles.previewCaption}>Free chưa từng Premium — banner giải thích</p>
          <Card>
            <EmptyState
              icon={<WalletIcon />}
              title="Ví trống"
              description="Ví (tài khoản nhận tiền & mã QR) là tính năng Premium. Premium được cấp thủ công bởi quản trị viên — hãy liên hệ người vận hành để mở khóa."
            />
          </Card>
        </div>
        <div>
          <p className={styles.previewCaption}>Premium — mời thêm tài khoản đầu tiên</p>
          <Card>
            <EmptyState
              icon={<WalletIcon />}
              title="Chưa có tài khoản nào"
              description="Thêm tài khoản ngân hàng đầu tiên để bắt đầu tạo mã QR chuyển khoản."
              action={
                <Button variant="primary" size="sm" iconStart={<PlusIcon />}>
                  Thêm tài khoản
                </Button>
              }
            />
          </Card>
        </div>
      </div>

      {/* Loading + error (mirror the M6 list-state convention). */}
      <p className={styles.subhead}>Đang tải & lỗi</p>
      <div className={styles.twoUp}>
        <Card padded={false}>
          <Table caption="Đang tải ví" captionHidden>
            <TableHead>
              <TableRow>
                <TableHeaderCell scope="col">Ngân hàng</TableHeaderCell>
                <TableHeaderCell scope="col">Số tài khoản</TableHeaderCell>
                <TableHeaderCell scope="col">Chủ tài khoản</TableHeaderCell>
              </TableRow>
            </TableHead>
            <TableBody>
              {[0, 1, 2].map((i) => (
                <TableRow key={i}>
                  <TableCell><Skeleton width="7rem" /></TableCell>
                  <TableCell><Skeleton width="6rem" /></TableCell>
                  <TableCell><Skeleton width="9rem" /></TableCell>
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </Card>
        <Card>
          <ErrorState
            title="Không tải được ví"
            description="Đã xảy ra lỗi khi tải danh sách tài khoản."
            action={<Button variant="secondary" size="sm">Thử lại</Button>}
          />
        </Card>
      </div>
    </Section>
  );
}

/** The bank-account table. `mode` drives the Free/Premium split: `premium` shows
 *  the action column (set-default / edit / delete); `free` is read-only (no
 *  action column). Reveal is Free-safe (reading the user's own number) in both. */
function WalletTable({
  mode,
  accounts,
}: {
  mode: "premium" | "free";
  accounts: Account[];
}) {
  const [revealed, setRevealed] = useState<Record<string, boolean>>({});
  const isPremium = mode === "premium";
  const colCount = isPremium ? 5 : 4;

  return (
    <Card padded={false}>
      <Table caption="Tài khoản ngân hàng nhận tiền">
        <TableHead>
          <TableRow>
            <TableHeaderCell scope="col">Ngân hàng</TableHeaderCell>
            <TableHeaderCell scope="col">Số tài khoản</TableHeaderCell>
            <TableHeaderCell scope="col">Chủ tài khoản</TableHeaderCell>
            <TableHeaderCell scope="col">Trạng thái</TableHeaderCell>
            {isPremium ? (
              <TableHeaderCell scope="col">
                <span className={styles.srOnly}>Hành động</span>
              </TableHeaderCell>
            ) : null}
          </TableRow>
        </TableHead>
        <TableBody>
          {accounts.length === 0 ? (
            <TableEmpty colSpan={colCount}>
              <EmptyState title="Chưa có tài khoản nào" />
            </TableEmpty>
          ) : (
            accounts.map((a) => {
              const show = revealed[a.uuid] ?? false;
              return (
                <TableRow key={a.uuid}>
                  <TableHeaderCell scope="row">
                    <span className={styles.bankCell}>
                      <span className={styles.bankName}>{a.bankName}</span>
                      <span className={styles.bankBin}>BIN {a.bankBin}</span>
                    </span>
                  </TableHeaderCell>
                  <TableCell>
                    <span className={styles.acctCell}>
                      <span className={styles.acctNumber}>
                        {show ? groupAccount(a.accountNumber) : maskAccount(a.accountNumber)}
                      </span>
                      <button
                        type="button"
                        className={styles.revealBtn}
                        aria-pressed={show}
                        aria-label={
                          show
                            ? `Ẩn số tài khoản ${a.bankName}`
                            : `Hiện số tài khoản ${a.bankName}`
                        }
                        onClick={() =>
                          setRevealed((r) => ({ ...r, [a.uuid]: !show }))
                        }
                      >
                        {show ? <EyeOffIcon /> : <EyeIcon />}
                      </button>
                    </span>
                  </TableCell>
                  <TableCell className={styles.holderCell}>
                    {a.accountHolderName}
                  </TableCell>
                  <TableCell>
                    {a.isDefault ? (
                      <Badge tone="settled" icon={<StarIcon />}>
                        Mặc định
                      </Badge>
                    ) : (
                      <span className={styles.note}>—</span>
                    )}
                  </TableCell>
                  {isPremium ? (
                    <TableCell actions>
                      {a.isDefault ? null : (
                        <Button
                          variant="ghost"
                          size="sm"
                          aria-label={`Đặt ${a.bankName} làm mặc định`}
                        >
                          <span className={styles.setDefaultBtn}>
                            <span className={styles.setDefaultIcon} aria-hidden="true">
                              <StarOutlineIcon />
                            </span>
                            Đặt mặc định
                          </span>
                        </Button>
                      )}
                      <Button variant="ghost" size="sm" aria-label={`Sửa ${a.bankName}`}>
                        Sửa
                      </Button>
                      <Button variant="ghost" size="sm" aria-label={`Xóa ${a.bankName}`}>
                        Xóa
                      </Button>
                    </TableCell>
                  ) : null}
                </TableRow>
              );
            })
          )}
        </TableBody>
      </Table>
    </Card>
  );
}

/* ── Section shell (local, mirrors M4/M5/M6) ─────────────────────────────────*/
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
