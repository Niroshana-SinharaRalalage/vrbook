namespace VrBook.Domain.Common;

/// <summary>
/// A guard predicate evaluated inside an aggregate. Convention: the rule name
/// (the type's full name) shows up in <see cref="BusinessRuleViolationException.Rule"/>.
/// </summary>
public interface IBusinessRule
{
    bool IsBroken();
    string Message { get; }
}

public static class BusinessRuleExtensions
{
    /// <summary>Throws <see cref="BusinessRuleViolationException"/> if the rule is broken.</summary>
    public static void Check(this IBusinessRule rule)
    {
        if (rule.IsBroken())
        {
            throw new BusinessRuleViolationException(rule.GetType().Name, rule.Message);
        }
    }
}
