using FlashSale.Application.Assistant;
using FlashSale.Application.Auth;
using FlashSale.Application.Automation;
using FlashSale.Application.Content;
using FlashSale.Application.Events;
using FlashSale.Application.Payment;
using FlashSale.Application.Saga;
using FlashSale.Application.Support;
using FlashSale.Application.Inventory;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Application.Persistence;
using FlashSale.Domain;
using FlashSale.Domain.Automation;
using FlashSale.Domain.Entities;
using FlashSale.Domain.Outbox;
using FlashSale.Domain.Saga;
using FlashSale.Application.Outbox;
using FlashSale.Infrastructure.Auth;
using FlashSale.Infrastructure.Ai;
using FlashSale.Infrastructure.Events;
using FlashSale.Infrastructure.Messaging;
using FlashSale.Infrastructure.Payment;
using FlashSale.Infrastructure.Persistence;
using FlashSale.Infrastructure.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Order.Api;
using Order.Api.Auth;
using Order.Api.OpenApi;
using StackExchange.Redis;
using System.Security.Claims;
using System.Text;
using System.Text.Json;

var builder = WebApplication.CreateBuilder(args);

// ---------------------------------------------------------------
// Composition root: PostgreSQL stays the source of truth; Redis is a
// fast pre-filter; the queue decouples acceptance from fulfillment.
// ---------------------------------------------------------------
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "Host=localhost;Port=5432;Database=FlashSaleDb;Username=postgres;Password=postgres;Maximum Pool Size=80";

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));
builder.Services.AddScoped<IOrderRepository, OrderRepository>();
builder.Services.AddScoped<IOrderReadModel, OrderReadModel>();
builder.Services.AddScoped<IDatabaseHealthCheck, DatabaseHealthCheck>();

// Business automation platform (spec §1): one domain-event publisher — RabbitMQ
// topic exchange when Messaging:Provider=RabbitMQ, in-memory otherwise.
builder.Services.AddDomainEventPublisher(builder.Configuration);

// Payment automation (spec §4/§5): config-bound options (never hard-coded),
// guarded payment transitions, timeout scan + manual triggers.
var paymentOptions = builder.Configuration
    .GetSection(PaymentAutomationOptions.SectionName).Get<PaymentAutomationOptions>()
    ?? new PaymentAutomationOptions();
builder.Services.AddSingleton(paymentOptions);
builder.Services.AddScoped<IAutomationRunRepository, AutomationRunRepository>();
builder.Services.AddScoped<IPaymentRepository, PaymentRepository>();
builder.Services.AddScoped<PaymentAutomationUseCase>();
builder.Services.AddScoped<RecordPaymentUseCase>();

// Checkout Saga Orchestration (Phases 9 & 11)
builder.Services.AddScoped<ICheckoutSagaRepository, CheckoutSagaRepository>();
builder.Services.AddHttpClient<IPaymentClient, HttpPaymentClient>();
builder.Services.AddScoped<CheckoutSagaCoordinator>();

// Transactional Outbox & Deduplication Inbox (Phase 10)
builder.Services.AddTransactionalOutbox();

// Inventory automation (spec §6): low-stock alerts, threshold config-bound.
var inventoryOptions = builder.Configuration
    .GetSection(InventoryAutomationOptions.SectionName).Get<InventoryAutomationOptions>()
    ?? new InventoryAutomationOptions();
builder.Services.AddSingleton(inventoryOptions);
builder.Services.AddScoped<IStockAlertRepository, StockAlertRepository>();
builder.Services.AddScoped<LowStockAlertUseCase>();

// Reporting automation (spec §7): daily report aggregation + persistence.
var reportingOptions = builder.Configuration
    .GetSection(ReportingAutomationOptions.SectionName).Get<ReportingAutomationOptions>()
    ?? new ReportingAutomationOptions();
builder.Services.AddSingleton(reportingOptions);
builder.Services.AddScoped<IDailyReportRepository, DailyReportRepository>();
builder.Services.AddScoped<DailyReportUseCase>();

// AI content + support triage (spec §8/§9): same Ollama client + rate limiter as
// the product assistant; every AI result is reviewed or grounded by rule.
builder.Services.AddScoped<IContentRepository, ContentRepository>();
builder.Services.AddScoped<ProductContentUseCase>();
builder.Services.AddScoped<ContentReviewUseCase>();
builder.Services.AddScoped<SupportTriageUseCase>();

// ---------------------------------------------------------------
// AI assistant (spec §10) — local Ollama through its OpenAI-compatible API.
// The typed client owns BaseAddress + dummy bearer key (Ollama ignores it;
// OpenAI-compatible wire format requires one); the use case owns timeout,
// bounded retry, grounding validation and the per-user rate limit.
// ---------------------------------------------------------------
var aiOptions = builder.Configuration.GetSection(OllamaOptions.SectionName).Get<OllamaOptions>()
    ?? new OllamaOptions();
builder.Services.AddSingleton(aiOptions);
builder.Services.AddHttpClient<IAiChatClient, OllamaChatClient>("ollama", (_, client) =>
{
    client.BaseAddress = new Uri($"{aiOptions.BaseUrl.TrimEnd('/')}/");
    client.DefaultRequestHeaders.Authorization =
        new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", aiOptions.ApiKey);
    // Must exceed the use case's AttemptTimeout (120s) so the use case owns the
    // timeout/retry budget; the HttpClient default (100s) would otherwise fire
    // first and burn the retry (2 × 100s → user-visible 503 at ~200s).
    client.Timeout = TimeSpan.FromSeconds(150);
});
builder.Services.AddScoped<IAiRateLimiter, RedisAiRateLimiter>();
builder.Services.AddScoped<ProductAssistantUseCase>();

// ---------------------------------------------------------------
// Observability (Phase III).
//
// ApiMetrics owns real System.Diagnostics.Metrics instruments; the singleton is
// what starts the MeterListener that aggregates them for the JSON snapshot.
//
// AddOpenApi() costs no new package: Microsoft.AspNetCore.OpenApi already ships
// in the ASP.NET shared framework and is referenced by this project.
// ---------------------------------------------------------------
builder.Services.AddSingleton<ApiMetrics>();
builder.Services.AddOpenApi(options =>
    options.AddDocumentTransformer<BearerSecuritySchemeTransformer>());
// Swagger UI hosting only (interview smoke gate: GET /swagger → 200). The
// document itself still comes from AddOpenApi above — Swashbuckle's generator
// is never asked to produce one, so /openapi/v1.json stays the single contract.
builder.Services.AddSwaggerGen();

// LOCAL v2 distributed tracing. The exporter is opt-in so the migration Job and
// production manifests remain independent of an observability collector.
var otlpEndpoint = builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"];
builder.Services
    .AddOpenTelemetry()
    .ConfigureResource(resource => resource.AddService(
        serviceName: "order-api",
        serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString()))
    .WithTracing(tracing =>
    {
        tracing.AddAspNetCoreInstrumentation()
            .AddHttpClientInstrumentation()
            .AddSource(HttpPaymentClient.ActivitySourceName);
        if (!string.IsNullOrWhiteSpace(otlpEndpoint))
        {
            tracing.AddOtlpExporter(options => options.Endpoint = new Uri(otlpEndpoint));
        }
    });

// ---------------------------------------------------------------
// Migration entrypoint (Phase 7C): `dotnet Order.Api.dll --migrate`.
//
// Positioned HERE, before Redis and the queue are registered, because a
// migration must depend on PostgreSQL and nothing else. Two reasons this is not
// cosmetic:
//   * ConnectionMultiplexer.Connect() below is eager, so a Redis-less Job would
//     hang/throw before reaching the migration.
//   * AddOrderQueue() is eager too and fails fast on missing broker config, so
//     placing the branch after it would make the migration Job unrunnable in
//     exactly the environment it exists for.
// infrastructure/kubernetes/base/migration/job.yaml is the only intended caller;
// the API and worker keep calling InitializeAsync for single-process local runs.
// ---------------------------------------------------------------
if (args.Contains("--migrate"))
{
    // ASP0000 is intentional: the migration path must run before the app (and
    // therefore before the queue/Redis singletons) exists, so a throwaway
    // provider is the only way to obtain a logger.
#pragma warning disable ASP0000
    await using var migrationProvider = builder.Services.BuildServiceProvider();
#pragma warning restore ASP0000
    var migrationLogger = migrationProvider
        .GetRequiredService<ILoggerFactory>()
        .CreateLogger("Migration");

    var exitCode = await MigrationRunner.RunAsync(migrationProvider, migrationLogger);
    migrationLogger.LogInformation("Migration entrypoint finished with exit code {ExitCode}", exitCode);
    return exitCode;
}

// ---------------------------------------------------------------
// Authentication & authorization (ADR-013).
//
// Registered AFTER the --migrate branch, and that ordering is load-bearing:
// JwtOptions.FromConfiguration() fails fast when a signing key is missing, and
// the whole point of the migration Job is to depend on PostgreSQL and nothing
// else. Above the branch, a key-less production Job would refuse to start.
// ---------------------------------------------------------------
var jwtOptions = JwtOptions.FromConfiguration(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddSingleton(jwtOptions);
builder.Services.AddSingleton<ITokenIssuer>(new JwtTokenIssuer(jwtOptions));
builder.Services.AddSingleton<IPasswordHasher>(new Pbkdf2PasswordHasher());
builder.Services.AddScoped<IUserStore, UserStore>();
builder.Services.AddScoped<AuthService>();

builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtOptions.Issuer,
            ValidateAudience = true,
            ValidAudience = jwtOptions.Audience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
            RoleClaimType = ClaimTypes.Role,
            NameClaimType = ClaimTypes.NameIdentifier,
        };
    });

builder.Services.AddAuthorizationBuilder().AddFlashSalePolicies();

// Redis: enabled when reachable, transparently skipped otherwise.
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "localhost:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<IStockReservationGateway>(sp =>
    new RedisStockGateway(sp.GetRequiredService<IConnectionMultiplexer>()));

// Queue selection: exactly one provider from configuration (ADR-005).
builder.Services.AddOrderQueue(builder.Configuration, builder.Environment.EnvironmentName);

builder.Services.AddScoped<OrderProcessor>();
builder.Services.AddScoped<SubmitOrderUseCase>();
builder.Services.AddHostedService<OrderProcessorHost>();

var app = builder.Build();

// Outermost user middleware: it must see every request — including ones
// rejected by authentication — so the in-flight gauge and the duration
// histogram describe the whole surface, not just the authorized part.
app.UseFlashSaleMetrics();

// Order matters: authentication must populate HttpContext.User before
// authorization evaluates the policies registered above.
app.UseAuthentication();
app.UseAuthorization();

// Startup concerns live in Infrastructure (schema + seed + Redis mirroring).
await DatabaseInitializer.InitializeAsync(app.Services);

app.MapAuthEndpoints();

// OpenAPI document (Phase III). Served as JSON at /openapi/v1.json, which is a
// stable, tool-agnostic contract: any client that can read a URL can generate a
// typed client or render docs from it.
app.MapOpenApi();

// Interactive UI over the SAME document (GET /swagger → 200). The endpoint
// points the UI at /openapi/v1.json, whose Bearer security scheme (added by
// BearerSecuritySchemeTransformer) is what makes the Authorize button work —
// verifying these two gates independently, as the smoke checklist requires.
app.UseSwaggerUI(options =>
    options.SwaggerEndpoint("/openapi/v1.json", "FlashSale API v1"));

// Metrics snapshot (Phase III). Deliberately a JSON snapshot rather than a
// Prometheus exporter: the interview surface needs a dependency-free view of
// what THIS process observed, and the instruments behind it are real Meter
// instruments, so an OTel exporter can be attached later without a rewrite.
app.MapGet("/internal/metrics", (ApiMetrics metrics) => Results.Ok(metrics.Snapshot()))
    .WithTags("Ops")
    .WithName("MetricsSnapshot")
    .WithSummary("In-process metrics snapshot as JSON (counters + duration histograms).");

// Automation dashboard (spec §14). DB-only roll-up of today's audit rows, so
// this endpoint stays read-only and dependency-free: no Redis, no queue, no
// background timing — just what AutomationRuns already recorded.
// ---------------------------------------------------------------
app.MapGet("/internal/automation/summary", async (IAutomationRunRepository runs) =>
{
    var summary = await runs.GetSummaryAsync();
    return Results.Ok(summary);
})
    .WithTags("Ops")
    .WithName("AutomationSummary")
    .WithSummary("Today's automation dashboard roll-up (runs by status, avg duration, top failing workflow).");

app.MapGet("/internal/automation/alerts", async (IStockAlertRepository alerts) =>
{
    var open = await alerts.ListOpenAsync(50);
    return Results.Ok(open);
})
    .WithTags("Ops")
    .WithName("OpenAlerts")
    .WithSummary("Open low-stock alerts (spec §6), newest first.");



// ---------------------------------------------------------------
// Thin HTTP endpoints: map routes and delegate to Application ports/use cases.
// No business logic here — only HTTP-to-domain translation.
// ---------------------------------------------------------------

app.MapPost("/api/orders", async (
    OrderRequest request,
    HttpRequest http,
    ClaimsPrincipal user,
    SubmitOrderUseCase useCase) =>
{
    if (request.Quantity <= 0)
    {
        ApiMetrics.OrderRejected("invalid_quantity");
        return Results.BadRequest(new { error = "Quantity must be positive" });
    }

    var idempotencyKey = http.Headers.TryGetValue("Idempotency-Key", out var headerKey)
        ? headerKey.ToString()
        : Guid.NewGuid().ToString("N");

    // ADR-013 §6: anonymous callers keep working (UserId = null); an
    // authenticated caller's order is attributed to the JWT `sub`. This endpoint
    // is deliberately NOT [Authorize] — the anonymous path is existing,
    // verified behaviour and the concurrency evidence depends on it.
    var userId = TryGetUserId(user);

    // Thin endpoint: all business orchestration lives in SubmitOrderUseCase
    // (Clean Architecture); presentation only maps outcomes to HTTP + metrics.
    var result = await useCase.ExecuteAsync(request.ProductId, request.Quantity, idempotencyKey, userId);

    switch (result.Outcome)
    {
        case SubmitOrderOutcome.Accepted:
            ApiMetrics.OrdersAccepted.Add(1);
            return Results.Accepted(
                $"/api/orders/{result.IdempotencyKey}",
                new { message = "Order accepted", idempotencyKey = result.IdempotencyKey, status = "processing" });

        case SubmitOrderOutcome.DuplicateRequest:
            ApiMetrics.OrderRejected("duplicate");
            return Results.Json(new { error = "Duplicate request", idempotencyKey = result.IdempotencyKey }, statusCode: 409);

        case SubmitOrderOutcome.OutOfStock:
            ApiMetrics.OrderRejected("out_of_stock");
            return Results.Json(new { error = "Out of stock" }, statusCode: 409);

        case SubmitOrderOutcome.StockDrift:
            // ADR-002's guard fired: the conditional UPDATE matched zero rows and the
            // order was refused instead of overselling. Counted separately from a Redis
            // refusal because drift means the two tiers disagreed — worth an alert,
            // whereas a Redis "sold out" is the expected outcome of a flash sale.
            ApiMetrics.StockDrift.Add(1);
            ApiMetrics.OrderRejected("stock_drift");
            return Results.Json(new { error = "Out of stock" }, statusCode: 409);

        case SubmitOrderOutcome.ProductNotFound:
            ApiMetrics.OrderRejected("unknown_product");
            return Results.NotFound(new { error = "Product not found" });

        case SubmitOrderOutcome.SystemBusy:
            ApiMetrics.OrderRejected("backpressure");
            return Results.Json(new { error = "System busy, retry shortly" }, statusCode: 503);

        case SubmitOrderOutcome.CompletedSynchronously:
            ApiMetrics.OrdersAccepted.Add(1);
            // Scope note: this counter means "completed by THIS process". Orders that
            // went through the queue are persisted by Order.Worker, which is a separate
            // process with its own meter instance — counting them here would be a guess.
            ApiMetrics.OrdersCompleted.Add(1);
            return Results.Ok(new { message = "Order placed successfully", idempotencyKey = result.IdempotencyKey });

        case SubmitOrderOutcome.InvalidQuantity:
            ApiMetrics.OrderRejected("invalid_quantity");
            return Results.BadRequest(new { error = "Quantity must be positive" });

        default:
            return Results.StatusCode(500);
    }
});

// Status polling: did my (idempotent) order actually persist?
// NOTE (spec §4): this legacy view answers the FULFILLMENT question from row
// existence alone; the payment lifecycle lives in Order.Status and is exposed
// by the automation endpoints below — deliberately not changed here, because
// the v1.0 smoke contract depends on processing|completed wording.
app.MapGet("/api/orders/{idempotencyKey}", async (string idempotencyKey, IOrderReadModel readModel) =>
{
    var order = await readModel.GetOrderStatusAsync(idempotencyKey);
    return order is null
        ? Results.Json(new { idempotencyKey, status = "processing" })
        : Results.Json(new { order.IdempotencyKey, status = "completed", order.OrderId, order.ProductId, order.Quantity });
});

// Simulated payment gateway (spec §4 paid? branch). outcome: completed|failed.
// Guarded transitions make duplicate calls idempotent (second → 409).
app.MapPost("/api/orders/{idempotencyKey}/pay", async (
    string idempotencyKey,
    PaymentRequest request,
    RecordPaymentUseCase useCase,
    CancellationToken ct) =>
{
    var outcome = request.Outcome?.ToLowerInvariant() switch
    {
        "completed" or "paid" => PaymentOutcome.Completed,
        "failed" or "fail" => PaymentOutcome.Failed,
        _ => (PaymentOutcome?)null
    };
    if (outcome is null)
        return Results.BadRequest(new { error = "outcome must be 'completed' or 'failed'" });

    var result = await useCase.ExecuteAsync(idempotencyKey, outcome.Value, ct);
    if (!result.Found) return Results.NotFound(new { error = "Order not found" });

    return outcome == PaymentOutcome.Completed
        ? (result.Transitioned
            ? Results.Ok(new { idempotencyKey, status = "confirmed" })
            : Results.Json(new { error = "Order is not pending payment" }, statusCode: 409))
        : (result.Transitioned
            ? Results.Ok(new { idempotencyKey, status = "payment_failed_recorded" })
            : Results.Json(new { error = "Order is not pending payment" }, statusCode: 409));
})
.WithTags("Automation")
.WithName("RecordPayment")
.WithSummary("Simulated payment gateway: completed → Confirmed, failed → stays PendingPayment (spec §4).");

// Manual payment-timeout scan (spec §5/§17-A): same use case as the timer,
// different trigger_type in the audit record — demo-friendly determinism.
app.MapPost("/internal/automation/payment-timeout-scan", async (
    PaymentAutomationUseCase useCase,
    CancellationToken ct) =>
{
    var result = await useCase.ExecuteAsync("manual", ct);
    return Results.Ok(new { trigger = "manual", result.Scanned, result.Reminded, result.Cancelled });
})
.WithTags("Ops")
.WithName("PaymentTimeoutScan")
.WithSummary("Run the abandoned-payment scan now: reminders + cancel/stock-release (writes an AutomationRun row).");

// Manual low-stock scan (spec §6/§17-B): DB-backed stock values only — the AI
// layer is never the source of truth for inventory.
app.MapPost("/internal/automation/low-stock-scan", async (
    LowStockAlertUseCase useCase,
    CancellationToken ct) =>
{
    var result = await useCase.ExecuteAsync("manual", ct);
    return Results.Ok(new
    {
        trigger = "manual",
        result.Scanned,
        result.AlertsCreated,
        result.ProductIds
    });
})
.WithTags("Ops")
.WithName("LowStockScan")
.WithSummary("Evaluate the low-stock rule for every product and raise deduplicated LOW_STOCK alerts.");

// Daily business report (spec §7/§17-C): aggregates straight from PostgreSQL.
// ?date=YYYY-MM-DD recomputes a past day (upsert — same row, refreshed figures).
app.MapPost("/internal/automation/daily-report", async (
    string? date,
    DailyReportUseCase useCase,
    CancellationToken ct) =>
{
    DateTime? day = null;
    if (!string.IsNullOrWhiteSpace(date))
    {
        if (!DateTime.TryParse(date, out var parsed))
            return Results.BadRequest(new { error = "date must be YYYY-MM-DD" });
        day = parsed;
    }

    var report = await useCase.ExecuteAsync(day, "manual", ct);
    return Results.Ok(report);
})
.WithTags("Ops")
.WithName("RunDailyReport")
.WithSummary("Generate (or refresh) the daily business report from database aggregates.");

app.MapGet("/internal/automation/daily-report/latest", async (
    IDailyReportRepository reports,
    CancellationToken ct) =>
{
    var report = await reports.GetLatestAsync(ct);
    return report is null ? Results.NotFound(new { error = "no report yet" }) : Results.Ok(report);
})
.WithTags("Ops")
.WithName("LatestDailyReport")
.WithSummary("Most recent persisted daily business report.");

// The caller's own order history (ADR-013 §5). Scoped by the JWT `sub`, so a
// caller can only ever see their own orders — no resource-based handler is
// needed because the query itself is the authorization boundary.
app.MapGet("/orders/me", async (ClaimsPrincipal user, IOrderReadModel readModel, CancellationToken ct) =>
{
    var userId = TryGetUserId(user);
    if (userId is null) return Results.Unauthorized();

    var orders = await readModel.GetOrdersByUserAsync(userId.Value, ct);
    return Results.Ok(new { count = orders.Count, orders });
})
.RequireAuthorization()
.WithTags("Orders")
.WithName("MyOrders")
.WithSummary("List the authenticated caller's orders, newest first.");

// Read endpoint for audits, dashboards and the benchmark harness.
app.MapGet("/api/products/{id}", async (int id, IOrderReadModel readModel) =>
{
    var product = await readModel.GetProductAsync(id);
    return product is null ? Results.NotFound() : Results.Ok(product);
});

// AI Feature A — grounded product assistant (spec §10). Authenticated callers
// only (the rate limit and the answer are per-user); the model may only
// recommend products that exist in the read model, and its structured output
// is validated before it ever reaches the response.
app.MapPost("/api/assistant/product", async (
    AssistantQuestion request,
    ClaimsPrincipal user,
    ProductAssistantUseCase assistant,
    CancellationToken ct) =>
{
    var userId = TryGetUserId(user);
    if (userId is null) return Results.Unauthorized();

    var result = await assistant.ExecuteAsync(userId.Value, request.Question, ct);
    ApiMetrics.RecordAiAssistant(result.Outcome.ToString(), result.Model, result.LatencyMs, result.PromptTokens, result.CompletionTokens);

    return result.Outcome switch
    {
        AssistantOutcome.InvalidQuestion => Results.BadRequest(new
        {
            error = "invalid_question",
            maxQuestionLength = ProductAssistantUseCase.MaxQuestionLength
        }),
        AssistantOutcome.RateLimited => Results.Json(
            new { error = "rate_limited", retryAfterSeconds = 60 }, statusCode: 429),
        AssistantOutcome.UpstreamUnavailable => Results.Json(
            new { error = "assistant_unavailable" }, statusCode: 503),
        AssistantOutcome.InvalidModelOutput => Results.Json(
            new { error = "assistant_invalid_output" }, statusCode: 502),
        _ => Results.Ok(new
        {
            answer = result.Answer,
            recommendedProducts = result.RecommendedProducts,
            model = result.Model,
            latencyMs = result.LatencyMs,
            tokens = new { prompt = result.PromptTokens, completion = result.CompletionTokens }
        })
    };
})
.RequireAuthorization()
.WithTags("Assistant")
.WithName("AskProductAssistant")
.WithSummary("Grounded AI product assistant (Ollama/Qwen): recommend only from real product data.");

// ---------------------------------------------------------------
// AI product content (spec §8) — STAFF/ADMIN only. Generation ALWAYS lands in
// ReviewRequired; publish requires an explicit human approval first (spec §13).
// ---------------------------------------------------------------
app.MapPost("/api/products/{id:int}/content/generate", async (
    int id,
    ClaimsPrincipal user,
    ProductContentUseCase useCase,
    CancellationToken ct) =>
{
    var staffId = TryGetUserId(user);
    if (staffId is null) return Results.Unauthorized();

    var result = await useCase.GenerateAsync(staffId.Value, id, ct);
    return result.Outcome switch
    {
        ContentGenerationOutcome.ProductNotFound => Results.NotFound(new { error = "Product not found" }),
        ContentGenerationOutcome.RateLimited => Results.Json(
            new { error = "rate_limited", retryAfterSeconds = 60 }, statusCode: 429),
        ContentGenerationOutcome.UpstreamUnavailable => Results.Json(
            new { error = "assistant_unavailable" }, statusCode: 503),
        ContentGenerationOutcome.InvalidModelOutput => Results.Json(
            new { error = "assistant_invalid_output" }, statusCode: 502),
        _ => Results.Ok(new
        {
            draftId = result.Draft!.Id,
            status = result.Draft.Status.ToString(),
            result.Draft.ShortDescription,
            result.Draft.SeoDescription,
            keywords = JsonSerializer.Deserialize<List<string>>(result.Draft.KeywordsJson),
            result.Draft.SocialCaption,
            faq = JsonSerializer.Deserialize<JsonElement>(result.Draft.FaqJson),
            model = result.Model,
            latencyMs = result.LatencyMs,
            tokens = new { prompt = result.PromptTokens, completion = result.CompletionTokens }
        })
    };
})
.RequireAuthorization(AuthPolicies.StaffOrAdmin)
.WithTags("Content")
.WithName("GenerateProductContent")
.WithSummary("Generate AI product content as a draft awaiting human review (never auto-published).");

app.MapGet("/api/content/review-queue", async (ContentReviewUseCase useCase, CancellationToken ct) =>
{
    var queue = await useCase.ReviewQueueAsync(ct);
    return Results.Ok(new { count = queue.Count, drafts = queue });
})
.RequireAuthorization(AuthPolicies.StaffOrAdmin)
.WithTags("Content")
.WithName("ContentReviewQueue")
.WithSummary("Drafts waiting for a human decision (spec §13).");

app.MapPost("/api/content/{draftId:int}/approve", async (
    int draftId, ClaimsPrincipal user, ContentReviewUseCase useCase, CancellationToken ct) =>
{
    var reviewerId = TryGetUserId(user);
    if (reviewerId is null) return Results.Unauthorized();

    var result = await useCase.ApproveAsync(reviewerId.Value, draftId, ct);
    if (!result.Found) return Results.NotFound(new { error = "Draft not found" });
    return result.Transitioned
        ? Results.Ok(new { draftId, status = result.Status.ToString() })
        : Results.Json(new { error = "Draft is not awaiting review", status = result.Status.ToString() }, statusCode: 409);
})
.RequireAuthorization(AuthPolicies.StaffOrAdmin)
.WithTags("Content")
.WithName("ApproveContent")
.WithSummary("Human approval: ReviewRequired → Approved (publishing is a separate call).");

app.MapPost("/api/content/{draftId:int}/reject", async (
    int draftId, ContentRejectRequest request, ClaimsPrincipal user,
    ContentReviewUseCase useCase, CancellationToken ct) =>
{
    var reviewerId = TryGetUserId(user);
    if (reviewerId is null) return Results.Unauthorized();
    if (string.IsNullOrWhiteSpace(request.Reason))
        return Results.BadRequest(new { error = "reason is required" });

    var result = await useCase.RejectAsync(reviewerId.Value, draftId, request.Reason.Trim(), ct);
    if (!result.Found) return Results.NotFound(new { error = "Draft not found" });
    return result.Transitioned
        ? Results.Ok(new { draftId, status = result.Status.ToString() })
        : Results.Json(new { error = "Draft cannot be rejected from its state", status = result.Status.ToString() }, statusCode: 409);
})
.RequireAuthorization(AuthPolicies.StaffOrAdmin)
.WithTags("Content")
.WithName("RejectContent")
.WithSummary("Human rejection with a reason (regeneration stays possible).");

app.MapPost("/api/content/{draftId:int}/publish", async (
    int draftId, ContentReviewUseCase useCase, CancellationToken ct) =>
{
    var result = await useCase.PublishAsync(draftId, ct);
    if (!result.Found) return Results.NotFound(new { error = "Draft not found" });
    return result.Transitioned
        ? Results.Ok(new { draftId, status = result.Status.ToString() })
        : Results.Json(new { error = "Draft is not approved — publish requires human approval", status = result.Status.ToString() }, statusCode: 409);
})
.RequireAuthorization(AuthPolicies.StaffOrAdmin)
.WithTags("Content")
.WithName("PublishContent")
.WithSummary("Apply approved content to the product (Approved → Published only).");

// Support triage (spec §9) — any authenticated caller; drafts are grounded in
// real order data and sensitive categories are flagged for human review.
app.MapPost("/api/support/triage", async (
    SupportTriageRequest request, ClaimsPrincipal user, SupportTriageUseCase useCase, CancellationToken ct) =>
{
    var userId = TryGetUserId(user);
    if (userId is null) return Results.Unauthorized();

    var result = await useCase.ExecuteAsync(userId.Value, request.Message, request.OrderKey, ct);
    return result.Outcome switch
    {
        SupportTriageOutcome.InvalidMessage => Results.BadRequest(new
        {
            error = "message_required",
            maxMessageLength = SupportTriageUseCase.MaxMessageLength
        }),
        SupportTriageOutcome.RateLimited => Results.Json(
            new { error = "rate_limited", retryAfterSeconds = 60 }, statusCode: 429),
        SupportTriageOutcome.UpstreamUnavailable => Results.Json(
            new { error = "assistant_unavailable" }, statusCode: 503),
        SupportTriageOutcome.InvalidModelOutput => Results.Json(
            new { error = "assistant_invalid_output" }, statusCode: 502),
        _ => Results.Ok(new
        {
            category = result.Category.ToString().ToUpperInvariant(),
            draftResponse = result.DraftResponse,
            requiresHumanReview = result.RequiresHumanReview,
            groundedFacts = result.GroundedFacts,
            model = result.Model,
            latencyMs = result.LatencyMs,
            tokens = new { prompt = result.PromptTokens, completion = result.CompletionTokens }
        })
    };
})
.RequireAuthorization()
.WithTags("Support")
.WithName("SupportTriage")
.WithSummary("Classify a support message, ground it in the real order, and draft a reply (sensitive → human review).");

// Ops runbook: rebuild the reservation counter from PostgreSQL truth after drift.
app.MapPost("/internal/resync-stock/{id}", async (int id, IOrderReadModel readModel, IStockReservationGateway redis) =>
{
    var product = await readModel.GetProductAsync(id);
    if (product is null) return Results.NotFound();
    await redis.SetStockAsync(id, product.AvailableStock);
    return Results.Ok(new { id, resyncedTo = product.AvailableStock });
});

// ---------------------------------------------------------------
// Checkout Distributed Saga (Phases 9 & 11)
// ---------------------------------------------------------------
app.MapPost("/api/saga/checkout", async (
    SagaCheckoutApiRequest request,
    HttpContext httpContext,
    CheckoutSagaCoordinator coordinator,
    CancellationToken ct) =>
{
    var key = httpContext.Request.Headers["Idempotency-Key"].ToString();
    if (string.IsNullOrWhiteSpace(key))
    {
        key = request.IdempotencyKey ?? $"saga-{Guid.NewGuid():N}";
    }

    var userId = TryGetUserId(httpContext.User);

    var command = new CheckoutSagaCommand(
        IdempotencyKey: key,
        ProductId: request.ProductId,
        Quantity: request.Quantity,
        Amount: request.Amount,
        UserId: userId);

    var result = await coordinator.ExecuteSagaAsync(command, ct);

    if (result.Status == SagaStatus.Completed)
    {
        return Results.Ok(result);
    }

    if (result.Status == SagaStatus.Compensated)
    {
        return Results.Json(result, statusCode: StatusCodes.Status422UnprocessableEntity);
    }

    return Results.BadRequest(result);
});

app.MapGet("/api/saga/{idempotencyKey}", async (
    string idempotencyKey,
    ICheckoutSagaRepository repo,
    CancellationToken ct) =>
{
    var saga = await repo.GetByIdempotencyKeyAsync(idempotencyKey, ct);
    return saga is not null ? Results.Ok(saga) : Results.NotFound(new { error = $"Saga '{idempotencyKey}' not found." });
});

// ---------------------------------------------------------------
// Transactional Outbox & Deduplication Inbox (Phase 10)
// ---------------------------------------------------------------
app.MapPost("/api/outbox/enqueue", async (
    OutboxEnqueueApiRequest request,
    IOutboxRepository outboxRepo,
    CancellationToken ct) =>
{
    var msg = new OutboxMessage
    {
        MessageId = request.MessageId ?? Guid.NewGuid(),
        EventType = request.EventType ?? "OrderPlacedEvent",
        Topic = request.Topic ?? "orders.events",
        Payload = request.Payload ?? $"{{\"orderId\":\"{Guid.NewGuid()}\",\"amount\":99.99}}"
    };

    await outboxRepo.EnqueueAsync(msg, ct);
    return Results.Ok(new { status = "enqueued", messageId = msg.MessageId, id = msg.Id });
});

// Honest view: `pending` is only what the dispatcher can act on RIGHT NOW.
// Rows dead-lettered or waiting out a backoff are reported separately — the
// previous version returned count=0 while rows sat permanently stuck.
app.MapGet("/api/outbox/pending", async (
    IOutboxRepository outboxRepo,
    CancellationToken ct) =>
{
    var pending = await outboxRepo.GetUnprocessedAsync(50, ct);
    var stuckCount = await outboxRepo.CountStuckAsync(ct);
    return Results.Ok(new { count = pending.Count, stuckCount, messages = pending });
});

app.MapGet("/api/outbox/stuck", async (
    IOutboxRepository outboxRepo,
    CancellationToken ct) =>
{
    var stuck = await outboxRepo.GetStuckAsync(50, ct);
    return Results.Ok(new
    {
        count = stuck.Count,
        deadLettered = stuck.Count(m => m.DeadLetteredAt is not null),
        backingOff = stuck.Count(m => m.DeadLetteredAt is null),
        messages = stuck
    });
});

// Operator recovery path for rows that exhausted their retries (or are stuck in
// a long backoff). Clears retry/backoff/dead-letter state so the dispatcher
// picks them up on its next tick — no SQL by hand, no redeploy.
app.MapPost("/api/outbox/requeue", async (
    IOutboxRepository outboxRepo,
    CancellationToken ct) =>
{
    var revived = await outboxRepo.RequeueStuckAsync(ct);
    return Results.Ok(new { status = "requeued", revived });
});

app.MapPost("/api/inbox/consume", async (
    InboxConsumeApiRequest request,
    IInboxRepository inboxRepo,
    CancellationToken ct) =>
{
    var alreadyProcessed = await inboxRepo.HasBeenProcessedAsync(request.MessageId, request.ConsumerName, ct);
    if (alreadyProcessed)
    {
        return Results.Ok(new { status = "deduplicated", messageId = request.MessageId, processed = false });
    }

    await inboxRepo.MarkProcessedAsync(request.MessageId, request.ConsumerName, ct);
    return Results.Ok(new { status = "processed", messageId = request.MessageId, processed = true });
});

app.MapGet("/health/live", () => Results.Ok(new { status = "healthy" }));

// Back-compat alias for compose probes written before the split.
app.MapGet("/healthz", () => Results.Ok(new { status = "healthy" }));

// Readiness: can this instance safely receive traffic? Postgres is the source
// of truth so it gates readiness. Redis/RabbitMQ are RUNTIME OPTIONAL — the
// API falls back to synchronous Postgres processing when Redis is down
// (Task 2 verified), so their absence must NOT make the API unready.
app.MapGet("/health/ready", async (IDatabaseHealthCheck healthCheck, ILogger<Program> readinessLogger) =>
{
    var canConnect = await healthCheck.CanConnectAsync();
    if (!canConnect)
        readinessLogger.LogWarning("Readiness check failed: PostgreSQL unreachable.");

    return canConnect
        ? Results.Ok(new { status = "ready" })
        : Results.Json(new { status = "not-ready" }, statusCode: 503);
});

/// <summary>
/// Read the caller's user id from the JWT <c>sub</c> claim, or <c>null</c> when
/// the request is anonymous (ADR-013 §6).
/// </summary>
/// <remarks>
/// A local function, not a helper class: it is used by exactly two endpoints in
/// this file, and both need the same "anonymous is allowed" answer.
/// </remarks>
static Guid? TryGetUserId(ClaimsPrincipal user)
{
    var sub = user.FindFirstValue(ClaimTypes.NameIdentifier) ?? user.FindFirstValue("sub");
    return Guid.TryParse(sub, out var id) ? id : null;
}

app.Run();

// Explicit, because the --migrate branch above returns an int: top-level
// statements require every code path to return a value once one of them does.
return 0;

public record OrderRequest(int ProductId, int Quantity);
public record SagaCheckoutApiRequest(int ProductId, int Quantity, decimal Amount, string? IdempotencyKey = null);
public record OutboxEnqueueApiRequest(Guid? MessageId, string? EventType, string? Topic, string? Payload);
public record InboxConsumeApiRequest(Guid MessageId, string ConsumerName);

/// <summary>Body for <c>POST /api/orders/{key}/pay</c> (spec §4 simulation).</summary>
public record PaymentRequest(string? Outcome);

/// <summary>Body for <c>POST /api/content/{id}/reject</c> (spec §8).</summary>
public record ContentRejectRequest(string? Reason);

/// <summary>Body for <c>POST /api/support/triage</c> (spec §9).</summary>
public record SupportTriageRequest(string? Message, string? OrderKey);

/// <summary>Request body for <c>POST /api/assistant/product</c> (spec §10).</summary>
public record AssistantQuestion(string? Question);

/// <summary>
/// Exposed so <c>WebApplicationFactory&lt;Program&gt;</c> can boot this exact
/// composition root from the integration test project (Phase IV E2E).
/// </summary>
/// <remarks>
/// The compiler already generates <c>Program</c> as a partial class for top-level
/// statements; this declaration only widens its accessibility. It must stay after
/// the type declarations above, because top-level statements must precede them.
/// </remarks>
public partial class Program;
