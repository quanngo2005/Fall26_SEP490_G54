import { HttpClient } from '@angular/common/http';
import { inject, Injectable } from '@angular/core';
import { map, Observable } from 'rxjs';
import { environment } from '../../environments/environment';
import { ApiResponse } from './api-response.model';

/**
 * Target Allocation v3 suggestion engine (docs/api/TARGET_ALLOCATION_CONTRACT.md, "Suggestion engine";
 * docs/plans/TARGET_ALLOCATION_V3_DECISIONS.md D1/D4/D5/D6/D8/D9). Read-only: `POST /target-suggestions/{year}`
 * computes a suggested split for one tier of the BM wizard (W3 quarter split = tier 1, W5/W6 department split =
 * tier 2); the caller reviews the result and saves it through the existing `TargetAssignmentService` PUT routes.
 */

export type SuggestionTier = 1 | 2 | 3;
export type SuggestionBasisDto = 'TARGET_LAST_YEAR' | 'ACTUAL_LAST_YEAR';
export type SuggestionOverwriteDto = 'EMPTY_ONLY' | 'UNLOCKED';
export type SuggestionRemainderMethodDto = 'LARGEST_REMAINDER' | 'BUFFER';
export type SuggestionEmployeesDto = 'ALL' | 'NEW_ONLY';
export type SuggestionRoundingModeDto = 'NEAREST' | 'UP' | 'DOWN';

export interface SuggestionCurrentValueDto {
  readonly unitId: string;
  readonly year: number | null;
  readonly q1: number | null;
  readonly q2: number | null;
  readonly q3: number | null;
  readonly q4: number | null;
  readonly locked: boolean;
}

export interface SuggestionRoundingDto {
  readonly moneyStep: number;
  readonly countStep: number;
  readonly mode: SuggestionRoundingModeDto;
}

export interface SuggestionRemainderDto {
  readonly method: SuggestionRemainderMethodDto;
  readonly bufferUnitId: string | null;
}

export interface SuggestTargetsRequestDto {
  readonly tier: SuggestionTier;
  readonly basis: SuggestionBasisDto;
  readonly anchorId: string;
  readonly kpiIds: readonly string[] | null;
  readonly departmentId: string | null;
  readonly employees: SuggestionEmployeesDto | null;
  readonly periods: 'YEAR_PLUS_4Q';
  readonly overwrite: SuggestionOverwriteDto;
  readonly currentValues: readonly SuggestionCurrentValueDto[];
  readonly rounding: SuggestionRoundingDto;
  readonly remainder: SuggestionRemainderDto;
}

export interface SuggestionCellDto {
  readonly unitId: string;
  readonly unitName: string;
  readonly year: number | null;
  readonly q1: number | null;
  readonly q2: number | null;
  readonly q3: number | null;
  readonly q4: number | null;
  readonly usedFallbackAverage: boolean;
  readonly overwritten: boolean;
  readonly locked: boolean;
  readonly lockReason: string | null;
}

export interface SuggestionLockedUnitDto {
  readonly unitId: string;
  readonly unitName: string;
  readonly reason: string;
}

export interface SuggestionKpiResultDto {
  readonly kpiId: string;
  readonly kpiCode: string;
  readonly kpiName: string;
  readonly unit: string;
  readonly aggregationType: string;
  readonly cells: readonly SuggestionCellDto[];
  readonly lockedUnits: readonly SuggestionLockedUnitDto[];
  readonly total: number;
  readonly sumMatches: boolean;
  readonly disabledReason: string | null;
  readonly warnings: readonly string[];
}

export interface SuggestTargetsResponseDto {
  readonly tier: SuggestionTier;
  readonly basis: SuggestionBasisDto;
  readonly overwrite: SuggestionOverwriteDto;
  readonly kpis: readonly SuggestionKpiResultDto[];
  readonly warnings: readonly string[];
}

@Injectable({ providedIn: 'root' })
export class TargetSuggestionService {
  private readonly http = inject(HttpClient);
  private readonly url = `${environment.apiUrl}/target-suggestions`;

  /** `POST /target-suggestions/{year}`: writes nothing, returns a suggested split to review before saving. */
  suggest(year: number, request: SuggestTargetsRequestDto): Observable<SuggestTargetsResponseDto> {
    return this.http
      .post<ApiResponse<SuggestTargetsResponseDto>>(`${this.url}/${year}`, request)
      .pipe(map((response) => this.unwrap(response)));
  }

  private unwrap<T>(response: ApiResponse<T>): T {
    if (!response.success || response.data == null) {
      throw new Error('Invalid target suggestion response');
    }
    return response.data;
  }
}
