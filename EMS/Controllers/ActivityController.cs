using EMS.Models;
using EMS.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Threading.Tasks;

namespace EMS.Web.Controllers
{
    public class ActivityController : Controller
    {
        private readonly AppDbContext _context;

        public ActivityController(AppDbContext context)
        {
            _context = context;
        }

        // View list of all employees with action buttons
        public async Task<IActionResult> Index()
        {
            var employees = await _context.Employees.ToListAsync();
            return View(employees);
        }

        // View login history of a specific user by Email 
        public async Task<IActionResult> LoginHistory(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId is required");

            var logins = await _context.LoginActivityLogs
                .Where(log => log.Email == userId)
                .OrderByDescending(log => log.LoginTime)
                .ToListAsync();

            ViewBag.UserEmail = userId;
            return View(logins);
        }

        // View recent activity of a specific user 
        public async Task<IActionResult> RecentActivity(string userId)
        {
            if (string.IsNullOrEmpty(userId))
                return BadRequest("UserId is required");

            var activities = await _context.UserActivityLogs
                .Where(act => act.UserId == userId)
                .OrderByDescending(act => act.AccessedAt)
                .ToListAsync();

            ViewBag.UserEmail = userId;
            return View(activities);
        }
    }
}
