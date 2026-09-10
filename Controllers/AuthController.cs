using Bekend.Data;
using Bekend.DTOs;
using Bekend.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Authorization;


namespace Bekend.Controllers;


[ApiController]
[Route("api/[controller]")]


/// <summary>
/// Kontroler za registraciju, prijavu i upravljanje nalogom prijavljenog korisnika
/// (JWT autentifikacija). Ruta: api/Auth.
/// </summary>
public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

    /// <summary>
    /// Registruje novog korisnika sa ulogom "korisnik" (RoleId = 1), heši lozinku
    /// pomoću BCrypt-a (nikad se ne čuva u čistom tekstu) i vraća JWT token
    /// zajedno sa osnovnim podacima o korisniku, kako bi frontend odmah mogao
    /// da ga prijavi bez posebnog login zahteva.
    /// </summary>
    [HttpPost("register")]
    public async Task<ActionResult<AuthResponse>> Register(RegisterRequest request)
    {
        bool emailExists = await _context.Users.AnyAsync(u => u.Email == request.Email);
        if (emailExists)
        {
            return BadRequest("Korisnik sa ovim email-om već postoji.");
        }

        var role = await _context.Roles.FirstAsync(r => r.Id == 1); // "korisnik"

        var user = new User
        {
            FullName = request.FullName,
            Email = request.Email,
            Phone = request.Phone,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.Password),
            RoleId = role.Id, // pretpostavka: 1 = obična uloga "korisnik", proveri u bazi
            IsActive = true
        };

        _context.Users.Add(user);
        await _context.SaveChangesAsync();

        var token = GenerateJwtToken(user, role.Name);

        return Ok(new AuthResponse
        {
            Token = token, // generisanje tokena
            User = new UserDto { Id = user.Id, FullName = user.FullName, Email = user.Email }
        });
    }

    /// <summary>
    /// Proverava kredencijale (BCrypt.Verify upoređuje lozinku sa hešom iz baze)
    /// i, ako su ispravni, izdaje novi JWT token važeći 2 sata.
    /// </summary>
    [HttpPost("login")]
    public async Task<ActionResult<AuthResponse>> Login(LoginRequest request)
    {
        var user = await _context.Users
        .Include(u => u.Role)
        .FirstOrDefaultAsync(u => u.Email == request.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(request.Password, user.PasswordHash))
        {
            return Unauthorized("Pogrešan email ili lozinka.");
        }

        var token = GenerateJwtToken(user, user.Role.Name);

        return Ok(new AuthResponse
        {
            Token = token,
            User = new UserDto { Id = user.Id, FullName = user.FullName, Email = user.Email }
        });
    }

    /// <summary>
    /// Vraća osnovne podatke o trenutno prijavljenom korisniku, pročitane
    /// direktno iz claim-ova unutar JWT tokena (ne iz baze) — korisno frontendu
    /// da posle refresh-a stranice proveri da li je token i dalje validan i ko je ulogovan.
    /// </summary>
    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    
    {
    var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
    var email = User.FindFirstValue(JwtRegisteredClaimNames.Email);
    var role = User.FindFirstValue(ClaimTypes.Role);

    return Ok(new { userId, email, role });
}

    /// <summary>
    /// Menja lozinku prijavljenog korisnika. UserId se uzima iz JWT tokena
    /// (ne iz body-ja zahteva), tako da korisnik ne može promeniti tuđu lozinku.
    /// Stara lozinka se mora tačno poklopiti sa hešom u bazi pre nego što se upiše nova.
    /// </summary>
    [Authorize]
    [HttpPut("change-password")]
    public async Task<IActionResult> ChangePassword(ChangePasswordRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.NewPassword) || request.NewPassword.Length < 6)
        {
            return BadRequest("Nova lozinka mora imati najmanje 6 karaktera.");
        }

        var userId = int.Parse(User.FindFirstValue(JwtRegisteredClaimNames.Sub)!);

        var user = await _context.Users.FirstOrDefaultAsync(u => u.Id == userId);

        if (user == null)
        {
            return NotFound();
        }

        if (!BCrypt.Net.BCrypt.Verify(request.OldPassword, user.PasswordHash))
        {
            return BadRequest("Stara lozinka nije ispravna.");
        }

        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
        await _context.SaveChangesAsync();

        return Ok(new { message = "Lozinka je uspešno promenjena." });
    }

    /// <summary>
    /// Test ruta koja služi samo za proveru da li [Authorize(Roles = "admin")]
    /// ispravno propušta admine, a odbija (403) sve ostale ulogovane korisnike.
    /// </summary>
    [Authorize(Roles = "admin")]
    [HttpGet("admin-only")]
    public IActionResult AdminOnly()
    {
        return Ok(new { poruka = "Ako vidiš ovo, ti si admin." });
    }

    /// <summary>
    /// Kreira potpisan JWT token za datog korisnika.
    /// Sub claim (JwtRegisteredClaimNames.Sub, kratko ime "sub") nosi korisnikov Id
    /// i čita se u kontrolerima preko User.FindFirstValue(JwtRegisteredClaimNames.Sub).
    /// Role claim koristi ClaimTypes.Role jer to [Authorize(Roles = "...")] očekuje po defaultu.
    /// Token važi 2 sata (DateTime.UtcNow.AddHours(2)) — posle toga frontend mora ponovo login.
    /// </summary>
    private string GenerateJwtToken(User user, string roleName)
{
    var jwtKey = _configuration["Jwt:Key"]!;
    var jwtIssuer = _configuration["Jwt:Issuer"]!;
    var jwtAudience = _configuration["Jwt:Audience"]!;

    var claims = new List<Claim>
    {
        new Claim(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
        new Claim(JwtRegisteredClaimNames.Email, user.Email),
        new Claim(ClaimTypes.Role, roleName)
    };

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey));
    var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

    var token = new JwtSecurityToken(
        issuer: jwtIssuer,
        audience: jwtAudience,
        claims: claims,
        expires: DateTime.UtcNow.AddHours(2),
        signingCredentials: creds
    );

    return new JwtSecurityTokenHandler().WriteToken(token);
}
}