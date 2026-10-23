using System.Net;
using System.Text;
using LumiCine.Application.Accounts;
using LumiCine.Application.Bookings;
using LumiCine.Domain.Bookings;
using LumiCine.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace LumiCine.Infrastructure.Bookings;

public sealed class TicketEmailComposer(LumiCineDbContext db, ITicketQrRenderer qrRenderer) : ITicketEmailComposer
{
    public async Task<EmailMessage> ComposeAsync(Guid bookingId, CancellationToken ct)
    {
        var booking = await db.Bookings.AsNoTracking().SingleOrDefaultAsync(x => x.Id == bookingId, ct)
            ?? throw new InvalidOperationException("The booking for the ticket email was not found.");
        var movieTitle = await (from showtime in db.Showtimes.AsNoTracking()
                                join movie in db.Movies.AsNoTracking() on showtime.MovieId equals movie.Id
                                where showtime.Id == booking.ShowtimeId
                                select movie.Title).SingleOrDefaultAsync(ct);
        var recipient = booking.UserId is Guid userId
            ? await db.Users.AsNoTracking().Where(x => x.Id == userId).Select(x => x.Email).SingleOrDefaultAsync(ct)
            : null;
        var tickets = await (from ticket in db.Tickets.AsNoTracking()
                             join item in db.BookingItems.AsNoTracking() on ticket.BookingItemId equals item.Id
                             join showtimeSeat in db.ShowtimeSeats.AsNoTracking() on item.ShowtimeSeatId equals showtimeSeat.Id
                             join seat in db.Seats.AsNoTracking() on showtimeSeat.SeatId equals seat.Id
                             where item.BookingId == bookingId
                             orderby seat.RowLabel, seat.Number, seat.Id
                             select new TicketLine(ticket.Code, seat.RowLabel, seat.Number)).ToListAsync(ct);
        if (tickets.Count == 0)
            throw new InvalidOperationException("Ticket emails are only composed once the tickets are issued.");

        var html = BuildHtmlBody(booking, movieTitle ?? string.Empty, tickets);
        var plain = BuildPlainBody(booking, movieTitle ?? string.Empty, tickets);
        var attachments = tickets.Select((ticket, index) => new EmailInlineAttachment(
            $"qr-{index}", $"ticket-{index + 1}.png", "image/png", qrRenderer.RenderPng(ticket.Code))).ToArray();
        return new EmailMessage(recipient ?? throw new InvalidOperationException("The booking has no recipient email address."),
            $"Your LumiCine tickets ({booking.Code})",
            plain)
        {
            HtmlBody = html,
            InlineAttachments = attachments
        };
    }

    private static string BuildHtmlBody(
        Booking booking, string movieTitle, IReadOnlyList<TicketLine> tickets)
    {
        var builder = new StringBuilder();
        builder.Append("<p>Thank you for booking with LumiCine.</p>");
        builder.Append("<p>Booking <strong>").Append(HtmlEncode(booking.Code)).Append("</strong> for <strong>")
            .Append(HtmlEncode(movieTitle)).Append("</strong>, total ")
            .Append(booking.TotalAmount.ToString("0.##")).Append(" VND.</p>");
        builder.Append("<p>Show each QR code at the entrance:</p><ul>");
        foreach (var (ticket, index) in tickets.Select((value, index) => (value, index)))
        {
            builder.Append("<li>Seat ").Append(HtmlEncode(ticket.RowLabel)).Append(ticket.Number)
                .Append(" &mdash; <img src=\"cid:qr-").Append(index)
                .Append("\" alt=\"QR for seat ").Append(HtmlEncode(ticket.RowLabel)).Append(ticket.Number)
                .Append("\" width=\"120\" height=\"120\" /></li>");
        }
        builder.Append("</ul>");
        return builder.ToString();
    }

    private static string BuildPlainBody(
        Booking booking, string movieTitle, IReadOnlyList<TicketLine> tickets)
    {
        var builder = new StringBuilder();
        builder.Append("Thank you for booking with LumiCine.").AppendLine();
        builder.Append("Booking ").Append(booking.Code).Append(" for ").Append(movieTitle).Append(", total ")
            .Append(booking.TotalAmount.ToString("0.##")).Append(" VND.").AppendLine();
        builder.Append("Ticket codes (QR codes are attached):").AppendLine();
        foreach (var ticket in tickets)
        {
            builder.Append("  Seat ").Append(ticket.RowLabel).Append(ticket.Number).Append(": ").Append(ticket.Code)
                .AppendLine();
        }
        return builder.ToString();
    }

    private static string HtmlEncode(string value) => WebUtility.HtmlEncode(value);

    private sealed record TicketLine(string Code, string RowLabel, int Number);
}
