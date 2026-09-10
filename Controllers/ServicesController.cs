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
/// Kontroler za usluge/prostorije koje se mogu rezervisati (npr. sala, teren).
/// Svaka Service ima TableCount — broj dostupnih "stolova" (jedinica) unutar te usluge,
/// koji ReservationsController koristi da proveri da li je izabrani broj stola validan.
/// </summary>
public class ServicesController : ControllerBase
{
    private readonly AppDbContext _context;

    public ServicesController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Javna ruta (bez [Authorize]) — vraća sve aktivne usluge. Gost može da ih vidi
    /// bez prijave, u skladu sa zahtevom da neprijavljeni korisnik samo pregleda ponudu.
    /// </summary>
    [HttpGet]
    public async Task<ActionResult<IEnumerable<ServiceDto>>> GetAll()
    {
        var services = await _context.Services
            .Where(s => s.IsActive == true)
            .Select(s => new ServiceDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                DurationMinutes = s.DurationMinutes,
                Price = s.Price,
                TableCount = s.TableCount
            })
            .ToListAsync();

        return Ok(services);
    }

    /// <summary>Vraća pojedinačnu aktivnu uslugu po Id-u, ili 404 ako ne postoji/nije aktivna.</summary>
    [HttpGet("{id}")]
    public async Task<ActionResult<ServiceDto>> GetById(int id)
    {
        var service = await _context.Services
            .Where(s => s.Id == id && s.IsActive == true)
            .Select(s => new ServiceDto
            {
                Id = s.Id,
                Name = s.Name,
                Description = s.Description,
                DurationMinutes = s.DurationMinutes,
                Price = s.Price,
                TableCount = s.TableCount
            })
            .FirstOrDefaultAsync();

                if (service == null)
        {
            return NotFound();
        }

        return Ok(service);
    }

    /// <summary>Admin: kreira novu uslugu, uvek kao aktivnu (IsActive = true).</summary>
    [Authorize(Roles = "admin")]
    [HttpPost]
    public async Task<ActionResult<ServiceDto>> Create(ServiceCreateDto request)
    {
        var service = new Service
        {
            Name = request.Name,
            Description = request.Description,
            DurationMinutes = request.DurationMinutes,
            Price = request.Price,
            TableCount = request.TableCount,
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        _context.Services.Add(service);
        await _context.SaveChangesAsync();

        var result = new ServiceDto
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            DurationMinutes = service.DurationMinutes,
            Price = service.Price,
            TableCount = service.TableCount
        };

        return CreatedAtAction(nameof(GetById), new { id = service.Id }, result);
    }

    /// <summary>Admin: menja podatke postojeće usluge (naziv, opis, trajanje, cenu, broj stolova).</summary>
    [Authorize(Roles = "admin")]
    [HttpPut("{id}")]
    public async Task<ActionResult<ServiceDto>> Update(int id, ServiceCreateDto request)
    {
        var service = await _context.Services.FirstOrDefaultAsync(s => s.Id == id);

        if (service == null)
        {
            return NotFound();
        }

        service.Name = request.Name;
        service.Description = request.Description;
        service.DurationMinutes = request.DurationMinutes;
        service.Price = request.Price;
        service.TableCount = request.TableCount;

        await _context.SaveChangesAsync();

        var result = new ServiceDto
        {
            Id = service.Id,
            Name = service.Name,
            Description = service.Description,
            DurationMinutes = service.DurationMinutes,
            Price = service.Price,
            TableCount = service.TableCount
        };

        return Ok(result);
    }

    /// <summary>
    /// Admin: "briše" uslugu, ali samo soft-delete-om (IsActive = false), a ne
    /// stvarnim brisanjem reda iz baze. Time se čuvaju stare rezervacije koje se
    /// referenciraju na ovu uslugu i izbegava se greška strane veze (foreign key)
    /// pri pokušaju brisanja usluge koja već ima rezervacije.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(int id)
    {
        var service = await _context.Services.FirstOrDefaultAsync(s => s.Id == id);

        if (service == null)
        {
            return NotFound();
        }

        service.IsActive = false;
        await _context.SaveChangesAsync();

        return NoContent();
    }
}