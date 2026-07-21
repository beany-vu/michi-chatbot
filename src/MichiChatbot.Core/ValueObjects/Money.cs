namespace MichiChatbot.Core.ValueObjects;

/// <summary>
/// A small value object for a price: an amount plus its currency (e.g. 29.00 "USD").
/// A `record` gives value-equality (two Moneys with the same amount+currency are equal),
/// which is exactly what a value object wants. Stored as jsonb on `plans.Price`.
/// </summary>
public record Money(decimal Amount, string Currency);
