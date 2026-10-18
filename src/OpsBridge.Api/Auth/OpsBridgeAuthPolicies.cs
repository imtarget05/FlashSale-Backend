using Microsoft.AspNetCore.Authorization;
using OpsBridge.Domain;

namespace OpsBridge.Api.Auth;

public static class OpsBridgeAuthPolicies
{
    public const string AdminOnly = "AdminOnly";
    public const string OperatorOrAdmin = "OperatorOrAdmin";
    public const string AuditorOrAbove = "AuditorOrAbove";

    public static void AddOpsBridgePolicies(this AuthorizationOptions options)
    {
        options.AddPolicy(AdminOnly, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(OpsBridgeRoles.Admin));

        options.AddPolicy(OperatorOrAdmin, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(OpsBridgeRoles.Admin, OpsBridgeRoles.Operator));

        options.AddPolicy(AuditorOrAbove, policy => policy
            .RequireAuthenticatedUser()
            .RequireRole(OpsBridgeRoles.Admin, OpsBridgeRoles.Operator, OpsBridgeRoles.Auditor));
    }
}
