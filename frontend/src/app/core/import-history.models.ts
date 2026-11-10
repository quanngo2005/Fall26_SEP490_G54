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
