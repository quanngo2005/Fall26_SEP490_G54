using System;
using System.Collections.Generic;

namespace G54.DAL.Entities;

public partial class Employee
{
    public Guid Id { get; set; }

    public string EmployeeCode { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string Email { get; set; } = null!;

    public string? Phone { get; set; }

    public Guid DepartmentId { get; set; }

    public Guid JobPositionId { get; set; }

    public Guid? ManagerId { get; set; }

    public DateTimeOffset LastSyncAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public EmployeeStatus Status { get; set; }

    public virtual Account? Account { get; set; }

    public virtual ICollection<ActualProgress> ActualProgresses { get; set; } = new List<ActualProgress>();

    public virtual ICollection<Appeal> Appeals { get; set; } = new List<Appeal>();

    public virtual Department Department { get; set; } = null!;

    public virtual ICollection<Evidence> Evidences { get; set; } = new List<Evidence>();

    public virtual ICollection<Employee> InverseManager { get; set; } = new List<Employee>();

    public virtual JobPosition JobPosition { get; set; } = null!;

    public virtual Employee? Manager { get; set; }

    public virtual ICollection<Participant> Participants { get; set; } = new List<Participant>();

    public virtual ICollection<PerformanceContribution> PerformanceContributions { get; set; } = new List<PerformanceContribution>();

    public virtual ICollection<PerformanceEvaluation> PerformanceEvaluationEmployees { get; set; } = new List<PerformanceEvaluation>();

    public virtual ICollection<PerformanceEvaluation> PerformanceEvaluationReviewerEmployees { get; set; } = new List<PerformanceEvaluation>();

    public virtual ICollection<ResultSubmission> ResultSubmissions { get; set; } = new List<ResultSubmission>();

    public virtual ICollection<RiskEvent> RiskEvents { get; set; } = new List<RiskEvent>();

    public virtual ICollection<TargetAssignment> TargetAssignmentApprovedByNavigations { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<TargetAssignment> TargetAssignmentAssignedByNavigations { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<TargetAssignment> TargetAssignmentEmployees { get; set; } = new List<TargetAssignment>();

    public virtual ICollection<TaskHandover> TaskHandoverFromEmployees { get; set; } = new List<TaskHandover>();

    public virtual ICollection<TaskHandover> TaskHandoverToEmployees { get; set; } = new List<TaskHandover>();

    public virtual ICollection<Verification> Verifications { get; set; } = new List<Verification>();

    public virtual ICollection<Work> Works { get; set; } = new List<Work>();
}
