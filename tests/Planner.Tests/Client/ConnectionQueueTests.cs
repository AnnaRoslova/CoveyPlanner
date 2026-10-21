using System.Net;
using Planner.Client.Services;

namespace Planner.Tests.Client;

/// <summary>Нет связи (batch-ux.md, BN1, BN3, BN6): изменение ждёт связи и повторяется, порядок сохраняется, чтения ждут очередь.</summary>
public class ConnectionQueueTests
{
    private static Task<HttpResponseMessage> Ok() => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK));

    /// <summary>«Сервер»: пока online = false, запросы падают, как при обрыве связи; удачные записываются по порядку.</summary>
    private sealed class FakeServer
    {
        public bool Online { get; set; } = true;

        public List<string> Received { get; } = [];

        public Func<Task<HttpResponseMessage>> Send(string what) => () =>
        {
            if (!Online)
            {
                throw new HttpRequestException("Failed to fetch");
            }

            Received.Add(what);
            return Ok();
        };
    }

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var i = 0; i < 200 && !condition(); i++)
        {
            await Task.Delay(10);
        }

        Assert.True(condition());
    }

    [Fact]
    public async Task Offline_ChangesWaitInOrder_RetryNowSendsAll_RestoredCountsThem()
    {
        var queue = new ConnectionQueue();
        var server = new FakeServer { Online = false };
        var restored = new List<int>();
        queue.Restored += restored.Add;

        var first = queue.SendAsync(server.Send("A"));
        var second = queue.SendAsync(server.Send("B"));
        await UntilAsync(() => queue.IsOffline);

        Assert.Equal(2, queue.Pending);
        Assert.False(first.IsCompleted);
        var read = queue.WhenDrainedAsync();
        Assert.False(read.IsCompleted); // BN6: чтение ждёт очередь

        server.Online = true;
        queue.RetryNow();
        await Task.WhenAll(first, second, read);

        Assert.Equal(["A", "B"], server.Received);
        Assert.False(queue.IsOffline);
        Assert.Equal(0, queue.Pending);
        Assert.Equal([2], restored);
    }

    [Fact]
    public async Task Online_SendsImmediately_NoRestoredEvent()
    {
        var queue = new ConnectionQueue();
        var server = new FakeServer();
        var restored = 0;
        queue.Restored += _ => restored++;

        await queue.SendAsync(server.Send("A"));

        Assert.Equal(["A"], server.Received);
        Assert.Equal(0, restored);
        Assert.True(queue.WhenDrainedAsync().IsCompleted);
    }

    [Fact]
    public async Task ServerError_IsNotRetried_GoesToCaller()
    {
        var queue = new ConnectionQueue();
        var calls = 0;

        var response = await queue.SendAsync(() =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Conflict));
        });

        Assert.Equal((HttpStatusCode.Conflict, 1), (response.StatusCode, calls)); // BN4: ответ сервера — не обрыв связи
        Assert.False(queue.IsOffline);
    }
}
