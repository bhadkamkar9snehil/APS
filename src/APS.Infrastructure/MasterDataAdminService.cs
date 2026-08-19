using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class MasterDataAdminService(ApsDbContext db) : IMasterDataAdminService
{
    public async Task<T> CreateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity
    {
        db.Set<T>().Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<T> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity
    {
        db.Set<T>().Update(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task DeleteAsync<T>(Guid id, CancellationToken cancellationToken = default) where T : Entity
    {
        var entity = await db.Set<T>().FirstOrDefaultAsync(x => x.Id == id, cancellationToken);
        if (entity is null) return;
        db.Set<T>().Remove(entity);
        await db.SaveChangesAsync(cancellationToken);
    }
}
