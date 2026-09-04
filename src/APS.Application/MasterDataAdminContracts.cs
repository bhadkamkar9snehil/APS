using APS.Domain;

namespace APS.Application;

/// <summary>
/// Generic create/update/delete boundary for editable planning master data. Shared persistence stays
/// generic, while a small number of cross-row invariants that must hold regardless of UI entry point
/// are enforced centrally by the implementation (for example, non-overlapping resource calendars).
/// </summary>
public interface IMasterDataAdminService
{
    Task<T> CreateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity;
    Task<T> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity;
    Task DeleteAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : Entity;
}
