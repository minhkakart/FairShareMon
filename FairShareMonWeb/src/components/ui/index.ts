/**
 * FairShareMon design-system primitives.
 * Feature code imports from "@/components/ui" — never fork a parallel style
 * system. All primitives are presentational + theme-aware; behavior that needs
 * app state (theme value, locale, toast queue, router links) is exposed as a
 * prop/attribute contract for the web-implementer to wire.
 */
export { Button } from "./Button/Button";
export type { ButtonProps, ButtonVariant, ButtonSize } from "./Button/Button";

export { Spinner } from "./Spinner/Spinner";
export type { SpinnerProps } from "./Spinner/Spinner";

export { TextField } from "./TextField/TextField";
export type { TextFieldProps } from "./TextField/TextField";

export { Form, FieldStack, FormError, FormActions } from "./Form/Form";

export { Card, CardHeader, CardBody } from "./Card/Card";
export type { CardProps } from "./Card/Card";

export {
  Table,
  TableHead,
  TableBody,
  TableFoot,
  TableRow,
  TableHeaderCell,
  TableCell,
  TableEmpty,
} from "./Table/Table";
export type {
  TableProps,
  TableRowProps,
  TableHeaderCellProps,
  TableCellProps,
  TableEmptyProps,
} from "./Table/Table";

export { Badge } from "./Badge/Badge";
export type { BadgeProps, BadgeTone } from "./Badge/Badge";

export { CategoryMarker } from "./CategoryMarker/CategoryMarker";
export type {
  CategoryMarkerProps,
  CategoryMarkerSize,
} from "./CategoryMarker/CategoryMarker";

export { ColorPicker, CURATED_COLORS } from "./ColorPicker/ColorPicker";
export type { ColorPickerProps } from "./ColorPicker/ColorPicker";

export { IconPicker, CURATED_ICONS } from "./IconPicker/IconPicker";
export type { IconPickerProps } from "./IconPicker/IconPicker";

export { Money } from "./Money/Money";
export type { MoneyProps } from "./Money/Money";

export { Select } from "./Select/Select";
export type { SelectProps, SelectOption } from "./Select/Select";

export { Combobox, normalizeForSearch } from "./Combobox/Combobox";
export type { ComboboxProps, ComboboxOption } from "./Combobox/Combobox";

export { TagMultiSelect } from "./TagMultiSelect/TagMultiSelect";
export type {
  TagMultiSelectProps,
  TagOption,
} from "./TagMultiSelect/TagMultiSelect";

export { MoneyInput } from "./MoneyInput/MoneyInput";
export type { MoneyInputProps } from "./MoneyInput/MoneyInput";

export { Alert } from "./Alert/Alert";
export type { AlertProps, AlertTone } from "./Alert/Alert";

export { Skeleton } from "./Feedback/Skeleton";
export type { SkeletonProps } from "./Feedback/Skeleton";
export { EmptyState } from "./Feedback/EmptyState";
export type { EmptyStateProps } from "./Feedback/EmptyState";
export { ErrorState } from "./Feedback/ErrorState";
export type { ErrorStateProps } from "./Feedback/ErrorState";

export { UpgradePrompt, LimitNotice } from "./Premium/Premium";
export type { UpgradePromptVariant } from "./Premium/Premium";

export { TierBadge } from "./TierBadge/TierBadge";
export type { TierBadgeProps } from "./TierBadge/TierBadge";

export {
  Stack,
  PageHeader,
  DescriptionList,
  DescriptionRow,
} from "./Layout/Layout";
export type {
  StackProps,
  StackGap,
  PageHeaderProps,
  DescriptionRowProps,
} from "./Layout/Layout";

export {
  Dialog,
  DialogTrigger,
  DialogClose,
  DialogContent,
  DialogFooter,
} from "./Dialog/Dialog";
export type { DialogContentProps, DialogTone } from "./Dialog/Dialog";

export { Toast, ToastProvider, ToastViewport } from "./Toast/Toast";
export type { ToastProps, ToastTone } from "./Toast/Toast";

export { AppShell, NavItem, AuthLayout } from "./AppShell/AppShell";
export type { AppShellProps } from "./AppShell/AppShell";

export { Pagination } from "./Pagination/Pagination";
export type { PaginationProps } from "./Pagination/Pagination";

/* Shared chart primitives (dataviz) — used by Stats (M6) + Admin (M8). */
export {
  KpiTile,
  KpiValue,
  KpiRow,
  RankedBarChart,
  TimeSeriesBarChart,
} from "./charts";
export type {
  KpiTileProps,
  RankedBarChartProps,
  RankedBarItem,
  TimeSeriesBarChartProps,
  TimeSeriesBarItem,
} from "./charts";

export { ThemeToggle } from "./Controls/ThemeToggle";
export type { ThemeToggleProps, ThemePreference } from "./Controls/ThemeToggle";
export { LanguageToggle } from "./Controls/LanguageToggle";
export type { LanguageToggleProps, Locale } from "./Controls/LanguageToggle";

export { cx } from "./utils/cx";
