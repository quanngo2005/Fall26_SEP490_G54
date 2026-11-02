namespace G54.DAL.Auditing;

/// <summary>
/// Marks an entity class or property as excluded from audit logging. Applied to the entity itself to skip
/// it entirely (e.g. <c>AuditLog</c>, <c>AuthAuditLog</c>), or to a property to omit it from Old/New values
/// (e.g. tokens, session data) without skipping the rest of the entity.
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Property, AllowMultiple = false)]
public sealed class AuditIgnoreAttribute : Attribute
{
}
