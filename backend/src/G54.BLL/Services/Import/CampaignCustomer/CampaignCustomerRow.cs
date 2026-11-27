using G54.BLL.Common.Import;

namespace G54.BLL.Services.Import.CampaignCustomer;

/// <summary>One campaign customer import row (<c>docs/api/CAMPAIGN_CONTRACT.md</c> §5).</summary>
public sealed class CampaignCustomerRow
{
    public int RowNumber { get; set; }
    public string? CustomerCif { get; set; }
    public string? CustomerName { get; set; }
    public string? Phone { get; set; }
    public string? Note { get; set; }

    public bool IsUpdate { get; set; }
    public Guid? ExistingId { get; set; }
    public List<ImportError> Errors { get; } = [];

    public void AddError(string column, string code, string message) => Errors.Add(new ImportError(column, code, message));
}
