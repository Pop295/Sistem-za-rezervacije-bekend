using Bekend.Data;
using Bekend.DTOs;
using Bekend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Bekend.Controllers;

[ApiController]
[Route("api/[controller]")]
/// <summary>
/// Kontroler za kreiranje, pregled, otkazivanje i admin upravljanje rezervacijama.
/// Svaka rezervacija ima svoj sopstveni TimeSlot (kreira se dinamički pri rezervaciji,
/// vidi Create() ispod) — termini se ne biraju sa unapred definisane liste, već korisnik
/// bira uslugu, sto i slobodno vreme početka.
/// </summary>
public class ReservationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReservationsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Vraća listu rezervacija. Obični korisnik vidi samo svoje rezervacije
    /// (filtrirano po userId iz tokena), dok admin vidi rezervacije svih korisnika.
    /// </summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReservationDto>>> GetAll()
    {
        // userId i role se čitaju direktno iz JWT claim-ova, bez dodatnog upita ka bazi
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var query = _context.Reservations.AsQueryable();

        if (!string.Equals(role, "Admin", StringComparison.OrdinalIgnoreCase))
        {
            query = query.Where(r => r.UserId == userId);
        }

        var rawReservations = await query
            .Select(r => new
            {
                r.Id,
                r.Status,
                r.TimeSlot.StartTime,
                r.TimeSlot.TableNumber,
                ServiceName = r.TimeSlot.Service.Name
            })
            .ToListAsync();

        var result = rawReservations
            .Select(r => new ReservationDto
            {
                Id = r.Id,
                ServiceName = r.ServiceName,
                TableNumber = r.TableNumber,
                Date = r.StartTime.ToString("yyyy-MM-dd"),
                Time = r.StartTime.ToString("HH:mm"),
                Status = r.Status
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Kreira novu rezervaciju. Umesto biranja postojećeg TimeSlot-a sa liste,
    /// klijent šalje uslugu, broj stola, datum i vreme početka — TimeSlot se
    /// generiše "u letu" (vidi ispod). Nakon uspešnog kreiranja automatski se
    /// šalje i Notification korisniku kao potvrda rezervacije.
    /// </summary>
    [Authorize]
    [HttpPost]
    public async Task<ActionResult<ReservationDto>> Create(ReservationCreateDto request)
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        if (!DateTime.TryParse($"{request.Date} {request.StartTime}", out var startDateTime))
        {
            return BadRequest("Neispravan format datuma ili vremena.");
        }

        // Ne dozvoljava rezervaciju termina koji je već prošao (npr. jučerašnji datum
        // ili današnji, ali sat koji je već bio).
        if (startDateTime < DateTime.Now)
        {
            return BadRequest("Ne možete rezervisati termin koji je već prošao.");
        }

        var service = await _context.Services
            .FirstOrDefaultAsync(s => s.Id == request.ServiceId && s.IsActive == true);

        if (service == null)
        {
            return BadRequest("Ne postoji aktivna prostorija sa tim ID-om.");
        }

        if (request.TableNumber < 1 || request.TableNumber > service.TableCount)
        {
            return BadRequest($"Broj stola mora biti izmedju 1 i {service.TableCount}.");
        }

        // Vreme završetka se računa automatski na osnovu trajanja usluge (npr. 60 min).
        var endDateTime = startDateTime.AddMinutes(service.DurationMinutes);

        // Provera preklapanja: da li za ISTI sto (TableNumber) i ISTU uslugu već postoji
        // zauzet termin (IsAvailable == false) čiji se interval [StartTime, EndTime)
        // vremenski seče sa novim terminom koji korisnik pokušava da rezerviše.
        // Ovo je prva linija odbrane od dvostruke rezervacije; druga linija je
        // UNIQUE indeks u bazi (uq_service_table_start) — vidi catch blok ispod,
        // koji hvata slučaj kad dva zahteva stignu u isto vreme (race condition).
        var hasOverlap = await _context.TimeSlots
            .AnyAsync(t => t.ServiceId == request.ServiceId
                && t.TableNumber == request.TableNumber
                && t.IsAvailable == false
                && t.StartTime < endDateTime
                && startDateTime < t.EndTime);

        if (hasOverlap)
        {
            return Conflict("Taj sto je vec rezervisan u izabranom terminu.");
        }

        // Novi TimeSlot se kreira odmah kao IsAvailable = false, jer je rezervisan
        // u istom trenutku kad i sama Reservation (vidi objašnjenje overlap provere gore).
        var timeSlot = new TimeSlot
        {
            ServiceId = request.ServiceId,
            TableNumber = request.TableNumber,
            StartTime = startDateTime,
            EndTime = endDateTime,
            IsAvailable = false,
            CreatedAt = DateTime.UtcNow
        };

        var reservation = new Reservation
        {
            UserId = userId,
            TimeSlot = timeSlot,
            Status = "confirmed",
            CreatedAt = DateTime.UtcNow
        };

        _context.TimeSlots.Add(timeSlot);
        _context.Reservations.Add(reservation);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Ovde upadamo ako je UNIQUE indeks u bazi (uq_service_table_start) odbio upis —
            // znači da je neko drugi rezervisao isti sto/termin u međuvremenu, posle naše
            // provere gore, ali pre nego što je naš SaveChanges stigao do baze.
            return Conflict("Taj sto je vec rezervisan u izabranom terminu.");
        }

        // Notifikacija se kreira tek NAKON uspešnog upisa rezervacije, jer nam treba
        // reservation.Id koji baza dodeljuje tek posle SaveChangesAsync() iznad.
        var notification = new Notification
        {
            UserId = userId,
            ReservationId = reservation.Id,
            Message = $"Rezervacija je uspešno kreirana: {service.Name}, sto br. {timeSlot.TableNumber}, " +
                      $"{timeSlot.StartTime:dd.MM.yyyy.} u {timeSlot.StartTime:HH:mm}.",
            IsRead = false,
            CreatedAt = DateTime.UtcNow
        };

        _context.Notifications.Add(notification);
        await _context.SaveChangesAsync();

        var result = new ReservationDto
        {
            Id = reservation.Id,
            ServiceName = service.Name,
            TableNumber = timeSlot.TableNumber,
            Date = timeSlot.StartTime.ToString("yyyy-MM-dd"),
            Time = timeSlot.StartTime.ToString("HH:mm"),
            Status = reservation.Status
        };

        return Created($"/api/reservations/{reservation.Id}", result);
    }

    /// <summary>
    /// Otkazuje rezervaciju. Dozvoljeno je samo vlasniku rezervacije ili adminu.
    /// Otkazivanjem se TimeSlot oslobađa (IsAvailable = true), pa taj sto/termin
    /// ponovo postaje dostupan za nove rezervacije.
    /// </summary>
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Cancel(int id)
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);
        var role = User.FindFirstValue(ClaimTypes.Role);

        var reservation = await _context.Reservations
            .Include(r => r.TimeSlot)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
        {
            return NotFound();
        }

        var isOwner = reservation.UserId == userId;
        var isAdmin = string.Equals(role, "admin", StringComparison.OrdinalIgnoreCase);

        if (!isOwner && !isAdmin)
        {
            return Forbid();
        }

        if (reservation.Status == "cancelled")
        {
            return BadRequest("Rezervacija je vec otkazana.");
        }

        reservation.Status = "cancelled";
        reservation.CancelledAt = DateTime.UtcNow;
        reservation.TimeSlot.IsAvailable = true;

        await _context.SaveChangesAsync();

        return NoContent();
    }

    /// <summary>
    /// Admin ruta za ručnu promenu statusa rezervacije (npr. "completed" nakon
    /// odrađene usluge, ili vraćanje iz "cancelled" u "confirmed"). Kad se status
    /// menja NA "cancelled" ili SA "cancelled" na nešto drugo, TimeSlot.IsAvailable
    /// se mora ažurirati u skladu s tim (napomena: pri vraćanju iz "cancelled" nazad
    /// u aktivan status treba postaviti IsAvailable = false, inače sto ostaje
    /// pogrešno označen kao slobodan).
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPut("{id}/status")]
    public async Task<ActionResult<ReservationDto>> UpdateStatus(int id, ReservationStatusUpdateDto request)
    {
        var validStatuses = new[] { "pending", "confirmed", "cancelled", "completed" };

        if (!validStatuses.Contains(request.Status))
        {
            return BadRequest("Nevazeci status. Dozvoljeno: pending, confirmed, cancelled, completed.");
        }

        var reservation = await _context.Reservations
            .Include(r => r.TimeSlot)
                .ThenInclude(t => t.Service)
            .FirstOrDefaultAsync(r => r.Id == id);

        if (reservation == null)
        {
            return NotFound();
        }

        reservation.Status = request.Status;

        if (request.Status == "cancelled")
        {
            reservation.CancelledAt = DateTime.UtcNow;
            reservation.TimeSlot.IsAvailable = true;
        }
        else
        {
            reservation.CancelledAt = null;
            reservation.TimeSlot.IsAvailable = false;
        }

        await _context.SaveChangesAsync();

        var result = new ReservationDto
        {
            Id = reservation.Id,
            ServiceName = reservation.TimeSlot.Service.Name,
            TableNumber = reservation.TimeSlot.TableNumber,
            Date = reservation.TimeSlot.StartTime.ToString("yyyy-MM-dd"),
            Time = reservation.TimeSlot.StartTime.ToString("HH:mm"),
            Status = reservation.Status
        };

        return Ok(result);
    }
}