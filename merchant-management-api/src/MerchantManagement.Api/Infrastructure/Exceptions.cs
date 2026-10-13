namespace MerchantManagement.Api.Infrastructure;

/// <summary>Thrown by services; mapped to 404 by <see cref="GlobalExceptionHandler"/>.</summary>
public sealed class NotFoundException(string message) : Exception(message);

/// <summary>Thrown for business-rule or uniqueness violations; mapped to 409.</summary>
public sealed class ConflictException(string message) : Exception(message);
