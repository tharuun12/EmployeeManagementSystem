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

            // Check if email already exists
            var existingEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.Email.ToLower().Trim() == normalizedEmail);

            if (existingEmployee != null)
            {
                TempData["ToastError"] = "A user with this Email already exists.";
                await LoadDropdownsAsync();
                return View(model);
            }

            // Get department information
            var department = await _context.Department.FindAsync(model.DepartmentId);
            if (department == null)
            {
                TempData["ToastError"] = "Selected department does not exist.";
                await LoadDropdownsAsync();
                return View(model);
            }

            // Role-specific validation and logic
            if (model.Role == "Manager")
            {
                // Check if department already has a manager
                if (department.ManagerId != null)
                {
                    TempData["ToastError"] = "This department already has a manager assigned. Please change the current manager to employee role first.";
                    await LoadDropdownsAsync();
                    return View(model);
                }

                // Ensure admin exists before creating manager
                var adminExists = await _context.Employees.AnyAsync(e => e.Role == "Admin");
                if (!adminExists)
                {
                    TempData["ToastError"] = "Please create an Admin before adding a Manager.";
                    await LoadDropdownsAsync();
                    return View(model);
                }
            }
            else if (model.Role == "Employee")
            {
                // Employees must have a manager in their department
                if (department.ManagerId == null)
                {
                    TempData["ToastError"] = "Please assign a Manager to the Department first.";
                    await LoadDropdownsAsync();
                    return View(model);
                }
            }

            // Get role information
            var role = await _roleManager.FindByNameAsync(model.Role);
            if (role == null)
            {
                TempData["ToastError"] = "Selected role is invalid.";
                await LoadDropdownsAsync();
                return View(model);
            }

            // Create new employee
            var employee = new Employee
            {
                FullName = model.FullName,
                Email = model.Email,
                PhoneNumber = model.PhoneNumber,
                Role = model.Role,
                RoleID = role.Id,
                IsActive = model.IsActive,
                DepartmentId = model.DepartmentId,
                ManagerId = model.Role == "Manager" ? null : department.ManagerId,
                LeaveBalance = model.LeaveBalance
            };

            _context.Employees.Add(employee);
            await _context.SaveChangesAsync();

            if (model.Role == "Manager")
            {
                department.ManagerId = employee.EmployeeId;
                department.ManagerName = employee.FullName;

                var employeesInDepartment = await _context.Employees
                    .Where(e => e.DepartmentId == model.DepartmentId && e.EmployeeId != employee.EmployeeId)
                    .ToListAsync();

                foreach (var emp in employeesInDepartment)
                {
                    emp.ManagerId = employee.EmployeeId;
                }

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

            var EmployeeLeave = new LeaveBalance
            {
                EmployeeId = employee.EmployeeId,
                TotalLeaves = model.LeaveBalance
            };

            _context.LeaveBalances.Add(EmployeeLeave);
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

                    // Store original state
                    bool wasManager = existingEmployee.Role == "Manager";
                    bool willBeManager = employee.Role == "Manager";
                    int oldDepartmentId = existingEmployee.DepartmentId;
                    int newDepartmentId = employee.DepartmentId;

                    // Get the new department
                    var newDepartment = await _context.Department
                        .FirstOrDefaultAsync(d => d.DepartmentId == newDepartmentId);

                    if (newDepartment == null)
                    {
                        TempData["ToastError"] = "Selected department does not exist.";
                        ViewBag.Departments = await _context.Department.ToListAsync();
                        ViewBag.Roles = await _roleManager.Roles.ToListAsync();
                        return View(employee);
                    }

                    // Validation for role changes
                    if (willBeManager && !wasManager)
                    {
                        if (newDepartment.ManagerId != null && newDepartment.ManagerId != id)
                        {
                            TempData["ToastError"] = "This department already has a manager assigned. Please change the current manager to employee role first.";
                            ViewBag.Departments = await _context.Department.ToListAsync();
                            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
                            return View(employee);
                        }
                    }
                    else if (willBeManager && wasManager && newDepartmentId != oldDepartmentId)
                    {
                        if (newDepartment.ManagerId != null && newDepartment.ManagerId != id)
                        {
                            TempData["ToastError"] = "The target department already has a manager assigned.";
                            ViewBag.Departments = await _context.Department.ToListAsync();
                            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
                            return View(employee);
                        }
                    }
                    else if (!willBeManager && employee.Role == "Employee")
                    {
                        if (newDepartment.ManagerId == null)
                        {
                            TempData["ToastError"] = "Cannot assign employee to a department without a manager. Please assign a manager to the department first.";
                            ViewBag.Departments = await _context.Department.ToListAsync();
                            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
                            return View(employee);
                        }
                    }

                    // Update role ID
                    var roleId = (await _roleManager.FindByNameAsync(employee.Role))?.Id;
                    if (roleId == null)
                    {
                        TempData["ToastError"] = "Selected role is invalid.";
                        ViewBag.Departments = await _context.Department.ToListAsync();
                        ViewBag.Roles = await _roleManager.Roles.ToListAsync();
                        return View(employee);
                    }
                    // Update AspNetUserRoles mapping for this user
                    if (!string.IsNullOrEmpty(existingEmployee.UserId))
                    {
                        var user = await _userManager.FindByIdAsync(existingEmployee.UserId);
                        if (user != null)
                        {
                            // Remove all current roles
                            var currentRoles = await _userManager.GetRolesAsync(user);
                            foreach (var r in currentRoles)
                                await _userManager.RemoveFromRoleAsync(user, r);

                            // Add the new role
                            await _userManager.AddToRoleAsync(user, employee.Role);
                        }
                    }


                    if (employee.LeaveBalance != existingEmployee.LeaveBalance)
                    {
                        var updateTotalLeave = await _context.LeaveBalances.FirstOrDefaultAsync(e => e.EmployeeId == employee.EmployeeId);
                        if (updateTotalLeave != null)
                        {
                            updateTotalLeave.TotalLeaves = employee.LeaveBalance + updateTotalLeave.TotalLeaves;
                            _context.LeaveBalances.Update(updateTotalLeave);
                            await _context.SaveChangesAsync();
                        }
                    }

                    // Update basic employee fields
                    existingEmployee.FullName = employee.FullName;
                    existingEmployee.Email = employee.Email;
                    existingEmployee.PhoneNumber = employee.PhoneNumber;
                    existingEmployee.Role = employee.Role;
                    existingEmployee.RoleID = roleId;
                    existingEmployee.IsActive = employee.IsActive;
                    existingEmployee.DepartmentId = newDepartmentId;
                    existingEmployee.LeaveBalance = employee.LeaveBalance;

                    // Handle Manager ID assignment
                    if (willBeManager)
                    {
                        existingEmployee.ManagerId = null; 
                    }
                    else
                    {
                        existingEmployee.ManagerId = newDepartment.ManagerId; 
                    }

                    // Handle role transition logic
                    if (wasManager && !willBeManager)
                    {
                        await HandleManagerDemotion(id);
                    }
                    else if (!wasManager && willBeManager)
                    {
                        await HandleManagerPromotion(id, existingEmployee.FullName, newDepartmentId);
                    }
                    else if (wasManager && willBeManager && oldDepartmentId != newDepartmentId)
                    {
                        await HandleManagerDepartmentChange(id, existingEmployee.FullName, oldDepartmentId, newDepartmentId);
                    }

                    // Create Employee Log
                    var employeeLog = new EmployeeLog
                    {
                        EmployeeId = existingEmployee.EmployeeId,
                        FullName = existingEmployee.FullName,
                        Email = existingEmployee.Email,
                        PhoneNumber = existingEmployee.PhoneNumber,
                        Role = existingEmployee.Role,
                        RoleID = existingEmployee.RoleID,
                        IsActive = existingEmployee.IsActive,
                        DepartmentId = existingEmployee.DepartmentId,
                        ManagerId = existingEmployee.ManagerId,
                        Operation = "Updated",
                        TimeStamp = DateTime.Now
                    };

                    _context.EmployeeLog.Add(employeeLog);
                    await _context.SaveChangesAsync();

                    TempData["ToastSuccess"] = "Employee updated successfully!";
                    return RedirectToAction(nameof(EmployeeList));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Employees.Any(e => e.EmployeeId == id))
                        return NotFound();
                    else
                        throw;
                }
            }

            ViewBag.Departments = await _context.Department.ToListAsync();
            ViewBag.Roles = await _roleManager.Roles.ToListAsync();
            return View(employee);
        }

        // Helper method to handle manager demotion
        private async Task HandleManagerDemotion(int employeeId)
        {
            var departmentsManaged = await _context.Department
                .Where(d => d.ManagerId == employeeId)
                .ToListAsync();

            foreach (var dept in departmentsManaged)
            {
                dept.ManagerId = null;
                dept.ManagerName = null;

                _context.departmentLogs.Add(new DepartmentLogs
                {
                    DepartmentId = dept.DepartmentId,
                    DepartmentName = dept.DepartmentName,
                    ManagerId = null,
                    Operation = "Manager Demoted",
                    TimeStamp = DateTime.Now
                });
            }

            var subordinates = await _context.Employees
                .Where(e => e.ManagerId == employeeId)
                .ToListAsync();

            foreach (var subordinate in subordinates)
            {
                subordinate.ManagerId = null;
            }
        }

        // Helper method to handle manager promotion
        private async Task HandleManagerPromotion(int employeeId, string employeeName, int departmentId)
        {
            var department = await _context.Department.FindAsync(departmentId);
            if (department != null)
            {
                department.ManagerId = employeeId;
                department.ManagerName = employeeName;

                var employeesInDepartment = await _context.Employees
                    .Where(e => e.DepartmentId == departmentId && e.EmployeeId != employeeId)
                    .ToListAsync();

                foreach (var emp in employeesInDepartment)
                {
                    emp.ManagerId = employeeId;
                }

                _context.departmentLogs.Add(new DepartmentLogs
                {
                    DepartmentId = department.DepartmentId,
                    DepartmentName = department.DepartmentName,
                    ManagerId = employeeId,
                    Operation = "Manager Assigned",
                    TimeStamp = DateTime.Now
                });
            }
        }

        // Helper method to handle manager department change
        private async Task HandleManagerDepartmentChange(int employeeId, string employeeName, int oldDepartmentId, int newDepartmentId)
        {
            var oldDepartment = await _context.Department.FindAsync(oldDepartmentId);
            if (oldDepartment != null && oldDepartment.ManagerId == employeeId)
            {
                oldDepartment.ManagerId = null;
                oldDepartment.ManagerName = null;

                var oldDepartmentEmployees = await _context.Employees
                    .Where(e => e.DepartmentId == oldDepartmentId && e.ManagerId == employeeId)
                    .ToListAsync();

                foreach (var emp in oldDepartmentEmployees)
                {
                    emp.ManagerId = null;
                }

                _context.departmentLogs.Add(new DepartmentLogs
                {
                    DepartmentId = oldDepartment.DepartmentId,
                    DepartmentName = oldDepartment.DepartmentName,
                    ManagerId = null,
                    Operation = "Manager Transferred Out",
                    TimeStamp = DateTime.Now
                });
            }

            await HandleManagerPromotion(employeeId, employeeName, newDepartmentId);
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
                // Check if this employee is assigned as a manager in any department
                var isManager = await _context.Department.AnyAsync(d => d.ManagerId == employee.EmployeeId);
                if (isManager)
                {
                    TempData["ToastError"] = "Cannot delete employee: assigned as department manager.";
                    return RedirectToAction(nameof(EmployeeList));
                }
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

            // Get the employee Id from Users table if needed
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
        // GET: Employee/CurrentMonthInfo
        [Authorize]
        public async Task<IActionResult> CurrentMonthInfo()
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

            var currentMonth = DateTime.Now.Month;
            var currentYear = DateTime.Now.Year;

            var monthlyLeaves = await _context.LeaveRequests
                .Where(l => l.EmployeeId == employee.EmployeeId &&
                            l.StartDate.Month == currentMonth &&
                            l.StartDate.Year == currentYear)
                .ToListAsync();

            var daysOnLeave = monthlyLeaves
                .Where(l => l.Status == "Approved")
                .Sum(l => (l.EndDate - l.StartDate).Days + 1);

            // Get manager name if applicable
            string managerName = "N/A";
            if (employee.ManagerId.HasValue)
            {
                var manager = await _context.Employees
                    .FirstOrDefaultAsync(e => e.EmployeeId == employee.ManagerId);
                managerName = manager?.FullName ?? "N/A";
            }

            var viewModel = new CurrentMonthEmployeeViewModel
            {
                Employee = employee,
                ManagerName = managerName,
                CurrentMonth = DateTime.Now.ToString("MMMM yyyy"),
                LeaveRequests = monthlyLeaves,
                DaysOnLeave = daysOnLeave,
                RemainingLeaveBalance = employee.LeaveBalance - daysOnLeave
            };

            return View(viewModel);
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
