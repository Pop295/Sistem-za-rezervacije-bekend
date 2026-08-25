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
public class ReservationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public ReservationsController(AppDbContext context)
    {
        _context = context;
    }

    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ReservationDto>>> GetAll()
    {
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

        [Authorize]
    [HttpPost]
    public async Task<ActionResult<ReservationDto>> Create(ReservationCreateDto request)
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        if (!DateTime.TryParse($"{request.Date} {request.StartTime}", out var startDateTime))
        {
            return BadRequest("Neispravan format datuma ili vremena.");
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

        var endDateTime = startDateTime.AddMinutes(service.DurationMinutes);

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
            return Conflict("Taj sto je vec rezervisan u izabranom terminu.");
        }

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