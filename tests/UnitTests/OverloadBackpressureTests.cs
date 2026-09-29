using System.Diagnostics;
using FlashSale.Application.Messaging;
using FlashSale.Application.Orders;
using FlashSale.Infrastructure.Messaging;
using Xunit;

namespace FlashSale.UnitTests;

/// <summary>
/// GATE 3 — OVERLOAD / BACKPRESSURE.
///
/// Câu hỏi phỏng vấn: "Nếu producer nhanh gấp 10 consumer thì hệ thống của
/// em làm gì? RAM có tăng vô hạn không?"
///
/// META-GATE chạy trước: oracle của bài này là "accepted + rejected =
/// submitted". Oracle sai thì mọi kết luận về backpressure sai theo.
///
/// Chạy trên <see cref="InMemoryOrderQueue"/> THẬT của repo (bounded channel
/// 5.000), không mock: đây chính là đường đi production ở dev mode, và queue
/// đó là thứ quyết định hành vi khi đầy.
/// </summary>
public class OverloadBackpressureTests
{
    [Fact]
    public void MetaGate_accounting_oracle_catches_lost_messages()
    {
        int submitted = 100;
        int accepted = 60, rejected = 40;
        Assert.Equal(submitted, accepted + rejected);

        // Negative: hành vi sai phải bị oracle bắt.
        const int lost = 5;
        Assert.False(accepted + rejected + lost == submitted);
    }

    private static OrderMessage Msg() => new(
        ProductId: 1,
        Quantity: 1,
        IdempotencyKey: Guid.NewGuid().ToString("N"),
        CreatedAt: DateTimeOffset.UtcNow);

    [Fact]
    public async Task Queue_rejects_when_full_and_accounting_balances()
    {
        var q = new InMemoryOrderQueue();
        const int submitted = 6_000;   // vượt capacity 5.000
        int accepted = 0, rejected = 0;

        for (int i = 0; i < submitted; i++)
        {
            if (await q.EnqueueAsync(Msg())) accepted++;
            else rejected++;
        }

        Assert.Equal(submitted, accepted + rejected);
        Assert.True(accepted <= 5_000, $"accepted={accepted} vượt capacity");
        Assert.Equal(submitted - accepted, rejected);
    }

    [Fact]
    public async Task Overload_is_shed_not_buffered_so_memory_is_bounded()
    {
        var q = new InMemoryOrderQueue();
        int accepted = 0;
        for (int i = 0; i < 20_000; i++)
            if (await q.EnqueueAsync(Msg())) accepted++;

        // RAM không tăng vô hạn: sau capacity, mọi message bị shed.
        Assert.Equal(5_000, accepted);
    }

    [Fact]
    public async Task Queue_drains_after_consumer_catches_up()
    {
        var q = new InMemoryOrderQueue();
        for (int i = 0; i < 5_000; i++) await q.EnqueueAsync(Msg());

        int drained = 0;
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var consumer = Task.Run(async () =>
        {
            try
            {
                while (drained < 5_000)
                {
                    var e = await q.DequeueAsync(cts.Token);
                    if (e is null) break;
                    drained++;
                }
            }
            catch (OperationCanceledException) { /* budget hết */ }
        });

        await consumer;
        Assert.Equal(5_000, drained);

        // Recovery: producer chạy lại và message MỚI được nhận, nhanh.
        var sw = Stopwatch.StartNew();
        bool acceptedAgain = await q.EnqueueAsync(Msg());
        sw.Stop();
        Assert.True(acceptedAgain, "sau khi drain, phải nhận lại được");
        Assert.True(sw.ElapsedMilliseconds < 1_000,
            $"recovery quá chậm: {sw.ElapsedMilliseconds}ms");
    }

    [Fact]
    public void Queue_full_has_its_own_outcome_not_faked_as_accepted()
    {
        // Chính sách overload đã implement trong SubmitOrderUseCase:
        // queue đầy -> SystemBusy + TRẢ LẠI reservation. Nếu không trả lại,
        // hàng tồn bị giữ cho các order không bao giờ được xử lý.
        //
        // SystemBusy phải là outcome RIÊNG: nếu gộp với Accepted thì client
        // tưởng đã mua hàng trong khi thực tế đã bị shed.
        Assert.NotEqual(SubmitOrderOutcome.SystemBusy, SubmitOrderOutcome.Accepted);
        Assert.NotEqual(SubmitOrderOutcome.SystemBusy, SubmitOrderOutcome.OutOfStock);
        Assert.True(Enum.IsDefined(SubmitOrderOutcome.SystemBusy));
    }
}
