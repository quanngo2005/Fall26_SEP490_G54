import { HttpErrorResponse } from '@angular/common/http';
import {
  hoursLeft,
  readSavedFilters,
  reconciliationErrorKey,
  SAVED_FILTERS_KEY,
  statusTone,
  writeSavedFilters,
} from './reconciliation-display';

describe('reconciliation display helpers', () => {
  afterEach(() => localStorage.removeItem(SAVED_FILTERS_KEY));

  it('makes only a mismatch stand out', () => {
    expect(statusTone('NOT_MATCHED')).toBe('danger');
    expect(statusTone('IN_REVIEW')).toBe('warning');
    expect(statusTone('MATCHED')).toBe('success');
  });

  it('counts whole hours left and never goes below zero', () => {
    const now = Date.parse('2026-10-26T00:00:00Z');

    expect(hoursLeft('2026-10-27T05:30:00Z', now)).toBe(30);
    expect(hoursLeft('2026-10-25T00:00:00Z', now)).toBe(0);
  });

  it('chooses the error text by the status the API answered with', () => {
    expect(reconciliationErrorKey(new HttpErrorResponse({ status: 403 }))).toBe(
      'reconciliation.errors.forbidden',
    );
    expect(reconciliationErrorKey(new HttpErrorResponse({ status: 404 }))).toBe(
      'reconciliation.errors.notFound',
    );
    expect(reconciliationErrorKey(new HttpErrorResponse({ status: 500 }))).toBe(
      'reconciliation.errors.generic',
    );
    expect(reconciliationErrorKey(new Error('offline'))).toBe('reconciliation.errors.generic');
  });

  it('saves, reads and forgets the saved filters', () => {
    expect(readSavedFilters()).toBeNull();
    const filters = {
      status: 'NOT_MATCHED',
      search: 'GD-1',
      period: 'ALL',
      from: '',
      to: '',
      department: 'ALL',
      jobPosition: 'ALL',
      product: 'ALL',
      reason: 'ALL',
    };

    expect(writeSavedFilters(filters)).toBeTrue();
    expect(readSavedFilters()).toEqual(filters);
    expect(writeSavedFilters(null)).toBeTrue();
    expect(readSavedFilters()).toBeNull();
  });

  it('ignores a broken saved value', () => {
    localStorage.setItem(SAVED_FILTERS_KEY, '{not json');

    expect(readSavedFilters()).toBeNull();
  });
});
