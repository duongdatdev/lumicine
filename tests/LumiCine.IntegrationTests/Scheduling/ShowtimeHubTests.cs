using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using LumiCine.Application.Scheduling;
using LumiCine.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;

namespace LumiCine.IntegrationTests.Persistence;

[Collection(PostgresDatabaseCollection.Name)]
public sealed class ShowtimeHubTests(PostgresDatabaseFixture fixture)
{
    [Fact]
    public async Task Subscribers_receive_only_their_showtime_changes_and_can_leave()
    {
        await using var db = fixture.CreateDbContext();
        var (movie, cinema, firstRoom) = await DatabaseTestData.CreateBaseAsync(db);
        var secondRoom = await DatabaseTestData.CreateRoomAsync(db, cinema.Id);
        var firstShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, firstRoom.Id);
        var secondShowtime = await DatabaseTestData.CreateShowtimeAsync(db, movie.Id, secondRoom.Id);

        await using var factory = new ApiWebApplicationFactory(connectionString: fixture.ConnectionString);
        using var hostClient = factory.CreateClient();
        await using var firstSubscriber = await SignalRTestClient.ConnectAsync(factory);
        await using var secondSubscriber = await SignalRTestClient.ConnectAsync(factory);
        await using var otherShowtimeSubscriber = await SignalRTestClient.ConnectAsync(factory);
        await firstSubscriber.JoinAsync(firstShowtime.Id);
        await secondSubscriber.JoinAsync(firstShowtime.Id);
        await otherShowtimeSubscriber.JoinAsync(secondShowtime.Id);

        await RescheduleAsync(factory, firstShowtime.Id, firstShowtime.StartAtUtc.AddDays(2));

        Assert.Equal(firstShowtime.Id, await firstSubscriber.ReadChangedShowtimeAsync());
        Assert.Equal(firstShowtime.Id, await secondSubscriber.ReadChangedShowtimeAsync());
        Assert.False(await otherShowtimeSubscriber.HasChangedShowtimeAsync());

        await secondSubscriber.LeaveAsync(firstShowtime.Id);
        await RescheduleAsync(factory, firstShowtime.Id, firstShowtime.StartAtUtc.AddDays(3));

        Assert.Equal(firstShowtime.Id, await firstSubscriber.ReadChangedShowtimeAsync());
        Assert.False(await secondSubscriber.HasChangedShowtimeAsync());
        Assert.False(await otherShowtimeSubscriber.HasChangedShowtimeAsync());
    }

    private static async Task RescheduleAsync(ApiWebApplicationFactory factory, Guid showtimeId, DateTime startAtUtc)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var showtimes = scope.ServiceProvider.GetRequiredService<IShowtimeService>();
        var result = await showtimes.RescheduleAsync(showtimeId,
            new RescheduleShowtimeRequest(new DateTimeOffset(startAtUtc, TimeSpan.Zero).ToString("O"), 100), default);
        Assert.NotNull(result);
    }

    private sealed class SignalRTestClient : IAsyncDisposable
    {
        private const char RecordSeparator = '\u001e';
        private readonly WebSocket socket;
        private readonly Channel<string> incoming = Channel.CreateUnbounded<string>();
        private readonly Task readLoop;
        private int invocationId;

        private SignalRTestClient(WebSocket socket)
        {
            this.socket = socket;
            readLoop = ReadLoopAsync();
        }

        public static async Task<SignalRTestClient> ConnectAsync(WebApplicationFactory<Program> factory)
        {
            using var http = factory.CreateClient();
            using var response = await http.PostAsync("/hubs/showtimes/negotiate?negotiateVersion=1", null);
            response.EnsureSuccessStatusCode();
            using var negotiate = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            var connectionToken = negotiate.RootElement.GetProperty("connectionToken").GetString()
                ?? throw new InvalidOperationException("SignalR negotiation returned no connection token.");
            var uri = new Uri($"ws://localhost/hubs/showtimes?id={Uri.EscapeDataString(connectionToken)}");
            var socket = await factory.Server.CreateWebSocketClient().ConnectAsync(uri, CancellationToken.None);
            var handshake = Encoding.UTF8.GetBytes("{\"protocol\":\"json\",\"version\":1}" + RecordSeparator);
            await socket.SendAsync(handshake, WebSocketMessageType.Text, true, CancellationToken.None);
            var handshakeResult = await ReceiveFrameAsync(socket, CancellationToken.None);
            if (!Encoding.UTF8.GetString(handshakeResult).StartsWith("{}" + RecordSeparator, StringComparison.Ordinal))
                throw new InvalidOperationException("SignalR JSON handshake failed.");
            return new SignalRTestClient(socket);
        }

        public Task JoinAsync(Guid showtimeId) => InvokeAsync("JoinShowtime", showtimeId);

        public Task LeaveAsync(Guid showtimeId) => InvokeAsync("LeaveShowtime", showtimeId);

        public async Task<Guid> ReadChangedShowtimeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await incoming.Reader.WaitToReadAsync(timeout.Token))
            {
                while (incoming.Reader.TryRead(out var message))
                {
                    using var json = JsonDocument.Parse(message);
                    var root = json.RootElement;
                    if (root.TryGetProperty("target", out var target) && target.GetString() == "SeatAvailabilityChanged")
                        return root.GetProperty("arguments")[0].GetProperty("showtimeId").GetGuid();
                }
            }

            throw new EndOfStreamException("SignalR connection closed before a showtime event arrived.");
        }

        public async Task<bool> HasChangedShowtimeAsync()
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
            try
            {
                while (await incoming.Reader.WaitToReadAsync(timeout.Token))
                {
                    while (incoming.Reader.TryRead(out var message))
                    {
                        using var json = JsonDocument.Parse(message);
                        if (json.RootElement.TryGetProperty("target", out var target) && target.GetString() == "SeatAvailabilityChanged")
                            return true;
                    }
                }
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested) { }
            return false;
        }

        private async Task InvokeAsync(string target, Guid showtimeId)
        {
            var id = Interlocked.Increment(ref invocationId).ToString(System.Globalization.CultureInfo.InvariantCulture);
            var payload = JsonSerializer.Serialize(new
            {
                type = 1,
                invocationId = id,
                target,
                arguments = new[] { showtimeId }
            }) + RecordSeparator;
            await socket.SendAsync(Encoding.UTF8.GetBytes(payload), WebSocketMessageType.Text, true, CancellationToken.None);

            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (await incoming.Reader.WaitToReadAsync(timeout.Token))
            {
                while (incoming.Reader.TryRead(out var message))
                {
                    using var json = JsonDocument.Parse(message);
                    var root = json.RootElement;
                    if (root.TryGetProperty("type", out var type) && type.GetInt32() == 3 &&
                        root.TryGetProperty("invocationId", out var responseId) && responseId.GetString() == id)
                    {
                        if (root.TryGetProperty("error", out var error))
                            throw new InvalidOperationException($"SignalR invocation failed: {error.GetString()}");
                        return;
                    }
                }
            }

            throw new TimeoutException($"SignalR invocation {target} did not complete.");
        }

        private async Task ReadLoopAsync()
        {
            try
            {
                while (socket.State == WebSocketState.Open)
                {
                    var frame = await ReceiveFrameAsync(socket, CancellationToken.None);
                    var text = Encoding.UTF8.GetString(frame);
                    foreach (var message in text.Split(RecordSeparator, StringSplitOptions.RemoveEmptyEntries))
                        await incoming.Writer.WriteAsync(message);
                }
            }
            catch (WebSocketException) { }
            catch (ObjectDisposedException) { }
            catch (OperationCanceledException) { }
            catch (EndOfStreamException) { }
            finally { incoming.Writer.TryComplete(); }
        }

        private static async Task<byte[]> ReceiveFrameAsync(WebSocket socket, CancellationToken cancellationToken)
        {
            using var buffer = new MemoryStream();
            var chunk = new byte[4096];
            WebSocketReceiveResult result;
            do
            {
                result = await socket.ReceiveAsync(chunk, cancellationToken);
                if (result.MessageType == WebSocketMessageType.Close)
                    throw new EndOfStreamException("SignalR connection closed.");
                buffer.Write(chunk, 0, result.Count);
            } while (!result.EndOfMessage);
            return buffer.ToArray();
        }

        public async ValueTask DisposeAsync()
        {
            if (socket.State == WebSocketState.Open)
                await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "test complete", CancellationToken.None);
            await readLoop;
            socket.Dispose();
        }
    }
}
