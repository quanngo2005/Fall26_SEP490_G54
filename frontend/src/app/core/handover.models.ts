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
