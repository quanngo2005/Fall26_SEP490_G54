import { provideHttpClient } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { environment } from '../../environments/environment';
import { SuggestTargetsRequestDto, TargetSuggestionService } from './target-suggestion.service';

describe('TargetSuggestionService', () => {
  let service: TargetSuggestionService;
  let http: HttpTestingController;
  const url = `${environment.apiUrl}/target-suggestions/2026`;
  const request = {
    tier: 1,
    basis: 'TARGET_LAST_YEAR',
    anchorId: 'b1',
  } as SuggestTargetsRequestDto;

  beforeEach(() => {
    TestBed.configureTestingModule({
      providers: [provideHttpClient(), provideHttpClientTesting()],
    });
    service = TestBed.inject(TargetSuggestionService);
    http = TestBed.inject(HttpTestingController);
  });

  afterEach(() => http.verify());

  it('posts the request to /target-suggestions/{year} and unwraps the envelope', () => {
    let result: unknown;
    service.suggest(2026, request).subscribe((value) => (result = value));

    const call = http.expectOne(url);
    expect(call.request.method).toBe('POST');
    expect(call.request.body).toEqual(request);
    call.flush({ success: true, data: { tier: 1 } });

    expect(result).toEqual({ tier: 1 });
  });

  it('fails instead of faking success when the envelope has no data', () => {
    let failed = false;
    service.suggest(2026, request).subscribe({ error: () => (failed = true) });

    http.expectOne(url).flush({ success: true, data: null });

    expect(failed).toBeTrue();
  });
});
