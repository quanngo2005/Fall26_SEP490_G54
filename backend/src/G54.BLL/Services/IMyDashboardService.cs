using System;
using System.Threading;
using System.Threading.Tasks;
using G54.BLL.Dtos.Dashboard;

namespace G54.BLL.Services;

/// <summary>
/// Service powering the Personal / Home Dashboard (SCR-HOME-01).
/// Provides operational KPI progress card metrics for Teller and Sales Officer roles.
/// Contract: docs/api/MY_DASHBOARD_PROGRESS_CARD_CONTRACT.md.
/// </summary>
public interface IMyDashboardService
{
    Task<MyProgressCardDto> GetMyProgressCardAsync(
        Guid accountId,
        MyProgressCardQueryDto query,
        CancellationToken cancellationToken = default);
}

