import { apiFetch } from './client';

// Mirror VrBook.Contracts.Dtos.Reports.* one-for-one.

export interface OccupancyPoint {
  readonly date: string; // YYYY-MM-DD (DateOnly)
  readonly bookedNights: number;
  readonly availableNights: number;
  readonly occupancyPct: number | null;
}

export interface OccupancySummary {
  readonly totalBookedNights: number;
  readonly totalAvailableNights: number;
  readonly averageOccupancyPct: number | null;
}

export interface OccupancyReport {
  readonly series: readonly OccupancyPoint[];
  readonly summary: OccupancySummary;
}

export interface RevenuePoint {
  readonly date: string;
  readonly revenue: number;
  readonly currency: string;
}

export interface RevenueSummary {
  readonly totalRevenue: number;
  readonly currency: string;
  readonly confirmedBookings: number;
}

export interface RevenueReport {
  readonly series: readonly RevenuePoint[];
  readonly summary: RevenueSummary;
}

export interface AdrPoint {
  readonly date: string;
  readonly adr: number | null; // null on zero-night days; chart breaks the line
  readonly bookedNights: number;
  readonly revenue: number;
  readonly currency: string;
}

export interface AdrSummary {
  readonly averageAdr: number | null;
  readonly totalBookedNights: number;
  readonly totalRevenue: number;
  readonly currency: string;
}

export interface AdrReport {
  readonly series: readonly AdrPoint[];
  readonly summary: AdrSummary;
}

export interface SourceSlice {
  readonly source: string;
  readonly bookings: number;
  readonly nights: number;
}

export interface SourceSummary {
  readonly totalBookings: number;
  readonly totalNights: number;
}

export interface SourceReport {
  readonly slices: readonly SourceSlice[];
  readonly summary: SourceSummary;
}

export interface ReportParams {
  readonly from: string; // YYYY-MM-DD
  readonly to: string;
  readonly propertyId?: string | null;
}

const buildQs = (params: ReportParams): string => {
  const qs = new URLSearchParams({ from: params.from, to: params.to });
  if (params.propertyId) qs.set('propertyId', params.propertyId);
  return qs.toString();
};

export const getOccupancyReport = (params: ReportParams): Promise<OccupancyReport> =>
  apiFetch<OccupancyReport>(`/api/v1/admin/reports/occupancy?${buildQs(params)}`);

export const getRevenueReport = (params: ReportParams): Promise<RevenueReport> =>
  apiFetch<RevenueReport>(`/api/v1/admin/reports/revenue?${buildQs(params)}`);

export const getAdrReport = (params: ReportParams): Promise<AdrReport> =>
  apiFetch<AdrReport>(`/api/v1/admin/reports/adr?${buildQs(params)}`);

export const getSourceReport = (params: ReportParams): Promise<SourceReport> =>
  apiFetch<SourceReport>(`/api/v1/admin/reports/source?${buildQs(params)}`);
