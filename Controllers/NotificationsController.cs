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
/// <summary>
/// Kontroler za notifikacije prijavljenog korisnika. Same notifikacije se kreiraju
/// na dva mesta van ovog kontrolera: pri kreiranju rezervacije (ReservationsController.Create)
/// i automatski, 12h pre termina, iz pozadinskog servisa (ReservationReminderService).
/// Ovaj kontroler samo čita, označava kao pročitano i briše postojeće notifikacije.
/// </summary>
public class NotificationsController : ControllerBase
{
    private readonly AppDbContext _context;

    public NotificationsController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>Vraća sve notifikacije prijavljenog korisnika, najnovije prve.</summary>
    [Authorize]
    [HttpGet]
    public async Task<ActionResult<IEnumerable<NotificationDto>>> GetAll()
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var notifications = await _context.Notifications
            .Where(n => n.UserId == userId)
            .OrderByDescending(n => n.CreatedAt)
            .Select(n => new NotificationDto
            {
                Id = n.Id,
                Message = n.Message,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return Ok(notifications);
    }

    /// <summary>
    /// Označava notifikaciju kao pročitanu. Provera notification.UserId != userId
    /// sprečava korisnika da (znajući samo Id) označi ili čita tuđu notifikaciju.
    /// </summary>
    [Authorize]
    [HttpPut("{id}/read")]
    public async Task<ActionResult<NotificationDto>> MarkAsRead(int id)
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id);

        if (notification == null)
        {
            return NotFound();
        }

        if (notification.UserId != userId)
        {
            return Forbid();
        }

        notification.IsRead = true;
        await _context.SaveChangesAsync();

        var result = new NotificationDto
        {
            Id = notification.Id,
            Message = notification.Message,
            IsRead = notification.IsRead,
            CreatedAt = notification.CreatedAt
        };

        return Ok(result);
    }

    /// <summary>Trajno briše notifikaciju. Isti vlasnički check kao u MarkAsRead iznad.</summary>
    [Authorize]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var notification = await _context.Notifications.FirstOrDefaultAsync(n => n.Id == id);

        if (notification == null)
        {
            return NotFound();
        }

        if (notification.UserId != userId)
        {
            return Forbid();
        }

        _context.Notifications.Remove(notification);
        await _context.SaveChangesAsync();

        return NoContent();
    }
}