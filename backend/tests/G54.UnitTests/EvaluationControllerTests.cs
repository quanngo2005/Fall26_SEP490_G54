using System.Security.Claims;
using G54.Api.Controllers;
using G54.BLL.Common.Models;
using G54.BLL.Dtos.Evaluation;
using G54.BLL.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Xunit;

namespace G54.UnitTests;

public sealed class EvaluationControllerTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    [Fact]
    public async Task GetEvaluationForm_ForwardsEmployeeAndPeriodWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var employeeId = Guid.NewGuid();
        var periodId = Guid.NewGuid();

        var result = await controller.GetEvaluationForm(employeeId, periodId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((employeeId, periodId, UserId), service.LastFormRequest);
    }

    [Fact]
    public async Task SubmitEvaluation_ForwardsTheRequestAndReturnsTheResult()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var request = new SubmitEvaluationRequestDto(Guid.NewGuid(), Guid.NewGuid(), Comment: "Good quarter");

        var result = await controller.SubmitEvaluation(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(request, service.LastSubmission);
        Assert.Equal(UserId, service.LastSubmissionUserId);
        var data = Assert.IsType<ApiResponse<EvaluationResultDto>>(ok.Value).Data;
        Assert.Equal(service.SubmitResult.EvaluationId, data!.EvaluationId);
    }

    [Fact]
    public async Task GetPeriods_ReturnsTheServiceOptions()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);

        var result = await controller.GetPeriods(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var data = Assert.IsType<ApiResponse<IReadOnlyList<EvaluationPeriodOptionDto>>>(ok.Value).Data;
        Assert.Single(data!);
    }

    [Fact]
    public async Task GetDepartmentEmployees_AllowsAnOmittedDepartment()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();

        var result = await controller.GetDepartmentEmployees(periodId, null, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, (Guid?)null, UserId), service.LastDepartmentEmployeesRequest);
    }

    [Fact]
    public async Task GetCalibration_ForwardsTheOptionalDepartmentAndPosition()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();
        var departmentId = Guid.NewGuid();
        var positionId = Guid.NewGuid();

        var result = await controller.GetCalibration(periodId, departmentId, positionId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, departmentId, positionId, UserId), service.LastCalibrationRequest);
    }

    [Fact]
    public async Task SubmitCalibration_ForwardsTheRequestWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var request = new SubmitCalibrationRequestDto(Guid.NewGuid(), Guid.NewGuid(), []);

        var result = await controller.SubmitCalibration(request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(request, service.LastCalibrationSubmission);
        Assert.Equal(UserId, service.LastCalibrationSubmissionUserId);
    }

    [Fact]
    public async Task GetApprovals_ForwardsThePeriodWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();

        var result = await controller.GetApprovals(periodId, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, UserId), service.LastApprovalsRequest);
    }

    [Fact]
    public async Task RequestRecalibration_UsesTheRouteDepartmentAndTheRejectionBody()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var departmentId = Guid.NewGuid();
        var request = new RejectCalibrationRequestDto(Guid.NewGuid(), "Too many Tier A");

        var result = await controller.RequestRecalibration(departmentId, request, CancellationToken.None);

        Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(departmentId, service.LastRejectedDepartmentId);
        Assert.Same(request, service.LastRejection);
        Assert.Equal(UserId, service.LastRejectionUserId);
    }

    [Fact]
    public async Task FinalizePeriod_ForwardsTheRequestWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var request = new FinalizeEvaluationPeriodRequestDto(Guid.NewGuid(), SignatureToken: "sig");

        var result = await controller.FinalizePeriod(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(request, service.LastFinalize);
        Assert.Equal(UserId, service.LastFinalizeUserId);
        Assert.Equal(service.FinalizeResult.TotalEvaluationsFinalized, Assert.IsType<ApiResponse<FinalizeResultDto>>(ok.Value).Data!.TotalEvaluationsFinalized);
    }

    [Fact]
    public async Task GetBranchPeriods_ForwardsQueryFiltersWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);

        var result = await controller.GetBranchPeriods(2026, "QUARTER", "IN_REVIEW", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((2026, "QUARTER", "IN_REVIEW", UserId), service.LastBranchPeriodsRequest);
        var data = Assert.IsType<ApiResponse<BranchEvaluationPeriodsOverviewDto>>(ok.Value).Data;
        Assert.NotNull(data);
        Assert.Equal("Chi nhánh kiểm thử", data.BranchName);
    }

    [Fact]
    public async Task ApproveDepartmentRanking_ForwardsTheDepartmentIdAndCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var deptId = Guid.NewGuid();
        var request = new ApproveDepartmentRankingRequestDto(Guid.NewGuid(), "Approved by BM");

        var result = await controller.ApproveDepartmentRanking(deptId, request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal(deptId, service.LastApprovedDepartmentId);
        Assert.Same(request, service.LastApproval);
        Assert.Equal(UserId, service.LastApprovalUserId);
        var data = Assert.IsType<ApiResponse<DepartmentApprovalResultDto>>(ok.Value).Data;
        Assert.NotNull(data);
        Assert.Equal("APPROVED", data.Status);
    }

    [Fact]
    public async Task GetPreCloseCheck_ForwardsPeriodIdWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();

        var result = await controller.GetPreCloseCheck(periodId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, UserId), service.LastPreCloseCheckRequest);
        var data = Assert.IsType<ApiResponse<PreCloseCheckOverviewDto>>(ok.Value).Data;
        Assert.NotNull(data);
        Assert.Equal("TEST-Q3", data.PeriodCode);
        Assert.Single(data.Items);
    }

    [Fact]
    public async Task GetPersonalEvaluation_ForwardsPeriodIdWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();

        var result = await controller.GetPersonalEvaluation(periodId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, UserId), service.LastPersonalEvaluationRequest);
        var data = Assert.IsType<ApiResponse<PersonalEvaluationViewDto>>(ok.Value).Data;
        Assert.NotNull(data);
        Assert.Equal(periodId, data.PeriodId);
    }

    [Fact]
    public async Task AcknowledgePersonalEvaluation_ForwardsRequestWithTheCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var request = new AcknowledgePersonalEvaluationRequestDto(Guid.NewGuid());

        var result = await controller.AcknowledgePersonalEvaluation(request, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Same(request, service.LastAcknowledgeRequest);
        Assert.Equal(UserId, service.LastAcknowledgeUserId);
        Assert.True(Assert.IsType<ApiResponse<AcknowledgePersonalEvaluationResultDto>>(ok.Value).Data!.Success);
    }

    [Fact]
    public async Task GetPersonalCriteriaLedger_ForwardsPeriodCriteriaAndCallerId()
    {
        var service = new RecordingEvaluationService();
        var controller = ControllerFor(service);
        var periodId = Guid.NewGuid();
        var criteriaId = Guid.NewGuid();

        var result = await controller.GetPersonalCriteriaLedger(periodId, criteriaId, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal((periodId, criteriaId, UserId), service.LastPersonalLedgerRequest);
        var data = Assert.IsType<ApiResponse<IReadOnlyList<PersonalCriteriaLedgerItemDto>>>(ok.Value).Data;
        Assert.NotNull(data);
        Assert.Single(data);
    }

    [Fact]
    public void Controller_RequiresAuthorizationAndUsesTheEvaluationsRoute()
    {
        var type = typeof(EvaluationController);

        Assert.NotNull(type.GetCustomAttributes(typeof(Microsoft.AspNetCore.Authorization.AuthorizeAttribute), inherit: false).SingleOrDefault());
        Assert.Equal("evaluations", type.GetCustomAttributes(typeof(RouteAttribute), inherit: false).Cast<RouteAttribute>().Single().Template);
    }

    private static EvaluationController ControllerFor(IEvaluationService service) => new(service)
    {
        ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext
            {
                User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", UserId.ToString())], "TestAuth")),
            },
        },
    };

    private sealed class RecordingEvaluationService : IEvaluationService
    {
        public EvaluationResultDto SubmitResult { get; } = new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 88m, "DRAFT", "A", DateTimeOffset.UtcNow, "ok");

        public FinalizeResultDto FinalizeResult { get; } = new(Guid.NewGuid(), "LOCKED", DateTimeOffset.UtcNow, 12, "locked");

        public (Guid EmployeeId, Guid PeriodId, Guid UserId) LastFormRequest { get; private set; }

        public SubmitEvaluationRequestDto? LastSubmission { get; private set; }

        public Guid LastSubmissionUserId { get; private set; }

        public (Guid PeriodId, Guid? DepartmentId, Guid UserId) LastDepartmentEmployeesRequest { get; private set; }

        public (Guid PeriodId, Guid? DepartmentId, Guid? PositionId, Guid UserId) LastCalibrationRequest { get; private set; }

        public SubmitCalibrationRequestDto? LastCalibrationSubmission { get; private set; }

        public Guid LastCalibrationSubmissionUserId { get; private set; }

        public (Guid PeriodId, Guid UserId) LastApprovalsRequest { get; private set; }

        public Guid LastRejectedDepartmentId { get; private set; }

        public RejectCalibrationRequestDto? LastRejection { get; private set; }

        public Guid LastRejectionUserId { get; private set; }

        public Guid LastApprovedDepartmentId { get; private set; }

        public ApproveDepartmentRankingRequestDto? LastApproval { get; private set; }

        public Guid LastApprovalUserId { get; private set; }

        public (Guid PeriodId, Guid UserId) LastPreCloseCheckRequest { get; private set; }

        public FinalizeEvaluationPeriodRequestDto? LastFinalize { get; private set; }

        public Guid LastFinalizeUserId { get; private set; }

        public Task<EvaluationFormDto> GetEvaluationFormAsync(Guid employeeId, Guid periodId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastFormRequest = (employeeId, periodId, currentUserId);
            return Task.FromResult<EvaluationFormDto>(null!);
        }

        public Task<EvaluationResultDto> SubmitEvaluationAsync(SubmitEvaluationRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastSubmission = request;
            LastSubmissionUserId = currentUserId;
            return Task.FromResult(SubmitResult);
        }

        public Task<IReadOnlyList<EvaluationPeriodOptionDto>> GetEvaluationPeriodsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<EvaluationPeriodOptionDto>>(
                [new EvaluationPeriodOptionDto(Guid.NewGuid(), "2026-Q1", "Q1 2026", new DateOnly(2026, 1, 1), new DateOnly(2026, 3, 31), "QUARTER", "OPEN", true)]);

        public Task<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>> GetDepartmentEmployeesForEvaluationAsync(
            Guid periodId,
            Guid? departmentId,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            LastDepartmentEmployeesRequest = (periodId, departmentId, currentUserId);
            return Task.FromResult<IReadOnlyList<DepartmentEmployeeEvaluationSummaryDto>>([]);
        }

        public Task<CalibrationOverviewDto> GetDepartmentCalibrationAsync(
            Guid periodId,
            Guid? departmentId,
            Guid? positionId,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            LastCalibrationRequest = (periodId, departmentId, positionId, currentUserId);
            return Task.FromResult<CalibrationOverviewDto>(null!);
        }

        public Task<CalibrationSubmitResultDto> SubmitDepartmentCalibrationAsync(SubmitCalibrationRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastCalibrationSubmission = request;
            LastCalibrationSubmissionUserId = currentUserId;
            return Task.FromResult(new CalibrationSubmitResultDto(request.DepartmentId, request.PeriodId, "DRAFT", 0, 0, true, DateTimeOffset.UtcNow, "saved"));
        }

        public Task<BranchApprovalOverviewDto> GetBranchApprovalOverviewAsync(Guid periodId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastApprovalsRequest = (periodId, currentUserId);
            return Task.FromResult<BranchApprovalOverviewDto>(null!);
        }

        public Task<CalibrationDecisionResultDto> RejectDepartmentCalibrationAsync(Guid departmentId, RejectCalibrationRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastRejectedDepartmentId = departmentId;
            LastRejection = request;
            LastRejectionUserId = currentUserId;
            return Task.FromResult(new CalibrationDecisionResultDto(departmentId, request.PeriodId, "RECALIBRATION_REQUESTED", "returned"));
        }

        public Task<DepartmentApprovalResultDto> ApproveDepartmentCalibrationAsync(Guid departmentId, ApproveDepartmentRankingRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastApprovedDepartmentId = departmentId;
            LastApproval = request;
            LastApprovalUserId = currentUserId;
            return Task.FromResult(new DepartmentApprovalResultDto(departmentId, request.PeriodId, "APPROVED", 5, "approved"));
        }

        public Task<PreCloseCheckOverviewDto> GetPreCloseCheckAsync(Guid periodId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastPreCloseCheckRequest = (periodId, currentUserId);
            var item = new PreCloseCheckItemDto("CHECK_1", "Check 1", 0, true);
            return Task.FromResult(new PreCloseCheckOverviewDto(periodId, "TEST-Q3", "Q3", new DateOnly(2026, 7, 1), new DateOnly(2026, 9, 30), DateTimeOffset.UtcNow, true, 0, [item]));
        }

        public Task<FinalizeResultDto> FinalizeAndLockPeriodAsync(FinalizeEvaluationPeriodRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastFinalize = request;
            LastFinalizeUserId = currentUserId;
            return Task.FromResult(FinalizeResult);
        }

        public (int? Year, string? PeriodType, string? Status, Guid UserId) LastBranchPeriodsRequest { get; private set; }

        public Task<BranchEvaluationPeriodsOverviewDto> GetBranchEvaluationPeriodsOverviewAsync(
            int? year,
            string? periodType,
            string? status,
            Guid currentUserId,
            CancellationToken cancellationToken = default)
        {
            LastBranchPeriodsRequest = (year, periodType, status, currentUserId);
            return Task.FromResult(new BranchEvaluationPeriodsOverviewDto("Chi nhánh kiểm thử", "BR-TEST", [2026], []));
        }

        public (Guid? PeriodId, Guid UserId) LastPersonalEvaluationRequest { get; private set; }
        public AcknowledgePersonalEvaluationRequestDto? LastAcknowledgeRequest { get; private set; }
        public Guid LastAcknowledgeUserId { get; private set; }
        public (Guid PeriodId, Guid CriteriaId, Guid UserId) LastPersonalLedgerRequest { get; private set; }

        public Task<PersonalEvaluationViewDto> GetPersonalEvaluationAsync(Guid? periodId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastPersonalEvaluationRequest = (periodId, currentUserId);
            return Task.FromResult(new PersonalEvaluationViewDto(
                periodId ?? Guid.NewGuid(),
                "Q3/2026",
                "2026-Q3",
                "FINAL",
                "—",
                0,
                false,
                85m,
                null,
                null,
                null,
                "A2",
                ["A1", "A2", "A3", "B", "C"],
                "Top 20%",
                1,
                5,
                "Sales Officer",
                [],
                null,
                0m,
                null,
                false,
                null));
        }

        public Task<AcknowledgePersonalEvaluationResultDto> AcknowledgePersonalEvaluationAsync(AcknowledgePersonalEvaluationRequestDto request, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastAcknowledgeRequest = request;
            LastAcknowledgeUserId = currentUserId;
            return Task.FromResult(new AcknowledgePersonalEvaluationResultDto(true, DateTimeOffset.UtcNow, "ok"));
        }

        public Task<IReadOnlyList<PersonalCriteriaLedgerItemDto>> GetPersonalCriteriaLedgerAsync(Guid periodId, Guid criteriaId, Guid currentUserId, CancellationToken cancellationToken = default)
        {
            LastPersonalLedgerRequest = (periodId, criteriaId, currentUserId);
            IReadOnlyList<PersonalCriteriaLedgerItemDto> items =
            [
                new PersonalCriteriaLedgerItemDto(Guid.NewGuid(), "TX-001", "10/10/2026", "100,000 VND", 5m, "VALID")
            ];
            return Task.FromResult(items);
        }
    }
}
