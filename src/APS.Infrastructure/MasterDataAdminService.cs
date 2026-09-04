using APS.Application;
using APS.Domain;
using Microsoft.EntityFrameworkCore;

namespace APS.Infrastructure;

public sealed class MasterDataAdminService(ApsDbContext db) : IMasterDataAdminService
{
    public async Task<T> CreateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity
    {
        await ValidateAsync(entity, cancellationToken);
        db.Set<T>().Add(entity);
        await db.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public async Task<T> UpdateAsync<T>(T entity, CancellationToken cancellationToken = default) where T : Entity
    {
        await ValidateAsync(entity, cancellationToken);
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

    private Task ValidateAsync<T>(T entity, CancellationToken cancellationToken) where T : Entity =>
        entity is ResourceCalendar calendar
            ? ValidateCalendarAsync(calendar, cancellationToken)
            : Task.CompletedTask;

    private async Task ValidateCalendarAsync(ResourceCalendar calendar, CancellationToken cancellationToken)
    {
        if (calendar.ResourceId == Guid.Empty)
            throw new InvalidOperationException("A resource calendar interval must reference a physical resource.");
        if (calendar.End <= calendar.Start)
            throw new InvalidOperationException("Resource calendar end must be after start.");
        if (calendar.CapacityFactorPct is < 0m or > 100m)
            throw new InvalidOperationException("Resource calendar capacity factor must be between 0 and 100 percent.");

        var overlaps = await db.ResourceCalendars.AsNoTracking().AnyAsync(x =>
            x.ResourceId == calendar.ResourceId &&
            x.Id != calendar.Id &&
            x.Start < calendar.End &&
            x.End > calendar.Start,
            cancellationToken);
        if (overlaps)
            throw new InvalidOperationException(
                "This resource already has a calendar interval in the selected time range. Edit or split the existing interval instead of stacking availability constraints.");
    }
}
