using Bekend.Data;
using Microsoft.EntityFrameworkCore;

namespace Bekend.Services;

/// <summary>
/// Pozadinski servis (registrovan u Program.cs preko AddHostedService) koji na
/// svakih 5 minuta proverava da li postoje potvrđene rezervacije čiji termin
/// počinje za manje od 12 sati, i za njih šalje podsetnik (Notification).
///
/// VAŽNO o DbContext-u: BackgroundService živi onoliko dugo koliko i cela aplikacija
/// (singleton), dok je AppDbContext registrovan kao "scoped" (živi kratko, po jednom
/// HTTP zahtevu). Zato se DbContext ovde NE ubrizgava direktno kroz konstruktor
/// (to bi bacalo grešku ili delilo jedan te isti DbContext zauvek, što nije bezbedno
/// za paralelan rad). Umesto toga, IServiceScopeFactory pravi nov, kratkotrajan
/// "scope" pri svakom pozivu SendDueReminders(), iz kog se izvlači sveži DbContext
/// (vidi CreateScope() ispod) — isti pattern koji bi koristio i pravi HTTP zahtev.
/// </summary>
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

    /// <summary>
    /// Glavna petlja servisa: pokreće se jednom pri startu aplikacije i radi dok
    /// se aplikacija ne ugasi (stoppingToken se okine pri shutdown-u). Greška u
    /// jednom krugu (uhvaćena u catch-u) se samo loguje i ne ruši ceo servis —
    /// petlja nastavlja da radi i pokušava ponovo za 5 minuta.
    /// </summary>
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

    /// <summary>
    /// Pronalazi sve potvrđene rezervacije čiji termin počinje u sledećih 12 sati
    /// i za koje podsetnik još nije poslat (ReminderSent == false), šalje im
    /// notifikaciju i označava ih kao obrađene (ReminderSent = true) da se
    /// podsetnik ne bi slao više puta za istu rezervaciju.
    /// </summary>
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