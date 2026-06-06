using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Pricing.Application.Plans.Queries;

public sealed record GetPricingPlanQuery(Guid PropertyId) : IRequest<PricingPlanDto?>;
