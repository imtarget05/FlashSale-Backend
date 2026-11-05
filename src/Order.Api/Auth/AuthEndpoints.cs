using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Order.Api.Auth;

public static class AuthEndpoints
{
    public static IEndpointRouteBuilder MapAuthEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/auth");
        group.MapPost("/register", () => Results.Ok());
        group.MapPost("/login", () => Results.Ok());
        group.MapPost("/refresh", () => Results.Ok());
        group.MapPost("/bootstrap", () => Results.Ok());
        return app;
    }
}
