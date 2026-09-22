using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Order.Api;

/// <summary>
/// Times every request and records it against the route <em>template</em>.
/// </summary>
public static class HttpMetricsMiddleware
{
    public static IApplicationBuilder UseFlashSaleMetrics(this IApplicationBuilder app) =>
        app.Use(async (context, next) =>
        {
            var metrics = context.RequestServices.GetRequiredService<ApiMetrics>();
            metrics.RequestStarted();

            var start = Stopwatch.GetTimestamp();
            try
            {
                await next();
            }
            finally
            {
                // finally, not a plain call at the end: a request that throws must
                // still decrement the in-flight gauge, or the "saturation" number
                // would only ever climb.
                metrics.RequestFinished();

                ApiMetrics.RequestDuration.Record(
                    Stopwatch.GetElapsedTime(start).TotalMilliseconds,
                    new KeyValuePair<string, object?>("method", context.Request.Method),
                    new KeyValuePair<string, object?>("route", RouteTemplateOf(context)));
            }
        });

    /// <summary>
    /// The route TEMPLATE (e.g. <c>/api/orders/{idempotencyKey}</c>) is what keeps
    /// cardinality bounded. Tagging the raw path would mint one series per order id,
    /// which is the classic way a metrics endpoint becomes the outage.
    /// </summary>
    private static string RouteTemplateOf(HttpContext context)
    {
        if (context.GetEndpoint() is RouteEndpoint endpoint &&
            endpoint.RoutePattern.RawText is { Length: > 0 } raw)
        {
            return "/" + raw.TrimStart('/');
        }

        // No endpoint: a 404, or a request rejected before routing matched.
        return "unmatched";
    }
}