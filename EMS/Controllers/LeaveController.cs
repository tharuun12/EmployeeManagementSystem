using EMS.Models;
using EMS.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;


namespace EMS.Web.Controllers
{
    public class LeaveController : Controller
    {
        private readonly AppDbContext _context;

        public LeaveController(AppDbContext context)
        {
            _context = context;
        }

        // GET: /Leave/Apply
        public IActionResult Apply()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Now find the matching employee
            var employee = _context.Employees.FirstOrDefault(e => e.UserId == userId); // Adjust field if you're using Email

            if (employee == null)
            {
                return NotFound("Employee not found.");
            }

            ViewData["Employee"] = employee;
            return View();
        }

        // POST: /Leave/Apply
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(LeaveRequest leave)
        {
            if (ModelState.IsValid)
            {
                _context.LeaveRequests.Add(leave);
                await _context.SaveChangesAsync();

                var employeeId = HttpContext.Session.GetInt32("EmployeeId");
                var employee = await _context.Employees.FirstOrDefaultAsync(e => e.EmployeeId == leave.EmployeeId);
                int dayss = (leave.EndDate - leave.StartDate).Days + 1;

                if (employee == null)
                {
                    var error = new ErrorViewModel
                    {
                        Message = "Employee not found."
                    };
                    return View("Error", error);
                }
                if ( employee.LeaveBalance >= dayss)
                {
                    employee.LeaveBalance -= dayss;
                    await _context.SaveChangesAsync();
                }

                if (leave.Status == "Approved")
                {
                    var balance = await _context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == leave.EmployeeId);
                    if (balance != null)
                    {
                        int days = (leave.EndDate - leave.StartDate).Days + 1;
                        balance.LeavesTaken += days;
                        await _context.SaveChangesAsync();
                    }
                }

                return RedirectToAction("MyLeaves", new { employeeId = leave.EmployeeId });
            }

            ViewData["Employees"] = _context.Employees.ToList();
            return View(leave);
        }


        // GET: /Leave/MyLeaves?employeeId=1
        public async Task<IActionResult> MyLeaves(int employeeId)
        {
            var leaves = await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.EmployeeId == employeeId)
                .OrderByDescending(l => l.RequestDate)
                .ToListAsync();

            ViewBag.EmployeeName = leaves.FirstOrDefault()?.Employee?.FullName ?? "";
            return View(leaves);
        }

        // GET: /Leave/ApproveList
        public async Task<IActionResult> ApproveList()
        {
            var leaves = await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.Status == "Pending")
                .ToListAsync();

            return View(leaves);
        }

        // GET: /Leave/Approvals/5
        public async Task<IActionResult> Approvals(int id)
        {
            var leave = await _context.LeaveRequests
                .Include(l => l.Employee)
                .FirstOrDefaultAsync(l => l.LeaveRequestId == id);
           
            if (leave == null)
                return NotFound();

            return View(leave);
        }

        // POST: /Leave/Approvals/5
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
