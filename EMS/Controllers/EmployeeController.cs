using EMS.Models;
using EMS.ViewModels;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;

namespace EMS.Web.Controllers
{
    public class EmployeeController : Controller
    {
        private readonly AppDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;
        private readonly UserManager<Users> _userManager;


        public EmployeeController(AppDbContext context, RoleManager<IdentityRole> roleManager, UserManager<Users> userManager)
        {
            _context = context;
            _roleManager = roleManager;
            _userManager = userManager;
        }

        // Employee/Index - Get
        public async Task<IActionResult> EmployeeList()
        {
            var employees = await _context.Employees
                .Include(e => e.Department)
                .ToListAsync();
            return View(employees);
        }

        // Employee/Create - Get
        public async Task<IActionResult> Create()
        {
            ViewBag.Departments = await _context.Department.ToListAsync();
            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
            return View();
        }

        
        private async Task LoadDropdownsAsync()
        {
            ViewBag.Departments = await _context.Department.ToListAsync();
            ViewBag.Managers = await _context.Employees.ToListAsync();
            ViewBag.Roles = (await _roleManager.Roles.ToListAsync()).Select(r => r.Name).ToList(); // just role names
        }
        
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmployeeViewModel model)
        {
            if (ModelState.IsValid)
            {
                var normalizedEmail = model.Email.Trim().ToLower();

                var currentEmployee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email.ToLower().Trim() == normalizedEmail);

                if (currentEmployee != null)
                {
                    TempData["ToastMessage"] = "A user with this Email already exists.";
                    await LoadDropdownsAsync();
                    return View(model);
                }
                if (model.Role == "Employee")
                {
                    var departmentCheck = await _context.Department.FirstOrDefaultAsync(e => e.DepartmentId == model.DepartmentId);
                    if (departmentCheck?.ManagerId == null)
                    {
                        var managerCheck = await _context.Employees.FirstOrDefaultAsync(e => e.Role == "Admin");

                        TempData["ToastMessage"] = "PLease create the Manager for the departement";
                        await LoadDropdownsAsync();
                        return View(model);
                    }
                }
                else if (model.Role == "Manager")
                {
                    var adminCount = await _context.Employees.CountAsync(e => e.Role == "Admin");

                    if (adminCount < 1)
                    {
                        TempData["ToastMessage"] = "Please create an Admin first to proceed.";
                        await LoadDropdownsAsync();
                        return View(model);
                    }
                }


                var role = await _roleManager.FindByNameAsync(model.Role);
                var department = await _context.Department
                    .FirstOrDefaultAsync(e => e.DepartmentId == model.DepartmentId);


                if (role == null)
                {
                    TempData["ToastMessage"] = "Selected role is invalid.";

                    //ModelState.AddModelError("Role", "Selected role is invalid.");
                    await LoadDropdownsAsync();
                    return View(model);
                }

                var employee = new Employee
                {
                    FullName = model.FullName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber,
                    Role = model.Role,
                    RoleID = role?.Id,
                    IsActive = model.IsActive,
                    DepartmentId = model.DepartmentId,
                    ManagerId = department?.ManagerId,
                    LeaveBalance = model.LeaveBalance
                };

                _context.Employees.Add(employee);
                await _context.SaveChangesAsync();

                var employeeIdValue = await _context.Employees
                    .FirstOrDefaultAsync(e => e.Email == model.Email);

                var employeeLogValue = new EmployeeLog
                {
                    EmployeeId = employeeIdValue.EmployeeId,
                    FullName = model.FullName,
                    Email = model.Email,
                    PhoneNumber = model.PhoneNumber,
                    Role = model.Role,
                    RoleID = employeeIdValue.RoleID,
                    IsActive = model.IsActive,
                    DepartmentId = model.DepartmentId,
                    ManagerId = model?.ManagerId,
                    Operation = "Created",
                    TimeStamp = DateTime.Now
                };
                if (department != null)
                {
                    department.ManagerName = model.FullName;
                    department.ManagerId = employeeIdValue.EmployeeId;

                    await _context.SaveChangesAsync(); 
                }
                _context.EmployeeLog.Add(employeeLogValue);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Employee created successfully!";
                return RedirectToAction(nameof(Index));
            }

            await LoadDropdownsAsync();
            return View(model);
        }

        //Employee/Edit/1 - Get
        public async Task<IActionResult> Edit(int id)
        {
            var employee = await _context.Employees.FindAsync(id);
            if (employee == null)
                return NotFound();

            ViewBag.Departments = await _context.Department.ToListAsync();
            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
            return View(employee);
        }

        // Employee/Edit/EmployeeId - Post
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Employee employee)
        {
            if (id != employee.EmployeeId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var employeeData = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == employee.EmployeeId);

                    if (employeeData == null)
                        return NotFound();

                    // Update only the fields that changed
                    employeeData.FullName = employee.FullName;
                    employeeData.Email = employee.Email;
                    employeeData.PhoneNumber = employee.PhoneNumber;
                    employeeData.Role = employee.Role;
                    employeeData.RoleID = employee.RoleID;
                    employeeData.DepartmentId = employee.DepartmentId;
                    employeeData.ManagerId = employee.ManagerId;
                    employeeData.IsActive = employee.IsActive;

                    // Optional: If department changed, update it
                    if (employeeData.DepartmentId != employee.DepartmentId)
                    {
                        var department = await _context.Department
                            .FirstOrDefaultAsync(d => d.DepartmentId == employee.DepartmentId);

                        if (department != null)
                        {
                            department.ManagerName = employee.FullName;
                            department.ManagerId = employee.EmployeeId;
                        }
                    }

                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Employees.Any(e => e.EmployeeId == id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(EmployeeList));
            }

            ViewBag.Departments = await _context.Department.ToListAsync();
            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
            return View(employee);
        }


        // Employee/Delete/EmployeeId - Get
        public async Task<IActionResult> Delete(int id)
        {
            var employee = await _context.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(m => m.EmployeeId == id);

            if (employee == null)
                return NotFound();

            return View(employee);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var employee = await _context.Employees.FindAsync(id);

            if (employee != null)
            {
                // Step 1: Lock the user
                var user = await _userManager.FindByEmailAsync(employee.Email);
                if (user != null)
                {
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                    await _userManager.UpdateAsync(user);
                }

                // Step 2: Unassign the employee as Manager in departments
                var departments = await _context.Department
                    .Where(d => d.ManagerId == employee.EmployeeId)
                    .ToListAsync();

                foreach (var dept in departments)
                {
                    dept.ManagerId = null;
                    dept.ManagerName = null;
                }

                // Step 3: Delete employee
                _context.Employees.Remove(employee);

                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(EmployeeList));
        }



        // GET: Employee/Filter
        public async Task<IActionResult> Filter(int? departmentId, string? role)
        {
            var query = _context.Employees.Include(e => e.Department).AsQueryable();

            if (departmentId.HasValue)
                query = query.Where(e => e.DepartmentId == departmentId.Value);

            if (!string.IsNullOrEmpty(role))
                query = query.Where(e => e.Role == role);

            var departments = await _context.Department.ToListAsync();
            var roles = await _context.Employees.Select(e => e.Role).Distinct().ToListAsync();

            ViewBag.Departments = departments;
            ViewBag.Roles = roles;

            return View(await query.ToListAsync());
        }

        // GET: /Employee/MyLeaves
        public async Task<IActionResult> MyLeaves()
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);

            // Optional: Get the employee Id from Users table if needed
            var employee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId.ToString() == userId);

            if (employee == null)
                return NotFound("Employee record not found.");

            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

            var leaves = await _context.LeaveRequests
                .Where(l => l.EmployeeId == employee.EmployeeId &&
                            l.StartDate.Month == currentMonth &&
                            l.StartDate.Year == currentYear)
                .ToListAsync();

            return View(leaves);
        }
        public async Task<IActionResult> ManagersList()
        {
            var managers = await _context.Employees
                .Where(e => e.Role == "Manager")
                .Select(e => new ManagerDetailsViewModel
                {
                    EmployeeId = e.EmployeeId,
                    FullName = e.FullName,
                    Email = e.Email,
                    DepartmentId = _context.Department
                                   .Where(d => d.ManagerId == e.EmployeeId)
                                   .Select(d => d.DepartmentId)
                                   .FirstOrDefault(),
                    DepartmentName = _context.Department
                                     .Where(d => d.ManagerId == e.EmployeeId)
                                     .Select(d => d.DepartmentName)
                                     .FirstOrDefault()
                })
                .ToListAsync();

            return View(managers);
        }
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

            string managerName = "N/A";
            if (employee.ManagerId.HasValue)
            {
                var manager = await _context.Employees
                    .FirstOrDefaultAsync(e => e.EmployeeId == employee.ManagerId);
                managerName = manager?.FullName ?? "N/A";
            }

            var viewModel = new EmployeeProfileViewModel
            {
                Employee = employee,
                ManagerName = managerName
            };

            return View(viewModel);
        }






    }
}
