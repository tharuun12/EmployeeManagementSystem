using EMS.Models;
using EMS.ViewModels;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Authorization;
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
        [Authorize(Roles = "Admin, Manager")]
        public async Task<IActionResult> EmployeeList()
        {
            var employees = await _context.Employees
                .Include(e => e.Department)
                .ToListAsync();
            return View(employees);
        }

        [Authorize(Roles = "Admin")]
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
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(EmployeeViewModel model)
        {
            if (!ModelState.IsValid)
            {
                await LoadDropdownsAsync();
                return View(model);
            }

            var normalizedEmail = model.Email.Trim().ToLower();

            var existingEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email.ToLower().Trim() == normalizedEmail);

            if (existingEmployee != null)
            {
                TempData["ToastMessage"] = "A user with this Email already exists.";
                await LoadDropdownsAsync();
                return View(model);
            }

            // Validation: Role & Department dependency
            if (model.Role == "Employee")
            {
                var department = await _context.Department
                    .FirstOrDefaultAsync(d => d.DepartmentId == model.DepartmentId);

                if (department?.ManagerId == null)
                {
                    TempData["ToastMessage"] = "Please assign a Manager to the Department first.";
                    await LoadDropdownsAsync();
                    return View(model);
                }
            }
            else if (model.Role == "Manager")
            {
                var adminExists = await _context.Employees.AnyAsync(e => e.Role == "Admin");

                if (!adminExists)
                {
                    TempData["ToastMessage"] = "Please create an Admin before adding a Manager.";
                    await LoadDropdownsAsync();
                    return View(model);
                }
            }

            // Fetch Role & Department
            var role = await _roleManager.FindByNameAsync(model.Role);
            if (role == null)
            {
                TempData["ToastMessage"] = "Selected role is invalid.";
                await LoadDropdownsAsync();
                return View(model);
            }

            var departmentInfo = await _context.Department
                .FirstOrDefaultAsync(d => d.DepartmentId == model.DepartmentId);

            // Prepare Employee
            var employee = new Employee
            {
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Role = model.Role,
                RoleID = role.Id,
                IsActive = model.IsActive,
                DepartmentId = model.DepartmentId,
                ManagerId = departmentInfo?.ManagerId,
                LeaveBalance = model.LeaveBalance
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            // Update Department if Role is Manager
            if (model.Role == "Manager" && departmentInfo != null)
            {
                departmentInfo.ManagerId = employee.EmployeeId;
                departmentInfo.ManagerName = employee.FullName;
                await _context.SaveChangesAsync();
            }

            // Create Employee Log
            var employeeLog = new EmployeeLog
            {
                EmployeeId = employee.EmployeeId,
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Role = model.Role,
                RoleID = employee.RoleID,
                IsActive = model.IsActive,
                DepartmentId = model.DepartmentId,
                ManagerId = employee.ManagerId,
                Operation = "Created",
                TimeStamp = DateTime.Now
            };

            _context.EmployeeLog.Add(employeeLog);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Employee created successfully!";
            return RedirectToAction("EmployeeList");
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
        [Authorize(Roles = "Admin")]
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
                    var existingEmployee = await _context.Employees
                        .Include(e => e.Department)
                        .FirstOrDefaultAsync(e => e.EmployeeId == id);

                    if (existingEmployee == null)
                        return NotFound();

                    bool wasManager = existingEmployee.Role == "Manager";

                    var department = await _context.Department
                        .FirstOrDefaultAsync(e => e.DepartmentId == employee.DepartmentId);

                    employee.ManagerId = department?.ManagerId;

                    // Update role ID
                    var roleId = (await _roleManager.FindByNameAsync(employee.Role))?.Id;
                    employee.RoleID = roleId;

                    // Update fields
                    existingEmployee.FullName = employee.FullName;
                    existingEmployee.Email = employee.Email;
                    existingEmployee.PhoneNumber = employee.PhoneNumber;
                    existingEmployee.Role = employee.Role;
                    existingEmployee.RoleID = roleId;
                    existingEmployee.IsActive = employee.IsActive;
                    existingEmployee.DepartmentId = employee.DepartmentId;
                    existingEmployee.ManagerId = employee.ManagerId;

                    if (employee.Role == "Manager")
                    {
                        // Assign this employee as the manager in the department
                        if (department != null)
                        {
                            department.ManagerId = existingEmployee.EmployeeId;
                            department.ManagerName = existingEmployee.FullName;

                            //Update all employees in the department to reflect this manager
                            var employeesInDept = await _context.Employees
                                .Where(e => e.DepartmentId == employee.DepartmentId && e.EmployeeId != employee.EmployeeId)
                                .ToListAsync();

                            foreach (var emp in employeesInDept)
                            {
                                emp.ManagerId = employee.EmployeeId;
                            }
                        }
                    }
                    else if (wasManager && employee.Role != "Manager")
                    {
                        // If demoted, remove as manager from all departments
                        var deptsManaged = await _context.Department
                            .Where(d => d.ManagerId == employee.EmployeeId)
                            .ToListAsync();

                        foreach (var dept in deptsManaged)
                        {
                            dept.ManagerId = null;
                            dept.ManagerName = null;
                        }

                        // Also clear their subordinates
                        var employeesManaged = await _context.Employees
                            .Where(e => e.ManagerId == employee.EmployeeId)
                            .ToListAsync();

                        foreach (var emp in employeesManaged)
                        {
                            emp.ManagerId = null;
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

        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var employee = await _context.Employees.FindAsync(id);

            if (employee != null)
            {
                // Lock the Identity user
                var user = await _userManager.FindByEmailAsync(employee.Email);
                if (user != null)
                {
                    user.LockoutEnabled = true;
                    user.LockoutEnd = DateTimeOffset.MaxValue;
                    await _userManager.UpdateAsync(user);
                }

                // Unassign this employee as Manager from all subordinates
                var subordinates = await _context.Employees
                    .Where(e => e.ManagerId == employee.EmployeeId)
                    .ToListAsync();

                foreach (var subordinate in subordinates)
                {
                    subordinate.ManagerId = null;
                }

                // Unassign the employee as Manager in departments
                var departments = await _context.Department
                    .Where(d => d.ManagerId == employee.EmployeeId)
                    .ToListAsync();

                foreach (var dept in departments)
                {
                    dept.ManagerId = null;
                    dept.ManagerName = null;

                    // Log the change
                    _context.departmentLogs.Add(new DepartmentLogs
                    {
                        DepartmentId = dept.DepartmentId,
                        DepartmentName = dept.DepartmentName,
                        ManagerId = null,
                        Operation = "Unassigned Manager (Deleted)",
                        TimeStamp = DateTime.Now
                    });
                }

                // Log the deletion
                _context.EmployeeLog.Add(new EmployeeLog
                {
                    EmployeeId = employee.EmployeeId,
                    FullName = employee.FullName,
                    Email = employee.Email,
                    PhoneNumber = employee.PhoneNumber,
                    Role = employee.Role,
                    RoleID = employee.RoleID,
                    IsActive = employee.IsActive,
                    DepartmentId = employee.DepartmentId,
                    ManagerId = employee.ManagerId,
                    Operation = "Deleted",
                    TimeStamp = DateTime.Now
                });

                // Remove employee
                _context.Employees.Remove(employee);

                await _context.SaveChangesAsync();
                TempData["ToastSuccess"] = "Employee deleted and changes updated successfully.";
            }

            return RedirectToAction(nameof(EmployeeList));
        }

        // GET: Employee/Filter
        [Authorize(Roles = "Admin, Manager")]
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

        [Authorize(Roles = "Admin")]
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
