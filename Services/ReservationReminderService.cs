using Bekend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bekend.Services;

public class ReservationReminderService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ReservationReminderService> _logger;
    private readonly TimeSpan _checkInterval = TimeSpan.FromMinutes(5);

    public ReservationReminderService(IServiceScopeFactory scopeFactory, ILogger<ReservationReminderService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await SendDueReminders();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Greska prilikom slanja podsetnika za rezervacije.");
            }

            await Task.Delay(_checkInterval, stoppingToken);
        }
    }

    private async Task SendDueReminders()
    {
        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var now = DateTime.Now;
        var reminderWindow = now.AddHours(12);

        var dueReservations = await context.Reservations
            .Include(r => r.TimeSlot)
                .ThenInclude(t => t.Service)
            .Where(r => r.Status == "confirmed"
                && !r.ReminderSent
                && r.TimeSlot.StartTime > now
                && r.TimeSlot.StartTime <= reminderWindow)
            .ToListAsync();

        if (dueReservations.Count == 0)
        {
            return;
        }

        foreach (var reservation in dueReservations)
        {
            var notification = new Models.Notification
            {
                UserId = reservation.UserId,
                ReservationId = reservation.Id,
                Message = $"Podsetnik: rezervacija za {reservation.TimeSlot.Service.Name}, " +
                          $"sto br. {reservation.TimeSlot.TableNumber} je zakazana za " +
                          $"{reservation.TimeSlot.StartTime:dd.MM.yyyy.} u {reservation.TimeSlot.StartTime:HH:mm} " +
                          $"(za manje od 12 sati).",
                IsRead = false,
                CreatedAt = DateTime.UtcNow
            };

            context.Notifications.Add(notification);
            reservation.ReminderSent = true;
        }

        await context.SaveChangesAsync();

        _logger.LogInformation("Poslato {Count} podsetnik(a) za rezervacije.", dueReservations.Count);
    }
}