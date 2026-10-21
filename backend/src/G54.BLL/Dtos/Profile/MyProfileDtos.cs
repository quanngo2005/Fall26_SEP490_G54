using System.ComponentModel.DataAnnotations;

namespace G54.BLL.Dtos.Profile;

/// <summary>The phone number after a change (<c>null</c> when it was cleared).</summary>
public sealed record MyPhoneDto(string? Phone);

public sealed record UpdateMyProfileDto([param: StringLength(100)] string? Phone);
