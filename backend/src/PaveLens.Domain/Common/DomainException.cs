namespace PaveLens.Domain.Common;

/// <summary>Raised when an operation would break a domain invariant.</summary>
public sealed class DomainException(string message) : Exception(message);
