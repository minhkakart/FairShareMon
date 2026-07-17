/**
 * StatTile — re-pointed to the shared `KpiTile` chart primitive (M8 OQ1a). The
 * M6 stat tile was generalized into `components/ui/charts/KpiTile` so Stats (M6)
 * and Admin (M8) share ONE KPI system; this thin re-export keeps the feature-local
 * name/import path stable for any existing caller.
 */
export { KpiTile as StatTile } from "@/components/ui";
export type { KpiTileProps as StatTileProps } from "@/components/ui";
