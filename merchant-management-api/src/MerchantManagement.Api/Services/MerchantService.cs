using MerchantManagement.Api.Contracts;
using MerchantManagement.Api.Data;
using MerchantManagement.Api.Domain;
using MerchantManagement.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace MerchantManagement.Api.Services;

public sealed class MerchantService(AppDbContext db, ILogger<MerchantService> logger)
{
    public async Task<PagedResult<MerchantResponse>> ListAsync(MerchantQuery q, CancellationToken ct)
    {
        var query = db.Merchants.AsNoTracking();

        if (!string.IsNullOrWhiteSpace(q.Search))
        {
            var pattern = LikePattern.Contains(q.Search);
            query = query.Where(m =>
                EF.Functions.Like(m.Name, pattern, LikePattern.Escape) ||
                EF.Functions.Like(m.LegalName, pattern, LikePattern.Escape) ||
                EF.Functions.Like(m.Email, pattern, LikePattern.Escape));
        }
        if (q.Status is { } status) query = query.Where(m => m.Status == status);

        var total = await query.CountAsync(ct);

        // Id as a tie-breaker keeps paging stable when the sort column has duplicates.
        var items = await Order(query, q.SortBy, q.Desc).ThenBy(m => m.Id)
            .Skip((q.Page - 1) * q.PageSize).Take(q.PageSize)
            .Select(m => new MerchantResponse(m.Id, m.Name, m.LegalName, m.Email, m.CountryCode,
                m.Status, m.CreatedAt, m.UpdatedAt, m.SubMerchants.Count))
            .ToListAsync(ct);

        return new PagedResult<MerchantResponse>(items, q.Page, q.PageSize, total);
    }

    public async Task<MerchantResponse> GetAsync(Guid id, CancellationToken ct) =>
        await db.Merchants.AsNoTracking().Where(m => m.Id == id)
            .Select(m => new MerchantResponse(m.Id, m.Name, m.LegalName, m.Email, m.CountryCode,
                m.Status, m.CreatedAt, m.UpdatedAt, m.SubMerchants.Count))
            .FirstOrDefaultAsync(ct)
        ?? throw new NotFoundException("Merchant not found.");

    public async Task<MerchantResponse> CreateAsync(CreateMerchantRequest r, CancellationToken ct)
    {
        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Merchants.AnyAsync(m => m.Email == email, ct))
            throw new ConflictException("A merchant with this email already exists.");

        var now = DateTime.UtcNow;
        var merchant = new Merchant
        {
            Id = Guid.NewGuid(), Name = r.Name.Trim(), LegalName = r.LegalName.Trim(), Email = email,
            CountryCode = r.CountryCode, Status = MerchantStatus.Pending, CreatedAt = now, UpdatedAt = now
        };
        db.Merchants.Add(merchant);
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Merchant {MerchantId} created", merchant.Id);
        return ToResponse(merchant, 0);
    }

    public async Task<MerchantResponse> UpdateAsync(Guid id, UpdateMerchantRequest r, CancellationToken ct)
    {
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == id, ct)
                       ?? throw new NotFoundException("Merchant not found.");
        var newStatus = r.Status!.Value;

        if (merchant.Status == MerchantStatus.Closed && newStatus != MerchantStatus.Closed)
            throw new ConflictException("A closed merchant cannot be reopened.");

        var email = r.Email.Trim().ToLowerInvariant();
        if (await db.Merchants.AnyAsync(m => m.Email == email && m.Id != id, ct))
            throw new ConflictException("A merchant with this email already exists.");

        merchant.Name = r.Name.Trim();
        merchant.LegalName = r.LegalName.Trim();
        merchant.Email = email;
        merchant.CountryCode = r.CountryCode;
        merchant.Status = newStatus;
        merchant.UpdatedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);

        logger.LogInformation("Merchant {MerchantId} updated, status {Status}", id, newStatus);
        var count = await db.SubMerchants.CountAsync(s => s.MerchantId == id, ct);
        return ToResponse(merchant, count);
    }

    public async Task DeleteAsync(Guid id, CancellationToken ct)
    {
        var merchant = await db.Merchants.FirstOrDefaultAsync(m => m.Id == id, ct)
                       ?? throw new NotFoundException("Merchant not found.");
        db.Merchants.Remove(merchant); // sub-merchants removed by ON DELETE CASCADE
        await db.SaveChangesAsync(ct);
        logger.LogInformation("Merchant {MerchantId} deleted", id);
    }

    private static IOrderedQueryable<Merchant> Order(IQueryable<Merchant> q, string sortBy, bool desc) =>
        (sortBy, desc) switch
        {
            ("name", false) => q.OrderBy(m => m.Name),
            ("name", true) => q.OrderByDescending(m => m.Name),
            ("email", false) => q.OrderBy(m => m.Email),
            ("email", true) => q.OrderByDescending(m => m.Email),
            ("status", false) => q.OrderBy(m => m.Status),
            ("status", true) => q.OrderByDescending(m => m.Status),
            (_, false) => q.OrderBy(m => m.CreatedAt),
            _ => q.OrderByDescending(m => m.CreatedAt)
        };

    private static MerchantResponse ToResponse(Merchant m, int subCount) =>
        new(m.Id, m.Name, m.LegalName, m.Email, m.CountryCode, m.Status, m.CreatedAt, m.UpdatedAt, subCount);
}

/// <summary>Builds LIKE patterns with user wildcards (%, _) escaped so search text is always literal.</summary>
internal static class LikePattern
{
    public const string Escape = "\\";

    public static string Contains(string term) =>
        "%" + term.Trim().Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_") + "%";
}
