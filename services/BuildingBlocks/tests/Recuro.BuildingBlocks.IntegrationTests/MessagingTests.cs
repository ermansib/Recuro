using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Recuro.BuildingBlocks.IntegrationTests;

[Collection(ContainersCollection.Name)]
public sealed class MessagingTests(ContainersFixture containers)
{
    [Fact]
    public async Task Outbox_relay_publishes_to_RabbitMQ_and_the_consumer_handles_the_event()
    {
        // Producer and consumer are separate "services" with separate databases and queues.
        await using var producer = TestHost.Build(containers, $"producer_{Guid.NewGuid():N}", messaging: true, serviceName: "producer");
        await using var consumer = TestHost.Build(containers, $"consumer_{Guid.NewGuid():N}", messaging: true, serviceName: $"consumer-{Guid.NewGuid():N}");
        await TestHost.CreateDatabaseAsync(producer);
        await TestHost.CreateDatabaseAsync(consumer);

        var hosted = consumer.GetServices<IHostedService>().Concat(producer.GetServices<IHostedService>()).ToList();
        foreach (var service in hosted)
        {
            await service.StartAsync(CancellationToken.None);
        }

        try
        {
            // Give the consumer time to declare and bind its queue before anything is published.
            await Task.Delay(TimeSpan.FromSeconds(2));

            var widget = Widget.Create("relayed");
            await using (var scope = TestHost.ScopeFor(producer, TestHost.TenantA))
            {
                var db = scope.ServiceProvider.GetRequiredService<TestDbContext>();
                db.Widgets.Add(widget);
                await db.SaveChangesAsync();
            }

            var received = await WaitForAsync(async () =>
            {
                await using var scope = TestHost.ScopeFor(consumer, TestHost.TenantA);
                return await scope.ServiceProvider.GetRequiredService<TestDbContext>().Received.AnyAsync(r => r.WidgetId == widget.Id);
            });
            Assert.True(received, "The consumer never handled the event.");

            await using var check = producer.CreateAsyncScope();
            var outbox = await check.ServiceProvider.GetRequiredService<TestDbContext>().OutboxMessages.SingleAsync();
            Assert.NotNull(outbox.ProcessedAt);
        }
        finally
        {
            foreach (var service in hosted)
            {
                await service.StopAsync(CancellationToken.None);
            }
        }
    }

    private static async Task<bool> WaitForAsync(Func<Task<bool>> condition)
    {
        for (var i = 0; i < 100; i++)
        {
            if (await condition())
            {
                return true;
            }

            await Task.Delay(200);
        }

        return false;
    }
}
