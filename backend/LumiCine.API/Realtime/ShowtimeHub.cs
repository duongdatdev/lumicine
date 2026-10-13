using LumiCine.Application.Scheduling;
using Microsoft.AspNetCore.SignalR;

namespace LumiCine.API.Realtime;

public sealed class ShowtimeHub(IShowtimeService showtimes) : Hub
{
    private const string CurrentShowtimeKey = "CurrentShowtimeId";

    public async Task JoinShowtime(Guid showtimeId)
    {
        if (showtimeId == Guid.Empty || await showtimes.GetPublicAsync(showtimeId, Context.ConnectionAborted) is null)
            throw new HubException("Showtime is not publicly available.");

        if (Context.Items.TryGetValue(CurrentShowtimeKey, out var current) && current is Guid currentId)
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(currentId), Context.ConnectionAborted);

        await Groups.AddToGroupAsync(Context.ConnectionId, GroupName(showtimeId), Context.ConnectionAborted);
        Context.Items[CurrentShowtimeKey] = showtimeId;
    }

    public async Task LeaveShowtime(Guid showtimeId)
    {
        if (Context.Items.TryGetValue(CurrentShowtimeKey, out var current) && current is Guid currentId && currentId == showtimeId)
        {
            await Groups.RemoveFromGroupAsync(Context.ConnectionId, GroupName(currentId), Context.ConnectionAborted);
            Context.Items.Remove(CurrentShowtimeKey);
        }
    }

    internal static string GroupName(Guid showtimeId) => $"showtime:{showtimeId:N}";
}
