/** Mirrors `HandoverRecipientDto` returned by `GET /handovers/recipients`. */
export interface HandoverRecipient {
  id: string;
  employeeCode: string;
  fullName: string;
  jobPositionTitle: string;
}

/** Body of `POST /works/{workId}/handovers`. */
export interface CreateHandoverRequest {
  toEmployeeId: string;
  reason: string;
  /** Monetary value of the work, checked against the server threshold and not stored. */
  amount: number;
}

export type HandoverDirection = 'INCOMING' | 'OUTGOING' | 'ALL';
export type HandoverStatus = 'PENDING' | 'ACCEPTED' | 'REJECTED';
export type HandoverKpiStatus = 'PENDING' | 'RECORDED';

/** Query parameters for `GET /handovers` (SCR-WRK-05). */
export interface HandoverListQuery {
  direction?: HandoverDirection;
  status?: HandoverStatus | '';
  fromEmployeeId?: string;
  kpiStatus?: HandoverKpiStatus | '';
  keyword?: string;
  page?: number;
  pageSize?: number;
}

/** Mirrors `HandoverListItemDto` (SCR-WRK-05 & SCR-WRK-05-D1). */
export interface HandoverListItem {
  id: string;
  handoverCode: string;
  workId: string;
  workCode: string;
  workTitle: string;
  workStatus: string;
  workTypeName: string | null;
  customerName: string | null;
  customerCif: string | null;
  targetValue: number | null;
  plannedEndDate: string | null;
  fromEmployeeId: string;
  fromEmployeeCode: string;
  fromEmployeeName: string;
  fromJobPositionTitle: string;
  toEmployeeId: string;
  toEmployeeCode: string;
  toEmployeeName: string;
  toJobPositionTitle: string;
  reason: string;
  referrerSharePercent: number;
  status: HandoverStatus;
  kpiStatus: HandoverKpiStatus;
  handoverAt: string;
  acceptedAt: string | null;
  updatedAt: string;
  isReadOnly: boolean;
}

/** Mirrors `HandoverDetailDto`. `status` is `PENDING` until the recipient answers. */
export interface HandoverDetail {
  id: string;
  workId: string;
  workCode: string;
  workTitle: string;
  fromEmployeeId: string;
  fromEmployeeName: string;
  toEmployeeId: string;
  toEmployeeName: string;
  reason: string;
  status: string;
}
