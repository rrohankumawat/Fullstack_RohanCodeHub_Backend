using Backend.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using System.Collections.Generic;

namespace Backend.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class EmployeeController(AppDbContext context) : ControllerBase
    {
        private readonly AppDbContext _context = context;

        // GET: api/Employee
        // Supports optional pagination and search:
        // ?page=1&pageSize=20&search=alice&sortBy=Name&desc=true
        [HttpGet("GetAll")]
        public async Task<ActionResult<object>> GetAll()
        {
            var list = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .ToListAsync();
            return Ok(list);
        }

        // GET: api/Employee/list
        // Returns minimal projection useful for dropdowns/autocomplete
        [HttpGet("GetList")]
        public async Task<ActionResult<IEnumerable<object>>> GetList(CancellationToken cancellationToken = default)
        {
            var items = await _context.Employees
                .AsNoTracking()
                .OrderBy(e => e.Name)
                .Select(e => new { e.Id, e.Name })
                .ToListAsync(cancellationToken);

            return Ok(items);
        }

        // GET: api/Employee/5
        [HttpGet("GetById/{id:int}")]
        public async Task<ActionResult<Employee>> GetById(int id, CancellationToken cancellationToken = default)
        {
            var employee = await _context.Employees
                .AsNoTracking()
                .FirstOrDefaultAsync(e => e.Id == id, cancellationToken);

            if (employee == null)
                return Ok(new { message = "Employee Not Found", status = false });

            return Ok(employee);
        }

        // POST: api/Employee
        [HttpPost("CreateEmployee")]
        public async Task<ActionResult<Employee>> Create([FromBody] Employee employee, CancellationToken cancellationToken = default)
        {
            if (employee == null)
                return Ok(new { message = "Bad Request", status = false });

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new { message = "Employee Details has been saved!", status = true });
        }

        // PUT: api/Employee/5
        [HttpPut("UpdateEmployee/{id:int}")]
        public async Task<IActionResult> Update(int id, [FromBody] Employee employee, CancellationToken cancellationToken = default)
        {
            if (employee == null || id != employee.Id)
                return Ok(new { message = "Bad Request", status = false });

            var existing = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (existing == null)
                return Ok(new { message="Employee Not Found", status = false });

            // Map updatable fields
            existing.Name = employee.Name;
            existing.Email = employee.Email;
            existing.Phone = employee.Phone;
            existing.City = employee.City;

            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new {message="Employee Details has been updated!", status = true });
        }

        // DELETE: api/Employee/5
        [HttpDelete("DeleteEmployee/{id:int}")]
        public async Task<IActionResult> Delete(int id, CancellationToken cancellationToken = default)
        {
            var existing = await _context.Employees.FirstOrDefaultAsync(e => e.Id == id, cancellationToken);
            if (existing == null)
                return NotFound();

            _context.Employees.Remove(existing);
            await _context.SaveChangesAsync(cancellationToken);

            return Ok(new {message="Employee record is deleted successfully!", status = true });
        }
    }
}
