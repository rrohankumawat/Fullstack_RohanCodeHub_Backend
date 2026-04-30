using Backend.Models;
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

        public UserController(AppDbContext context) => _context = context;

        public record RegisterRequest(string Email, string Password);
        public record LoginRequest(string Email, string Password);

        [HttpPost("register")]
        public async Task<IActionResult> Register([FromBody] RegisterRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." , status = false});
            }

            var email = request.Email.Trim().ToLowerInvariant();

            var exists = await _context.Users
                .AnyAsync(u => u.Email != null && u.Email.ToLower() == email);

            if (exists)
            {
                return Conflict(new { message = "A user with that email already exists." , status = false});
            }

            var hashed = HashPassword(request.Password);

            var user = new User
            {
                Email = email,
                Password = hashed
            };

            await _context.Users.AddAsync(user);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registrations Successful!", status = true });
        }

        [HttpPost("login")]
        public async Task<IActionResult> Login([FromBody] LoginRequest request)
        {
            if (request is null || string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.Password))
            {
                return BadRequest(new { message = "Email and password are required." , status = false});
            }

            var email = request.Email.Trim().ToLowerInvariant();

            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Email != null && u.Email.ToLower() == email);

            if (user is null)
            {
                // Do not reveal whether the email exists
                return Unauthorized(new { message = "Invalid credentials." , status = false});
            }

            var verified = VerifyPassword(user.Password ?? string.Empty, request.Password);
            if (!verified)
            {
                return Unauthorized(new { message = "Invalid credentials.", status = false });
            }

            // Authentication succeeded. For now return basic user info.
            // If you need JWTs, add authentication services and return a token here.
            return Ok(new { message = "Login Successfully!", status = true });
        }

        private static string HashPassword(string password)
        {
            const int saltSize = 16;
            const int hashSize = 32;
            const int iterations = 100_000;

            var salt = new byte[saltSize];
            RandomNumberGenerator.Fill(salt);

            using var pbkdf2 = new Rfc2898DeriveBytes(password, salt, iterations, HashAlgorithmName.SHA256);
            var hash = pbkdf2.GetBytes(hashSize);

            // Store as: iterations.saltBase64.hashBase64
            return $"{iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
        }

        private static bool VerifyPassword(string stored, string providedPassword)
        {
            if (string.IsNullOrEmpty(stored))
            {
                return false;
            }

            var parts = stored.Split('.', 3);
            if (parts.Length != 3)
            {
                return false;
            }

            if (!int.TryParse(parts[0], out var iterations))
            {
                return false;
            }

            var salt = Convert.FromBase64String(parts[1]);
            var storedHash = Convert.FromBase64String(parts[2]);

            using var pbkdf2 = new Rfc2898DeriveBytes(providedPassword, salt, iterations, HashAlgorithmName.SHA256);
            var computedHash = pbkdf2.GetBytes(storedHash.Length);

            return CryptographicOperations.FixedTimeEquals(storedHash, computedHash);
        }
    }
}
