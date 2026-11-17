namespace SmartSupport.Domain.Common;

/// <summary>
/// Thrown when a business rule is broken (e.g. changing plan while a bill is overdue).
/// The API turns it into an HTTP 400 response, and the AI agent turns it into a
/// friendly explanation for the customer.
/// </summary>
public class DomainException(string message) : Exception(message);
