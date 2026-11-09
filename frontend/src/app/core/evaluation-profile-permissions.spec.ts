import {
  canApproveEvaluationProfiles,
  canManageEvaluationProfiles,
} from './evaluation-profile-permissions';

describe('evaluation-profile-permissions', () => {
  it('lets only KPI_MANAGE (or full access) manage profiles, whatever the code format', () => {
    expect(canManageEvaluationProfiles(['kpi:manage'])).toBeTrue();
    expect(canManageEvaluationProfiles(['KPI_MANAGE'])).toBeTrue();
    expect(canManageEvaluationProfiles(['FULL_ACCESS'])).toBeTrue();
    expect(canManageEvaluationProfiles(['evaluation-profile:approve'])).toBeFalse();
  });

  it('lets only EVALUATION_PROFILE_APPROVE (or full access) approve profiles', () => {
    expect(canApproveEvaluationProfiles(['evaluation-profile:approve'])).toBeTrue();
    expect(canApproveEvaluationProfiles(['FULL_ACCESS'])).toBeTrue();
    expect(canApproveEvaluationProfiles(['kpi:manage'])).toBeFalse();
  });

  it('grants nothing when there are no permissions', () => {
    expect(canManageEvaluationProfiles(null)).toBeFalse();
    expect(canApproveEvaluationProfiles(undefined)).toBeFalse();
    expect(canApproveEvaluationProfiles([])).toBeFalse();
  });
});
