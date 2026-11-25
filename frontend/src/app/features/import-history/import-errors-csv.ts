import { ImportRowError } from '../../core/import-history.models';

/** Lets Excel read the file as UTF-8, so Vietnamese text stays readable. */
const UTF8_BOM = '﻿';

/** A cell starting with one of these is run as a formula by spreadsheet programs (CSV injection). */
const FORMULA_START = /^[=+\-@\t\r]/;

function cell(value: string | number): string {
  const text = String(value);
  const safe = FORMULA_START.test(text) ? `'${text}` : text;
  return /[",\r\n]/.test(safe) ? `"${safe.replace(/"/g, '""')}"` : safe;
}

/** The rejected rows of a batch as an Excel-friendly CSV file: header row, then `row, field, code, message`. */
export function importErrorsCsv(headers: readonly string[], rows: readonly ImportRowError[]): Blob {
  const lines = [
    headers.map(cell).join(','),
    ...rows.map((error) => [error.row, error.field, error.code, error.message].map(cell).join(',')),
  ];
  return new Blob([UTF8_BOM + lines.join('\r\n')], { type: 'text/csv;charset=utf-8' });
}
