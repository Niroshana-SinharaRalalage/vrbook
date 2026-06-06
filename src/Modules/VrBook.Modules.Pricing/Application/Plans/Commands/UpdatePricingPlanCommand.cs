using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record UpdatePricingPlanCommand(Guid PropertyId, UpdatePricingPlanRequest Request) : IRequest<PricingPlanDto>;
