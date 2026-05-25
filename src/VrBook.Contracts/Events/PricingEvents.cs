namespace VrBook.Contracts.Events;

public sealed record PricingPlanUpdated(Guid PricingPlanId, Guid PropertyId) : DomainEvent;

public sealed record PricingRuleAdded(Guid PricingPlanId, Guid RuleId) : DomainEvent;

public sealed record PricingRuleRemoved(Guid PricingPlanId, Guid RuleId) : DomainEvent;
