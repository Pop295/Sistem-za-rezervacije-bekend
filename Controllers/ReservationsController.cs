using Bekend.Data;
using Bekend.DTOs;
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
                ServiceName = r.TimeSlot.Service.Name
            })
            .ToListAsync();

        var result = rawReservations
            .Select(r => new ReservationDto
            {
                Id = r.Id,
                ServiceName = r.ServiceName,
                Date = r.StartTime.ToString("yyyy-MM-dd"),
                Time = r.StartTime.ToString("HH:mm"),
                Status = r.Status
            })
            .ToList();

        return Ok(result);
    }
}