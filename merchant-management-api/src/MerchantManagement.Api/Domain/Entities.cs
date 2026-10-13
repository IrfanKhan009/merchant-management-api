namespace MerchantManagement.Api.Domain;

public static class Roles
{
    public const string Admin = "Admin";
    public const string Manager = "Manager";
    public const string Viewer = "Viewer";
    public static readonly string[] All = [Admin, Manager, Viewer];
}

public enum MerchantStatus { Pending, Active, Suspended, Closed }

public class User
{
    public Guid Id { get; set; }
    public string Email { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public string Role { get; set; } = Roles.Viewer;
    public bool IsActive { get; set; } = true;
    public DateTime CreatedAt { get; set; }
}

public class Merchant
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string LegalName { get; set; } = "";
    public string Email { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public MerchantStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<SubMerchant> SubMerchants { get; set; } = [];
}

public class SubMerchant
{
    public Guid Id { get; set; }
    public Guid MerchantId { get; set; }
    public Merchant? Merchant { get; set; }
    public string Name { get; set; } = "";
    public string ExternalReference { get; set; } = "";
    public MerchantStatus Status { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
