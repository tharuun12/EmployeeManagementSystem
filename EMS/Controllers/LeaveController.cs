using EMS.Models;
using EMS.Web.Data;
using Microsoft.AspNetCore.Authorization;
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

        private async Task<bool> UpdateLeaveBalanceAsync(int employeeId, DateTime startDate, DateTime endDate)
        {
            try
            {
                // Calculate business days for the leave period
                int days = CalculateBusinessDays(startDate, endDate);
                if (days <= 0)
                {
                    return false; 
                }

                var leaveBalance = await EnsureLeaveBalanceExistsAsync(employeeId);
                if (leaveBalance == null)
                {
                    return false;
                }

                var employee = await _context.Employees.FindAsync(employeeId);
                if (employee == null)
                {
                    return false;
                }

                // Check if employee has sufficient leave balance
                if (employee.LeaveBalance < days)
                {
                    return false; 
                }

                leaveBalance.LeavesTaken += days;

                employee.LeaveBalance = Math.Max(0, employee.LeaveBalance - days);

                _context.LeaveBalances.Update(leaveBalance);
                _context.Employees.Update(employee);

                await _context.SaveChangesAsync();

                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// Calculates business days between start and end date (excluding weekends)
        private static int CalculateBusinessDays(DateTime startDate, DateTime endDate)
        {
            if (startDate > endDate)
                return 0;

            int businessDays = 0;
            DateTime current = startDate;

            while (current <= endDate)
            {
                if (current.DayOfWeek != DayOfWeek.Saturday && current.DayOfWeek != DayOfWeek.Sunday)
                {
                    businessDays++;
                }
                current = current.AddDays(1);
            }

            return businessDays;
        }

        /// Ensures LeaveBalance record exists for employee, creates if not found
        private async Task<LeaveBalance?> EnsureLeaveBalanceExistsAsync(int employeeId)
        {
            try
            {
                var existingBalance = await _context.LeaveBalances
                    .FirstOrDefaultAsync(b => b.EmployeeId == employeeId);

                if (existingBalance != null)
                {
                    return existingBalance;
                }

                var employee = await _context.Employees.FindAsync(employeeId);
                if (employee == null)
                {
                    return null;
                }

                var newLeaveBalance = new LeaveBalance
                {
                    EmployeeId = employeeId,
                    TotalLeaves = employee.LeaveBalance, 
                    LeavesTaken = 0
                };

                _context.LeaveBalances.Add(newLeaveBalance);
                await _context.SaveChangesAsync();

                return newLeaveBalance;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // GET: /Leave/Apply
        public IActionResult Apply()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var employee = _context.Employees.FirstOrDefault(e => e.UserId == userId); 

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
                if (leave.StartDate > leave.EndDate)
                {
                    TempData["ToastError"] = "End date must be after start date.";
                    ViewData["Employees"] = await _context.Employees.ToListAsync();
                    return View(leave);
                }

                // Calculate days needed
                int daysRequested = CalculateBusinessDays(leave.StartDate, leave.EndDate);
                if (daysRequested <= 0)
                {
                    TempData["ToastError"] = "Invalid leave period selected.";
                    ViewData["Employees"] = await _context.Employees.ToListAsync();
                    return View(leave);
                }

                var employee = await _context.Employees.FindAsync(leave.EmployeeId);
                if (employee == null)
                {
                    TempData["ToastError"] = "Employee not found.";
                    ViewData["Employees"] = await _context.Employees.ToListAsync();
                    return View(leave);
                }

                if (employee.LeaveBalance < daysRequested)
                {
                    TempData["ToastError"] = $"Insufficient leave balance. You have {employee.LeaveBalance} days available, but requested {daysRequested} days.";
                    ViewData["Employees"] = await _context.Employees.ToListAsync();
                    return View(leave);
                }

                leave.RequestDate = DateTime.UtcNow;
                if (string.IsNullOrEmpty(leave.Status))
                {
                    leave.Status = "Pending";
                }
                else
                {
                    leave.Status = leave.Status != "Approved" ? "Pending" : "Approved";
                }
                _context.LeaveRequests.Add(leave);
                await _context.SaveChangesAsync();

                await EnsureLeaveBalanceExistsAsync(leave.EmployeeId);

                if (leave.Status == "Approved")
                {
                    bool updateSuccess = await UpdateLeaveBalanceAsync(leave.EmployeeId, leave.StartDate, leave.EndDate);
                    if (!updateSuccess)
                    {
                        TempData["ToastError"] = "Failed to update leave balance.";
                    }
                    else
                    {
                        TempData["ToastSuccess"] = "Leave applied and approved successfully!";
                    }
                }
                else
                {
                    TempData["ToastSuccess"] = "Leave application submitted successfully!";
                }

                return RedirectToAction("MyLeaves", new { employeeId = leave.EmployeeId });
            }

            ViewData["Employees"] = await _context.Employees.ToListAsync();
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
        [Authorize(Roles = "Admin, Manager")]
        public async Task<IActionResult> ApproveList()
        {
            var leaves = await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.Status == "Pending")
                .ToListAsync();

            return View(leaves);
        }

        // GET: /Leave/ApproveList
        [Authorize(Roles = "Admin, Manager")]
        public async Task<IActionResult> EmployeeLeaveList()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            var manager = await _context.Employees
                .FirstOrDefaultAsync(e => e.UserId == userId);

            if (manager == null)
            {
                return NotFound("Manager not found.");
            }

            var teamEmployeeIds = await _context.Employees
                .Where(e => e.ManagerId == manager.EmployeeId)
                .Select(e => e.EmployeeId)
                .ToListAsync();

            var leaves = await _context.LeaveRequests
                .Include(l => l.Employee)
                .Where(l => l.Status == "Pending" && teamEmployeeIds.Contains(l.EmployeeId))
                .ToListAsync();

            return View(leaves);
        }

        // GET: /Leave/Approvals/5
        [Authorize(Roles = "Admin, Manager")]
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
        [Authorize(Roles = "Admin, Manager")]
        public async Task<IActionResult> Approvals(int id, string status)
        {
            var leave = await _context.LeaveRequests.FindAsync(id);
            if (leave == null)
                return NotFound();

            string originalStatus = leave.Status;

            if (status == "Approved" && originalStatus != "Approved")
            {
                // Calculate days for this leave
                int daysRequested = CalculateBusinessDays(leave.StartDate, leave.EndDate);

                // Check employee's current balance
                var employee = await _context.Employees.FindAsync(leave.EmployeeId);
                if (employee == null)
                {
                    TempData["ToastError"] = "Employee not found.";
                    return RedirectToAction("ApproveList");
                }

                if (employee.LeaveBalance < daysRequested)
                {
                    TempData["ToastError"] = $"Cannot approve: Employee has insufficient leave balance. Available: {employee.LeaveBalance} days, Requested: {daysRequested} days.";
                    return RedirectToAction("ApproveList");
                }

                await EnsureLeaveBalanceExistsAsync(leave.EmployeeId);

                // Update both Employee and LeaveBalance tables
                bool updateSuccess = await UpdateLeaveBalanceAsync(leave.EmployeeId, leave.StartDate, leave.EndDate);
                if (!updateSuccess)
                {
                    TempData["ToastError"] = "Failed to update leave balance. Please try again.";
                    return RedirectToAction("ApproveList");
                }

                // Update leave status
                leave.Status = "Approved";
                _context.LeaveRequests.Update(leave);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Leave approved successfully and balances updated!";
            }
            else if (status == "Rejected")
            {
                leave.Status = "Rejected";
                _context.LeaveRequests.Update(leave);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Leave rejected successfully!";
            }
            else
            {
                leave.Status = status;
                _context.LeaveRequests.Update(leave);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = $"Leave status updated to {status}!";
            }

            return RedirectToAction(nameof(ApproveList));
        }

    }
}
