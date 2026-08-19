using APS.Domain;

namespace APS.Application;

/// <summary>
/// Generic create/update/delete over the master-data entity types the UI editor supports. One
/// service instead of one per entity type - the entities themselves are plain EF-mapped classes
/// with no business logic attached, so there is nothing type-specific to add per entity beyond
/// "which DbSet does this belong to," which the implementation resolves via reflection.
/// </summary>
public interface IMasterDataAdminService
{
    Task<T> CreateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity;
    Task<T> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity;
    Task DeleteAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : Entity;
}
