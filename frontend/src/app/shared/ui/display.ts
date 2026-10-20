/** Locale-aware number rendering that follows the N/A rule of docs/api/DEPARTMENT_CONTRACT.md. */
export function displayNumber(
  value: number | null | undefined,
  language: string,
  notAvailable: string,
): string {
  return value == null
    ? notAvailable
    : new Intl.NumberFormat(language, { maximumFractionDigits: 2 }).format(value);
}

export function initials(name: string): string {
  return name
    .split(/\s+/)
    .filter(Boolean)
    .slice(-2)
    .map((part) => part[0])
    .join('')
    .toUpperCase();
}

/** Human readable byte size. Unit symbols are universal and intentionally not localized. */
export function displayBytes(
  bytes: number | null | undefined,
  language: string,
  notAvailable: string,
): string {
  if (bytes == null) return notAvailable;
  if (bytes < 1024) return `${bytes} B`;
  const format = new Intl.NumberFormat(language, { maximumFractionDigits: 1 });
  return bytes < 1024 * 1024
    ? `${format.format(bytes / 1024)} KB`
    : `${format.format(bytes / (1024 * 1024))} MB`;
}
