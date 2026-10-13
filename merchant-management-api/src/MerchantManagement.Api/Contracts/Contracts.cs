using System.ComponentModel.DataAnnotations;
using MerchantManagement.Api.Domain;

namespace MerchantManagement.Api.Contracts;

// ---------- Paging ----------
public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Page, int PageSize, int TotalCount)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

// ---------- Auth / users ----------
public sealed class LoginRequest
{
    [Required, StringLength(254)] public string Email { get; init; } = "";
    [Required, StringLength(128)] public string Password { get; init; } = "";
}

public sealed record LoginResponse(string AccessToken, string TokenType, DateTime ExpiresAtUtc, string Role);

public sealed class CreateUserRequest
{
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";

    [Required, StringLength(128, MinimumLength = 12, ErrorMessage = "Password must be 12-128 characters.")]
    public string Password { get; init; } = "";

    [Required, RegularExpression("^(Admin|Manager|Viewer)$", ErrorMessage = "Role must be Admin, Manager or Viewer.")]
    public string Role { get; init; } = "";
}

public sealed record UserResponse(Guid Id, string Email, string Role, bool IsActive, DateTime CreatedAt);

// ---------- Merchants ----------
public class CreateMerchantRequest
{
    [Required, StringLength(200, MinimumLength = 2)] public string Name { get; init; } = "";
    [Required, StringLength(200, MinimumLength = 2)] public string LegalName { get; init; } = "";
    [Required, EmailAddress, StringLength(254)] public string Email { get; init; } = "";

    [Required, RegularExpression("^[A-Z]{2}$", ErrorMessage = "CountryCode must be an upper-case ISO 3166-1 alpha-2 code, e.g. PK.")]
    public string CountryCode { get; init; } = "";
}

public sealed class UpdateMerchantRequest : CreateMerchantRequest
{
    [Required, EnumDataType(typeof(MerchantStatus))] public MerchantStatus? Status { get; init; }
}

public sealed record MerchantResponse(
    Guid Id, string Name, string LegalName, string Email, string CountryCode,
    MerchantStatus Status, DateTime CreatedAt, DateTime UpdatedAt, int SubMerchantCount);

public sealed class MerchantQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(100)] public string? Search { get; init; }
    public MerchantStatus? Status { get; init; }

    [RegularExpression("^(name|email|status|createdAt)$", ErrorMessage = "SortBy must be one of: name, email, status, createdAt.")]
    public string SortBy { get; init; } = "createdAt";

    public bool Desc { get; init; }
}

// ---------- Sub-merchants ----------
public sealed class CreateSubMerchantRequest
{
    [Required, StringLength(200, MinimumLength = 2)] public string Name { get; init; } = "";

    [Required, RegularExpression("^[A-Za-z0-9_-]{1,64}$", ErrorMessage = "ExternalReference may contain letters, digits, '_' and '-' (max 64).")]
    public string ExternalReference { get; init; } = "";
}

public sealed class UpdateSubMerchantRequest
{
    [Required, StringLength(200, MinimumLength = 2)] public string Name { get; init; } = "";
    [Required, EnumDataType(typeof(MerchantStatus))] public MerchantStatus? Status { get; init; }
}

public sealed record SubMerchantResponse(
    Guid Id, Guid MerchantId, string Name, string ExternalReference,
    MerchantStatus Status, DateTime CreatedAt, DateTime UpdatedAt);

public sealed class SubMerchantQuery
{
    [Range(1, 1_000_000)] public int Page { get; init; } = 1;
    [Range(1, 100)] public int PageSize { get; init; } = 20;
    [StringLength(100)] public string? Search { get; init; }
    public MerchantStatus? Status { get; init; }

    [RegularExpression("^(name|status|createdAt)$", ErrorMessage = "SortBy must be one of: name, status, createdAt.")]
    public string SortBy { get; init; } = "createdAt";

    public bool Desc { get; init; }
}
