/** List row for SCR-ADM-07 import history. Mirrors G54.BLL.Dtos.TransactionImport.ImportBatchDto (list view omits `errors`). */
export interface ImportBatchSummary {
  id: string;
  fileName: string;
  status: string;
  totalRows: number;
  importedRows: number;
  duplicateRows: number;
  errorRows: number;
  errors: null;
  importedAt: string;
  importedBy: string;
}

/** One rejected row of an import (`ImportRowErrorDto`). `field` is the header name as written in the uploaded file. */
export interface ImportRowError {
  row: number;
  field: string;
  code: string;
  message: string;
}

/** One saved row of a batch (`ImportedTransactionDto`, `GET /transaction-imports/{id}/transactions`). */
export interface ImportedTransaction {
  id: string;
  transactionCode: string;
  employeeCode: string;
  productCode: string;
  transactionDate: string;
  amount: number;
  denominator: number | null;
  referEmployeeCode: string | null;
  customerCode: string | null;
}

/** The file types an import batch can hold (`import_batch.file_type`). */
export type ImportFileType = 'TRANSACTION' | 'SERVICE_RATING';

/** One customer service rating saved by a rating import (`GET /service-rating-imports/{id}/ratings`). */
export interface ImportedRating {
  id: string;
  transactionCode: string;
  employeeCode: string;
  ratingDate: string;
  score: number;
  customerComment: string | null;
}

/** Codes reconciled right after the import. `null` when the batch was saved but reconciliation did not run. */
export interface ImportReconcileSummary {
  matched: number;
  notMatched: number;
  inReview: number;
}

/**
 * `POST /transaction-imports` and `POST /service-rating-imports` result (SCR-ADM-06 step 3). Mirrors `ImportBatchDto`
 * including `errors` and `reconcile` (always `null` for service ratings).
 */
export interface ImportBatchResult extends Omit<ImportBatchSummary, 'errors'> {
  errors: ImportRowError[] | null;
  reconcile: ImportReconcileSummary | null;
}

/** Upload progress (0-100) followed by the saved batch. */
export type ImportUploadEvent =
  { kind: 'progress'; percent: number } | { kind: 'done'; result: ImportBatchResult };
