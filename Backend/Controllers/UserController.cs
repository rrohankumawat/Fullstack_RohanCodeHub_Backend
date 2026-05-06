using Backend.Models;
using Backend.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserController : ControllerBase
    {
        private readonly AppDbContext _context;
        private readonly TokenService _tokenService;

        public UserController(AppDbContext context, TokenService tokenService)
        {
            _context = context;
            _tokenService = tokenService;
        }

        public record RegisterRequest(string Email, string Password);
        public record LoginRequest(string Email, string Password);
        public record RefreshRequest(string RefreshToken);

        // ── REGISTER ──────────────────────────────────────────────────────────
        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Email and password are required.", status = false });

            var email = request.Email.Trim().ToLowerInvariant();

            if (await _context.Users.AnyAsync(u => u.Email != null && u.Email.ToLower() == email))
                return Conflict(new { message = "A user with that email already exists.", status = false });

            var user = new User
            {
                Email = email,
                Password = HashPassword(request.Password)
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration Successful!", status = true });
        }

        // ── LOGIN ─────────────────────────────────────────────────────────────
        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
                return BadRequest(new { message = "Email and password are required.", status = false });

            var email = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

            if (user is null || !VerifyPassword(user.Password ?? string.Empty, request.Password))
                return Unauthorized(new { message = "Invalid credentials.", status = false });

            var accessToken = _tokenService.GenerateAccessToken(user);
            var refreshToken = _tokenService.GenerateRefreshToken(user.Id);

            await _context.RefreshTokens.AddAsync(refreshToken);
            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Login Successful!",
                status = true,
                accessToken,
                refreshToken = refreshToken.Token,
                expiresIn = 15 * 60  // seconds
            });
        }

        // ── REFRESH ───────────────────────────────────────────────────────────
        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { message = "Refresh token is required.", status = false });

            var stored = await _context.RefreshTokens
                .Include(rt => rt.User)
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (stored is null || stored.IsRevoked || stored.ExpiresAt < DateTime.UtcNow)
                return Unauthorized(new { message = "Invalid or expired refresh token.", status = false });

            // Rotate: revoke old, issue new
            stored.IsRevoked = true;

            var newRefreshToken = _tokenService.GenerateRefreshToken(stored.UserId);
            await _context.RefreshTokens.AddAsync(newRefreshToken);
            await _context.SaveChangesAsync();

            var newAccessToken = _tokenService.GenerateAccessToken(stored.User);

            return Ok(new
            {
                message = "Token refreshed.",
                status = true,
                accessToken = newAccessToken,
                refreshToken = newRefreshToken.Token,
                expiresIn = 15 * 60
            });
        }

        // ── LOGOUT ────────────────────────────────────────────────────────────
        [Authorize]
        [HttpPost("logout")]
        public async Task<IActionResult> Logout([FromBody] RefreshRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.RefreshToken))
                return BadRequest(new { message = "Refresh token is required.", status = false });

            var stored = await _context.RefreshTokens
                .FirstOrDefaultAsync(rt => rt.Token == request.RefreshToken);

            if (stored is not null && !stored.IsRevoked)
            {
                stored.IsRevoked = true;
                await _context.SaveChangesAsync();
            }

            return Ok(new { message = "Logged out successfully.", status = true });
        }

        // ── PROTECTED EXAMPLE ─────────────────────────────────────────────────
        [Authorize]
        [HttpGet("me")]
        public IActionResult Me()
        {
            var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value
                      ?? User.FindFirst("sub")?.Value;
            var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value
                     ?? User.FindFirst("email")?.Value;

            return Ok(new { userId, email, status = true });
        }

        // ── HELPERS ───────────────────────────────────────────────────────────
        private static string HashPassword(string password)
        {
            const int saltSize = 16, hashSize = 32, iterations = 100_000;
            var salt = new byte[saltSize];
            RandomNumberGenerator.Fill(salt);
            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(pbkdf2.GetBytes(hashSize))}";
        }

        private static bool VerifyPassword(string stored, string provided)
        {
            if (string.IsNullOrEmpty(stored)) return false;
            var parts = stored.Split('.', 3);
            if (parts.Length != 3 || !int.TryParse(parts[0], out var iterations)) return false;
            var salt = Convert.FromBase64String(parts[1]);
            var storedHash = Convert.FromBase64String(parts[2]);
            using var pbkdf2 = new Rfc2898DeriveBytes(provided, salt, iterations, HashAlgorithmName.SHA256);
            return System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
                storedHash, pbkdf2.GetBytes(storedHash.Length));
        }
    }
}