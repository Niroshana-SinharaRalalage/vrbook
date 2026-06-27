using MediatR;
using VrBook.Contracts.Dtos;
using VrBook.Contracts.Interfaces;

namespace VrBook.Modules.Pricing.Application.Plans.Commands;

public sealed record UpdatePricingPlanCommand(Guid PropertyId, UpdatePricingPlanRequest Request, Guid TenantId)
    : IRequest<PricingPlanDto>, ITenantScoped;
