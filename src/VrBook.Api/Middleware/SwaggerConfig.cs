using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace VrBook.Api.Middleware;

public static class SwaggerConfig
{
    public static IServiceCollection AddSwaggerConfigured(this IServiceCollection services)
    {
        services.AddSwaggerGen(opts =>
        {
            opts.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "VrBook API",
                Version = "v1",
                Description = "Direct-Booking Vacation Rental Platform — Phase 1 API. " +
                              "See /BookingApp_Proposal.md §6 for conventions.",
                Contact = new OpenApiContact { Name = "Solutions Architecture" },
            });

            opts.EnableAnnotations();
            opts.SupportNonNullableReferenceTypes();
            opts.UseInlineDefinitionsForEnums();

            opts.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
            {
                Name = "Authorization",
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "AD B2C bearer token. Set `Authorization: Bearer {token}`.",
            });

            opts.AddSecurityRequirement(new OpenApiSecurityRequirement
            {
                {
                    new OpenApiSecurityScheme
                    {
                        Reference = new OpenApiReference
                        {
                            Type = ReferenceType.SecurityScheme,
                            Id = "Bearer",
                        },
                    },
                    Array.Empty<string>()
                },
            });
        });

        return services;
    }
}
