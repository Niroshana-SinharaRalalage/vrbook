using MediatR;
using VrBook.Contracts.Dtos;

namespace VrBook.Modules.Pricing.Application.Quotes.Commands;

public sealed record ComputeQuoteCommand(Guid PropertyId, QuoteRequest Request) : IRequest<QuoteDto>;
