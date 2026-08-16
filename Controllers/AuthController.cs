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


public class AuthController : ControllerBase
{
    private readonly AppDbContext _context;
    private readonly IConfiguration _configuration;

    public AuthController(AppDbContext context, IConfiguration configuration)
    {
        _context = context;
        _configuration = configuration;
    }

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

    [Authorize]
    [HttpGet("me")]
    public IActionResult Me()
    {
    var userId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);
    var email = User.FindFirstValue(JwtRegisteredClaimNames.Email);
    var role = User.FindFirstValue(ClaimTypes.Role);

    return Ok(new { userId, email, role });
}

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