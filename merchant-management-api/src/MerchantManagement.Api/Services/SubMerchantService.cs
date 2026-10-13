using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Data;
using MerchantManagement.Api.Domain;
using MerchantManagement.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Services;

public sealed class SubMerchantService(AppDbContext db, ILogger<SubMerchantService> logger)
{
    public async Task<PagedResult<SubMerchantResponse>> ListAsync(Guid merchantId, SubMerchantQuery q, CancellationToken ct)
    {
        await EnsureMerchantAsync(merchantId, ct);

        var query = db.SubMerchants.AsNoTracking().Where(s => s.MerchantId == merchantId);
        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = LikePattern.Contains(q.Search);
            query = query.Where(s =>
                EF.Functions.Like(s.Name, pattern, LikePattern.Escape) ||
                EF.Functions.Like(s.ExternalReference, pattern, LikePattern.Escape));
        }
        if (q.Status is { } status) query = query.Where(s => s.Status == status);

        var total = await query.CountAsync(ct);
        var items = await Order(query, q.SortBy, q.Desc).ThenBy(s => s.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(s => new SubMerchantResponse(s.Id, s.MerchantId, s.Name, s.ExternalReference, s.Status, s.CreatedAt, s.UpdatedAt))
            .ToListAsync(ct);

        return new PagedResult<SubMerchantResponse>(items, q.Page, q.PageSize, total);
    }

    public async Task<SubMerchantResponse> GetAsync(Guid merchantId, Guid id, CancellationToken ct)
    {
        var sub = await db.SubMerchants.AsNoTracking()
                      .FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId, ct)
                  ?? throw new NotFoundException("Sub-merchant not found.");
        return ToResponse(sub);
    }

    public async Task<SubMerchantResponse> CreateAsync(Guid merchantId, CreateSubMerchantRequest r, CancellationToken ct)
    {
        var parent = await db.Merchants.AsNoTracking().Where(m => m.Id == merchantId)
                         .Select(m => new { m.Status }).FirstOrDefaultAsync(ct)
                     ?? throw new NotFoundException("Merchant not found.");

        if (parent.Status is MerchantStatus.Suspended or MerchantStatus.Closed)
            throw new ConflictException($"Cannot add sub-merchants to a {parent.Status.ToString().ToLowerInvariant()} merchant.");

        if (await db.SubMerchants.AnyAsync(s => s.MerchantId == merchantId && s.ExternalReference == r.ExternalReference, ct))
            throw new ConflictException("This merchant already has a sub-merchant with that external reference.");

        var now = DateTime.UtcNow;
        var sub = new SubMerchant
        {
            Id = Guid.NewGuid(), MerchantId = merchantId, Name = r.Name.Trim(),
            ExternalReference = r.ExternalReference, Status = MerchantStatus.Pending, CreatedAt = now, UpdatedAt = now
        };
        db.SubMerchants.Add(sub);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Sub-merchant {SubMerchantId} created under {MerchantId}", sub.Id, merchantId);
        return ToResponse(sub);
    }

    public async Task<SubMerchantResponse> UpdateAsync(Guid merchantId, Guid id, UpdateSubMerchantRequest r, CancellationToken ct)
    {
        var sub = await db.SubMerchants.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId, ct)
                  ?? throw new NotFoundException("Sub-merchant not found.");
        var newStatus = r.Status!.Value;

        if (sub.Status == MerchantStatus.Closed && newStatus != MerchantStatus.Closed)
            throw new ConflictException("A closed sub-merchant cannot be reopened.");

        sub.Name = r.Name.Trim();
        sub.Status = newStatus;
        sub.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return ToResponse(sub);
    }

    public async Task DeleteAsync(Guid merchantId, Guid id, CancellationToken ct)
    {
        var sub = await db.SubMerchants.FirstOrDefaultAsync(s => s.Id == id && s.MerchantId == merchantId, ct)
                  ?? throw new NotFoundException("Sub-merchant not found.");
        db.SubMerchants.Remove(sub);
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Sub-merchant {SubMerchantId} deleted", id);
    }

    private async Task EnsureMerchantAsync(Guid merchantId, CancellationToken ct)
    {
        if (!await db.Merchants.AnyAsync(m => m.Id == merchantId, ct))
            throw new NotFoundException("Merchant not found.");
    }

    private static IOrderedQueryable<SubMerchant> Order(IQueryable<SubMerchant> q, string sortBy, bool desc) =>
        (sortBy, desc) switch
        {
            ("name", false) => q.OrderBy(s => s.Name),
            ("name", true) => q.OrderByDescending(s => s.Name),
            ("status", false) => q.OrderBy(s => s.Status),
            ("status", true) => q.OrderByDescending(s => s.Status),
            (_, false) => q.OrderBy(s => s.CreatedAt),
            _ => q.OrderByDescending(s => s.CreatedAt)
        };

    private static SubMerchantResponse ToResponse(SubMerchant s) =>
        new(s.Id, s.MerchantId, s.Name, s.ExternalReference, s.Status, s.CreatedAt, s.UpdatedAt);
}
