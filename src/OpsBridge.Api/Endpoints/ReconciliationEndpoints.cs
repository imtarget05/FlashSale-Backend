using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using OpsBridge.Application.Reconciliation;
using OpsBridge.Application.UseCases;
using OpsBridge.Domain;
using OpsBridge.Domain.Reconciliation;
using OpsBridge.Infrastructure.Ingestion;

namespace OpsBridge.Api.Endpoints;

public static class ReconciliationEndpoints
{
    public static IEndpointRouteBuilder MapReconciliationEndpoints(this IEndpointRouteBuilder app)
    {
        var group = app.MapGroup("/api/v1/reconciliation")
            .WithTags("Reconciliation");

        // Purchase Orders
        app.MapPost("/api/v1/purchase-orders", async (
            HttpContext context,
            IPurchaseOrderRepository poRepo,
            CancellationToken ct) =>
        {
            var contentType = context.Request.ContentType ?? "";
            if (contentType.Contains("text/csv", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.Body);
                var csv = await reader.ReadToEndAsync(ct);
                var pos = CsvPurchaseOrderParser.Parse(csv);
                foreach (var po in pos)
                {
                    await poRepo.AddAsync(po, ct);
                }
                return Results.Created("/api/v1/purchase-orders", new { count = pos.Count, message = "Purchase orders imported from CSV" });
            }

            var request = await context.Request.ReadFromJsonAsync<CreatePurchaseOrderRequest>(cancellationToken: ct);
            if (request == null || string.IsNullOrWhiteSpace(request.OrderNumber))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "OrderNumber and lines are required." });
            }

            var purchaseOrder = new PurchaseOrder
            {
                Id = Guid.NewGuid(),
                OrderNumber = request.OrderNumber,
                SupplierCode = request.SupplierCode,
                SupplierName = request.SupplierName,
                Currency = request.Currency ?? "VND",
                Status = PurchaseOrderStatus.Open,
                OrderedAt = DateTimeOffset.UtcNow,
                SourceSystem = request.SourceSystem ?? "ApiSnapshot",
                Lines = request.Lines.Select(l => new PurchaseOrderLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = l.LineNumber,
                    ItemSku = l.ItemSku,
                    Description = l.Description ?? "",
                    OrderedQuantity = l.OrderedQuantity,
                    UnitOfMeasure = l.UnitOfMeasure ?? "PCS",
                    UnitPrice = l.UnitPrice,
                    TotalAmount = Math.Round(l.OrderedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero)
                }).ToList()
            };
            purchaseOrder.TotalAmount = purchaseOrder.Lines.Sum(l => l.TotalAmount);

            await poRepo.AddAsync(purchaseOrder, ct);
            return Results.Created($"/api/v1/purchase-orders/{purchaseOrder.OrderNumber}", purchaseOrder);
        })
        .WithTags("Procurement")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status400BadRequest);

        app.MapGet("/api/v1/purchase-orders/{orderNumber}", async (
            string orderNumber,
            IPurchaseOrderRepository poRepo,
            CancellationToken ct) =>
        {
            var po = await poRepo.GetByOrderNumberAsync(orderNumber, ct);
            return po != null ? Results.Ok(po) : Results.NotFound(new { error = "NotFound", message = $"Purchase order '{orderNumber}' not found." });
        })
        .WithTags("Procurement");

        // Goods Receipts
        app.MapPost("/api/v1/goods-receipts", async (
            CreateGoodsReceiptRequest request,
            IGoodsReceiptRepository grRepo,
            CancellationToken ct) =>
        {
            if (string.IsNullOrWhiteSpace(request.ReceiptNumber) || string.IsNullOrWhiteSpace(request.PurchaseOrderNumber))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "ReceiptNumber and PurchaseOrderNumber are required." });
            }

            var gr = new GoodsReceipt
            {
                Id = Guid.NewGuid(),
                ReceiptNumber = request.ReceiptNumber,
                PurchaseOrderNumber = request.PurchaseOrderNumber,
                SupplierCode = request.SupplierCode,
                ReceivedAt = request.ReceivedAt ?? DateTimeOffset.UtcNow,
                SourceSystem = request.SourceSystem ?? "ApiSnapshot",
                Lines = request.Lines.Select(l => new GoodsReceiptLine
                {
                    Id = Guid.NewGuid(),
                    LineNumber = l.LineNumber,
                    ItemSku = l.ItemSku,
                    ReceivedQuantity = l.ReceivedQuantity,
                    UnitOfMeasure = l.UnitOfMeasure ?? "PCS",
                    AllocatedQuantity = 0m
                }).ToList()
            };

            await grRepo.AddAsync(gr, ct);
            return Results.Created($"/api/v1/goods-receipts/{gr.ReceiptNumber}", gr);
        })
        .WithTags("Procurement")
        .Produces(StatusCodes.Status201Created);

        app.MapGet("/api/v1/goods-receipts/{receiptNumber}", async (
            string receiptNumber,
            IGoodsReceiptRepository grRepo,
            CancellationToken ct) =>
        {
            var gr = await grRepo.GetByReceiptNumberAsync(receiptNumber, ct);
            return gr != null ? Results.Ok(gr) : Results.NotFound(new { error = "NotFound", message = $"Goods receipt '{receiptNumber}' not found." });
        })
        .WithTags("Procurement");

        // Supplier Invoices
        app.MapPost("/api/v1/invoices", async (
            HttpContext context,
            ISupplierInvoiceRepository invoiceRepo,
            CancellationToken ct) =>
        {
            var contentType = context.Request.ContentType ?? "";
            if (contentType.Contains("text/csv", StringComparison.OrdinalIgnoreCase))
            {
                using var reader = new StreamReader(context.Request.Body);
                var csv = await reader.ReadToEndAsync(ct);
                var invoices = CsvInvoiceParser.Parse(csv);
                foreach (var inv in invoices)
                {
                    await invoiceRepo.AddAsync(inv, ct);
                }
                return Results.Created("/api/v1/invoices", new { count = invoices.Count, message = "Invoices imported from CSV" });
            }

            var request = await context.Request.ReadFromJsonAsync<CreateSupplierInvoiceRequest>(cancellationToken: ct);
            if (request == null || string.IsNullOrWhiteSpace(request.InvoiceNumber))
            {
                return Results.BadRequest(new { error = "InvalidRequest", message = "InvoiceNumber is required." });
            }

            var existing = await invoiceRepo.GetByNumberAndSupplierAsync(request.InvoiceNumber, request.SupplierCode, ct);
            if (existing != null)
            {
                return Results.Conflict(new { error = "DuplicateInvoice", message = $"Invoice '{request.InvoiceNumber}' for supplier '{request.SupplierCode}' already exists." });
            }

            var invoice = new SupplierInvoice
            {
                Id = Guid.NewGuid(),
                InvoiceNumber = request.InvoiceNumber,
                PurchaseOrderNumber = request.PurchaseOrderNumber,
                SupplierCode = request.SupplierCode,
                SupplierName = request.SupplierName,
                Currency = request.Currency ?? "VND",
                InvoiceDate = request.InvoiceDate ?? DateTimeOffset.UtcNow,
                Status = InvoiceStatus.PendingReconciliation,
                SourceSystem = request.SourceSystem ?? "ApiSnapshot",
                Lines = request.Lines.Select(l =>
                {
                    var sub = Math.Round(l.InvoicedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero);
                    var tax = Math.Round(sub * l.TaxRate, 2, MidpointRounding.AwayFromZero);
                    return new SupplierInvoiceLine
                    {
                        Id = Guid.NewGuid(),
                        LineNumber = l.LineNumber,
                        ItemSku = l.ItemSku,
                        Description = l.Description ?? "",
                        InvoicedQuantity = l.InvoicedQuantity,
                        UnitOfMeasure = l.UnitOfMeasure ?? "PCS",
                        UnitPrice = l.UnitPrice,
                        TaxRate = l.TaxRate,
                        LineTotal = sub + tax,
                        MatchedQuantity = 0m
                    };
                }).ToList()
            };
            invoice.SubTotal = invoice.Lines.Sum(l => Math.Round(l.InvoicedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero));
            invoice.TaxAmount = invoice.Lines.Sum(l => Math.Round(Math.Round(l.InvoicedQuantity * l.UnitPrice, 2, MidpointRounding.AwayFromZero) * l.TaxRate, 2, MidpointRounding.AwayFromZero));
            invoice.TotalAmount = invoice.SubTotal + invoice.TaxAmount;

            await invoiceRepo.AddAsync(invoice, ct);
            return Results.Created($"/api/v1/invoices/{invoice.Id}", invoice);
        })
        .WithTags("Invoices")
        .Produces(StatusCodes.Status201Created)
        .Produces(StatusCodes.Status409Conflict);

        app.MapGet("/api/v1/invoices/{id:guid}", async (
            Guid id,
            ISupplierInvoiceRepository invoiceRepo,
            CancellationToken ct) =>
        {
            var invoice = await invoiceRepo.GetByIdAsync(id, ct);
            return invoice != null ? Results.Ok(invoice) : Results.NotFound(new { error = "NotFound", message = $"Invoice '{id}' not found." });
        })
        .WithTags("Invoices");

        // 3-Way Reconciliation Execution
        group.MapPost("/run", async (
            RunReconciliationRequest request,
            ReconcileInvoiceUseCase reconcileUseCase,
            CancellationToken ct) =>
        {
            var policy = new TolerancePolicy(
                PriceTolerancePercentage: request.PriceTolerancePercentage ?? 0.01m,
                QuantityTolerancePercentage: request.QuantityTolerancePercentage ?? 0.00m,
                QuantityToleranceAbsolute: request.QuantityToleranceAbsolute ?? 0.00m
            );

            var result = await reconcileUseCase.ExecuteAsync(request.InvoiceId, policy, request.BatchId, ct);
            return Results.Ok(result);
        })
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status404NotFound);

        // Results
        group.MapGet("/results/{id:guid}", async (
            Guid id,
            IReconciliationRepository reconRepo,
            CancellationToken ct) =>
        {
            var result = await reconRepo.GetResultByIdAsync(id, ct);
            return result != null ? Results.Ok(result) : Results.NotFound(new { error = "NotFound", message = $"Reconciliation result '{id}' not found." });
        });

        // Discrepancies
        group.MapGet("/discrepancies", async (
            DiscrepancyStatus? status,
            IReconciliationRepository reconRepo,
            CancellationToken ct) =>
        {
            var list = await reconRepo.GetDiscrepanciesAsync(status, ct);
            return Results.Ok(list);
        });

        // Discrepancy Review
        group.MapPost("/discrepancies/{id:guid}/review", async (
            Guid id,
            ReviewDiscrepancyRequest request,
            HttpContext context,
            ReviewDiscrepancyUseCase reviewUseCase,
            CancellationToken ct) =>
        {
            // Determine role from Claims or Header
            var user = context.User;
            var role = user.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value
                ?? context.Request.Headers["X-User-Role"].FirstOrDefault()
                ?? (user.IsInRole(OpsBridgeRoles.Admin) ? OpsBridgeRoles.Admin :
                    user.IsInRole(OpsBridgeRoles.FinanceReviewer) ? OpsBridgeRoles.FinanceReviewer :
                    user.IsInRole(OpsBridgeRoles.Operator) ? OpsBridgeRoles.Operator : "");

            var userId = user.Identity?.Name
                ?? context.Request.Headers["X-User-Id"].FirstOrDefault()
                ?? "Operator-1";

            var command = new ReviewDiscrepancyCommand(
                DiscrepancyId: id,
                Action: request.Action,
                ReviewerUserId: userId,
                ReviewerRole: role,
                Notes: request.Notes ?? ""
            );

            try
            {
                var decision = await reviewUseCase.ExecuteAsync(command, ct);
                return Results.Ok(decision);
            }
            catch (UnauthorizedAccessException ex)
            {
                return Results.Json(new { error = "Forbidden", message = ex.Message }, statusCode: StatusCodes.Status403Forbidden);
            }
        })
        .Produces(StatusCodes.Status200OK)
        .Produces(StatusCodes.Status403Forbidden)
        .Produces(StatusCodes.Status404NotFound);

        // Mapping Rules
        group.MapPost("/mapping-rules", async (
            CreateMappingRuleRequest request,
            IMappingRuleRepository mappingRepo,
            CancellationToken ct) =>
        {
            var rule = new MappingRule
            {
                Id = Guid.NewGuid(),
                RuleType = request.RuleType,
                SourceValue = request.SourceValue,
                TargetValue = request.TargetValue,
                ConversionFactor = request.ConversionFactor <= 0 ? 1.0m : request.ConversionFactor,
                SupplierCode = request.SupplierCode,
                IsActive = true,
                CreatedAt = DateTimeOffset.UtcNow
            };
            await mappingRepo.AddAsync(rule, ct);
            return Results.Created($"/api/v1/reconciliation/mapping-rules/{rule.Id}", rule);
        });

        // Audit Trail
        group.MapGet("/audit-trail", async (
            IAuditEventRepository auditRepo,
            CancellationToken ct) =>
        {
            var events = await auditRepo.GetRecentAsync(100, ct);
            return Results.Ok(events);
        });

        return app;
    }
}

// DTOs
public sealed record CreatePurchaseOrderRequest(
    string OrderNumber,
    string SupplierCode,
    string SupplierName,
    string? Currency,
    string? SourceSystem,
    List<CreatePurchaseOrderLineRequest> Lines
);

public sealed record CreatePurchaseOrderLineRequest(
    int LineNumber,
    string ItemSku,
    string? Description,
    decimal OrderedQuantity,
    string? UnitOfMeasure,
    decimal UnitPrice
);

public sealed record CreateGoodsReceiptRequest(
    string ReceiptNumber,
    string PurchaseOrderNumber,
    string SupplierCode,
    DateTimeOffset? ReceivedAt,
    string? SourceSystem,
    List<CreateGoodsReceiptLineRequest> Lines
);

public sealed record CreateGoodsReceiptLineRequest(
    int LineNumber,
    string ItemSku,
    decimal ReceivedQuantity,
    string? UnitOfMeasure
);

public sealed record CreateSupplierInvoiceRequest(
    string InvoiceNumber,
    string PurchaseOrderNumber,
    string SupplierCode,
    string SupplierName,
    string? Currency,
    DateTimeOffset? InvoiceDate,
    string? SourceSystem,
    List<CreateSupplierInvoiceLineRequest> Lines
);

public sealed record CreateSupplierInvoiceLineRequest(
    int LineNumber,
    string ItemSku,
    string? Description,
    decimal InvoicedQuantity,
    string? UnitOfMeasure,
    decimal UnitPrice,
    decimal TaxRate
);

public sealed record RunReconciliationRequest(
    Guid InvoiceId,
    decimal? PriceTolerancePercentage = null,
    decimal? QuantityTolerancePercentage = null,
    decimal? QuantityToleranceAbsolute = null,
    Guid? BatchId = null
);

public sealed record ReviewDiscrepancyRequest(
    ReviewAction Action,
    string? Notes
);

public sealed record CreateMappingRuleRequest(
    MappingRuleType RuleType,
    string SourceValue,
    string TargetValue,
    decimal ConversionFactor = 1.0m,
    string? SupplierCode = null
);
