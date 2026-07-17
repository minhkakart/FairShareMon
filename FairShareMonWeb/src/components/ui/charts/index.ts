/**
 * Shared chart primitives (dataviz layer). Extracted from the M6 stats feature
 * so Stats (M6) and Admin (M8) share ONE chart system on the `--fs-viz-*`
 * palette. All presentational + theme-aware; the caller supplies API-computed
 * ratios/values (no money math in a chart) and pairs each chart with an
 * accessible data table (the chart region is `role="img"`).
 */
export { KpiTile, KpiValue, KpiRow } from "./KpiTile";
export type { KpiTileProps } from "./KpiTile";

export { RankedBarChart } from "./RankedBarChart";
export type { RankedBarChartProps, RankedBarItem } from "./RankedBarChart";

export { TimeSeriesBarChart } from "./TimeSeriesBarChart";
export type {
  TimeSeriesBarChartProps,
  TimeSeriesBarItem,
} from "./TimeSeriesBarChart";
