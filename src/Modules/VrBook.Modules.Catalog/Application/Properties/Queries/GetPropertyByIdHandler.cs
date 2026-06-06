using MediatR;
using Microsoft.EntityFrameworkCore;
using VrBook.Modules.Catalog.Infrastructure.Persistence;

namespace VrBook.Modules.Catalog.Application.Properties.Queries;

internal sealed class GetPropertyByIdHandler(CatalogDbContext db) : IRequestHandler<GetPropertyByIdQuery, PropertyBasicInfo?>
{
    public Task<PropertyBasicInfo?> Handle(GetPropertyByIdQuery request, CancellationToken cancellationToken) =>
        db.Properties.AsNoTracking()
            .Where(p => p.Id == request.Id)
            .Select(p => new PropertyBasicInfo(p.Id, p.Slug, p.Title, p.OwnerUserId, p.IsActive))
            .FirstOrDefaultAsync(cancellationToken);
}
