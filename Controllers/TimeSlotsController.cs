using Bekend.Data;
using Bekend.DTOs;
using Bekend.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bekend.Controllers;

[ApiController]
[Route("api/[controller]")]
/// <summary>
/// Kontroler za pregled i (admin) ručno upravljanje terminima (TimeSlot).
/// Napomena: ReservationsController.Create takođe kreira TimeSlot zapise
/// automatski pri svakoj rezervaciji, pa admin obično ne mora ručno da ih dodaje —
/// POST/PUT ovde ostaju korisni za izuzetne slučajeve (npr. blokiranje termina
/// bez stvarne rezervacije). GET je javna ruta jer je koristi kalendar dostupnosti
/// na frontendu i za goste, ne samo za prijavljene korisnike.
/// </summary>
public class TimeSlotsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TimeSlotsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Vraća sve termine (i slobodne i zauzete) iz baze, bez filtriranja po datumu.</summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimeSlotDto>>> GetAll()
    {
        // Korak 1: povlacimo samo sirove kolone iz baze (SQL upit)
        var rawSlots = await _context.TimeSlots
            .Select(t => new
            {
                t.Id,
                t.ServiceId,
                t.TableNumber,
                t.StartTime,
                t.IsAvailable
            })
            .ToListAsync();

        // Korak 2: formatiramo StartTime u odvojene Date/Time stringove (u memoriji, ne u SQL-u)
        var result = rawSlots
            .Select(t => new TimeSlotDto
            {
                Id = t.Id,
                ServiceId = t.ServiceId,
                TableNumber = t.TableNumber,
                Date = t.StartTime.ToString("yyyy-MM-dd"),
                Time = t.StartTime.ToString("HH:mm"),
                IsAvailable = t.IsAvailable ?? false
            })
            .ToList();

        return Ok(result);
    }

    /// <summary>
    /// Admin: ručno kreira termin za dati sto. Isti UNIQUE indeks u bazi
    /// (uq_service_table_start) koji štiti ReservationsController.Create štiti i ovu
    /// rutu od pravljenja dva termina za isti sto u istom trenutku (vidi catch ispod).
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<TimeSlotDto>> Create(TimeSlotCreateDto request)
    {
        if (request.EndTime <= request.StartTime)
        {
            return BadRequest("Vreme zavrsetka mora biti posle vremena pocetka.");
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

        var timeSlot = new TimeSlot
        {
            ServiceId = request.ServiceId,
            TableNumber = request.TableNumber,
            StartTime = request.StartTime,
            EndTime = request.EndTime,
            IsAvailable = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.TimeSlots.Add(timeSlot);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Taj sto je vec rezervisan u to vreme pocetka.");
        }

        var result = new TimeSlotDto
        {
            Id = timeSlot.Id,
            ServiceId = timeSlot.ServiceId,
            TableNumber = timeSlot.TableNumber,
            Date = timeSlot.StartTime.ToString("yyyy-MM-dd"),
            Time = timeSlot.StartTime.ToString("HH:mm"),
            IsAvailable = timeSlot.IsAvailable ?? false
        };

        return Created($"/api/timeslots/{timeSlot.Id}", result);
    }

    /// <summary>Admin: menja podatke postojećeg termina (uslugu, sto, vreme).</summary>
    [Authorize(Roles = "admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<TimeSlotDto>> Update(int id, TimeSlotCreateDto request)
    {
        var timeSlot = await _context.TimeSlots.FirstOrDefaultAsync(t => t.Id == id);

        if (timeSlot == null)
        {
            return NotFound();
        }

        if (request.EndTime <= request.StartTime)
        {
            return BadRequest("Vreme zavrsetka mora biti posle vremena pocetka.");
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

        timeSlot.ServiceId = request.ServiceId;
        timeSlot.TableNumber = request.TableNumber;
        timeSlot.StartTime = request.StartTime;
        timeSlot.EndTime = request.EndTime;

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Taj sto je vec rezervisan u to vreme pocetka.");
        }

        var result = new TimeSlotDto
        {
            Id = timeSlot.Id,
            ServiceId = timeSlot.ServiceId,
            TableNumber = timeSlot.TableNumber,
            Date = timeSlot.StartTime.ToString("yyyy-MM-dd"),
            Time = timeSlot.StartTime.ToString("HH:mm"),
            IsAvailable = timeSlot.IsAvailable ?? false
        };

        return Ok(result);
    }

    /// <summary>
    /// Admin: trajno briše termin (nije soft-delete). Ako termin ima vezanu
    /// rezervaciju, baza vraća grešku strane veze koju hvatamo kao 409 Conflict.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var timeSlot = await _context.TimeSlots.FirstOrDefaultAsync(t => t.Id == id);

        if (timeSlot == null)
        {
            return NotFound();
        }

        _context.TimeSlots.Remove(timeSlot);

        try
        {
            await _context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            return Conflict("Termin ne moze biti obrisan jer postoji rezervacija vezana za njega.");
        }

        return NoContent();
    }
}