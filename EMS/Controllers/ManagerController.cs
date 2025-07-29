using EMS; // Or your actual namespace where IEmailService is
using EMS.ViewModels;
using EMS.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading.Tasks;

namespace EMS.Controllers
{
    public class ManagerController : Controller
    {
        private readonly AppDbContext _context;
        public ManagerController(AppDbContext context)
        {
            _context = context;
        }

        // Email testing 

        //private readonly IEmailService _emailService;

        //public ManagerController(IEmailService emailService)
        //{
        //    _emailService = emailService;
        //}

        //public IActionResult Index()
        //{
        //    return View();
        //}

        //[HttpGet]
        //public async Task<IActionResult> TestEmail()
        //{
        //    await _emailService.SendEmailAsync("tharuunmohan@gmail.com", "Test Email", "This is a test email from EMS.");
        //    return Ok("✅ Test email sent successfully.");
        //}

        public async Task<IActionResult> Index()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            if (string.IsNullOrEmpty(userId))
            {
                return Unauthorized();
            }

            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (employee == null)
            {
                return NotFound("Employee not found for current user.");
            }

            var viewModel = new EmployeeProfileViewModel
            {
                Employee = employee,
            };

            return View(viewModel);
        }

        // GET: /Manager/ApproveList
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> ApproveList()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Get the current logged-in employee (manager)
            var manager = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (manager == null)
            {
                return NotFound("Manager not found.");
            }

            // Get all employees under this manager
            var teamEmployeeIds = await _context.Employees
                .Where(e => e.ManagerId == manager.EmployeeId)
                .Select(e => e.EmployeeId)
                .ToListAsync();

            // Get pending leave requests for these employees
            var leaves = await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.Status == "Pending" && teamEmployeeIds.Contains(l.EmployeeId))
                .ToListAsync();

            return View(leaves);
        }

        // GET: /Manager/Approvals/5
        [Authorize(Roles = "Manager")]
        public async Task<IActionResult> Approvals(int id)
        {
            var leave = await _context.LeaveRequests
                .Include(l => l.Employee)
                .FirstOrDefaultAsync(l => l.LeaveRequestId == id);

            if (leave == null)
                return NotFound();

            return View(leave);
        }

        // POST: /Manager/Approvals/5
        [Authorize(Roles = "Manager")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approvals(int id, string status)
        {
            var leave = await _context.LeaveRequests.FindAsync(id);
            if (leave == null) return NotFound();

            leave.Status = status;

            if (status == "Approved")
            {
                var balance = await _context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == leave.EmployeeId);
                if (balance != null)
                {
                    int days = (leave.EndDate - leave.StartDate).Days + 1;

                    if (balance.LeavesTaken + days > balance.TotalLeaves)
                    {
                        ModelState.AddModelError("", "Insufficient leave balance.");
                        return View(leave);
                    }

                    balance.LeavesTaken += days;
                    await _context.SaveChangesAsync();
                }
            }
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(ApproveList));
        }
    }
}
