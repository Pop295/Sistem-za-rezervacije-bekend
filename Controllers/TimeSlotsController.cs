using Bekend.Data;
using Bekend.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Bekend.Controllers;

[ApiController]
[Route("api/[controller]")]
public class TimeSlotsController : ControllerBase
{
    private readonly AppDbContext _context;

    public TimeSlotsController(AppDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<ActionResult<IEnumerable<TimeSlotDto>>> GetAll()
    {
        // Korak 1: povlacimo samo sirove kolone iz baze (SQL upit)
        var rawSlots = await _context.TimeSlots
            .Select(t => new
            {
                t.Id,
                t.ServiceId,
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
                Date = t.StartTime.ToString("yyyy-MM-dd"),
                Time = t.StartTime.ToString("HH:mm"),
                IsAvailable = t.IsAvailable ?? false
            })
            .ToList();

        return Ok(result);
    }
}