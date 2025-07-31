using EMS.Models;
using EMS.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMS.Web.Controllers
{
    public class DepartmentController : Controller
    {
        private readonly AppDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;

        public DepartmentController(AppDbContext context, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _roleManager = roleManager;
        }

        // Department/Index - Get
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Index()
        {
            var departments = await _context.Department
                .Include(d => d.Manager)
                .ToListAsync();
            return View(departments);
        }

        // Departement/Create - GET
        [Authorize(Roles = "Admin")]
        public IActionResult Create()
        {
            ViewData["Managers"] = _context.Employees.ToList();
            return View();
        }
        // Departement/Create - POST
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(Department department)
        {
            if (ModelState.IsValid)
            {
                // Validate manager selection
                if (department.ManagerId.HasValue)
                {
                    var selectedEmployee = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == department.ManagerId);

                    if (selectedEmployee == null)
                    {
                        TempData["ToastError"] = "Selected manager does not exist.";
                        ViewData["Managers"] = await _context.Employees.ToListAsync();
                        return View(department);
                    }

                    // Check if employee is already managing another department
                    var existingManagerDepartment = await _context.Department
                        .FirstOrDefaultAsync(d => d.ManagerId == department.ManagerId);

                    if (existingManagerDepartment != null)
                    {
                        TempData["ToastError"] = $"This employee is already managing {existingManagerDepartment.DepartmentName} department.";
                        ViewData["Managers"] = await _context.Employees.ToListAsync();
                        return View(department);
                    }

                    // Set manager name
                    department.ManagerName = selectedEmployee.FullName;

                    // Update employee role to Manager
                    if (selectedEmployee.Role != "Manager")
                    {
                        selectedEmployee.Role = "Manager";
                        selectedEmployee.RoleID = (await _roleManager.FindByNameAsync("Manager"))?.Id;
                    }

                    // Managers don't have managers
                    selectedEmployee.ManagerId = null;
                }

                // Save the new department
                _context.Department.Add(department);
                await _context.SaveChangesAsync();

                // If there's a manager, update their department assignment
                if (department.ManagerId.HasValue)
                {
                    var manager = await _context.Employees.FindAsync(department.ManagerId);
                    if (manager != null)
                    {
                        manager.DepartmentId = department.DepartmentId;
                        await _context.SaveChangesAsync();
                    }
                }

                // Create a department log
                var departmentLog = new DepartmentLogs
                {
                    DepartmentId = department.DepartmentId,
                    DepartmentName = department.DepartmentName,
                    ManagerId = department.ManagerId,
                    Operation = "Created",
                    TimeStamp = DateTime.Now
                };
                _context.departmentLogs.Add(departmentLog);
                await _context.SaveChangesAsync();

                TempData["ToastSuccess"] = "Department created successfully.";
                return RedirectToAction(nameof(Index));
            }

            ViewData["Managers"] = await _context.Employees.ToListAsync();
            return View(department);
        }

        // Departement/Edit/DepartmentId - GET
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Edit(int id)
        {
            var department = await _context.Department.FindAsync(id);
            if (department == null)
                return NotFound();

            ViewData["Managers"] = _context.Employees.ToList();
            return View(department);
        }

        // Departement/Edit/DepartmentId - POST
        [Authorize(Roles = "Admin")]
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, Department department)
        {
            if (id != department.DepartmentId)
                return NotFound();

            if (ModelState.IsValid)
            {
                try
                {
                    var existingDepartment = await _context.Department.FindAsync(id);
                    if (existingDepartment == null)
                        return NotFound();

                    // Store old manager info for cleanup
                    int? oldManagerId = existingDepartment.ManagerId;
                    int? newManagerId = department.ManagerId;

                    // If changing manager
                    if (oldManagerId != newManagerId)
                    {
                        // If assigning a new manager
                        if (newManagerId.HasValue)
                        {
                            var newManager = await _context.Employees
                                .FirstOrDefaultAsync(e => e.EmployeeId == newManagerId);

                            if (newManager == null)
                            {
                                TempData["ToastError"] = "Selected manager does not exist.";
                                ViewData["Managers"] = await _context.Employees.ToListAsync();
                                return View(department);
                            }

                            var existingManagerDepartment = await _context.Department
                                .FirstOrDefaultAsync(d => d.ManagerId == newManagerId && d.DepartmentId != id);

                            if (existingManagerDepartment != null)
                            {
                                TempData["ToastError"] = $"This employee is already a manager of {existingManagerDepartment.DepartmentName} department. Please change their role first.";
                                ViewData["Managers"] = await _context.Employees.ToListAsync();
                                return View(department);
                            }

                            // Handle old manager cleanup
                            if (oldManagerId.HasValue)
                            {
                                await HandleManagerRemoval(oldManagerId.Value, id);
                            }

                            // Handle new manager assignment
                            await HandleManagerAssignment(newManager, id);

                            // Update department
                            existingDepartment.ManagerId = newManagerId;
                            existingDepartment.ManagerName = newManager.FullName;
                        }
                        else
                        {
                            // Removing manager (setting to null)
                            if (oldManagerId.HasValue)
                            {
                                await HandleManagerRemoval(oldManagerId.Value, id);
                            }

                            existingDepartment.ManagerId = null;
                            existingDepartment.ManagerName = null;
                        }
                    }

                    // Update other department fields
                    existingDepartment.DepartmentName = department.DepartmentName;

                    _context.Update(existingDepartment);

                    // Create log entry
                    var log = new DepartmentLogs
                    {
                        DepartmentId = department.DepartmentId,
                        DepartmentName = department.DepartmentName,
                        ManagerId = department.ManagerId,
                        Operation = "Updated",
                        TimeStamp = DateTime.Now
                    };

                    _context.departmentLogs.Add(log);
                    await _context.SaveChangesAsync();

                    TempData["ToastSuccess"] = "Department updated successfully!";
                    return RedirectToAction(nameof(Index));
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Department.Any(e => e.DepartmentId == id))
                        return NotFound();
                    else
                        throw;
                }
            }

            ViewData["Managers"] = await _context.Employees.ToListAsync();
            return View(department);
        }

        // Helper method to handle manager removal
        private async Task HandleManagerRemoval(int managerId, int departmentId)
        {
            var oldManager = await _context.Employees.FindAsync(managerId);
            if (oldManager != null && oldManager.Role == "Manager")
            {
                // Check if this manager manages any other departments
                var otherDepartments = await _context.Department
                    .Where(d => d.ManagerId == managerId && d.DepartmentId != departmentId)
                    .ToListAsync();

                // If not managing other departments, demote to Employee
                if (!otherDepartments.Any())
                {
                    oldManager.Role = "Employee";
                    oldManager.RoleID = (await _roleManager.FindByNameAsync("Employee"))?.Id;

                    // Find a manager in their department to report to, or set to null
                    var departmentManager = await _context.Department
                        .Where(d => d.DepartmentId == oldManager.DepartmentId && d.ManagerId != managerId)
                        .Select(d => d.ManagerId)
                        .FirstOrDefaultAsync();

                    oldManager.ManagerId = departmentManager;
                }
            }

            // Clear manager reference from all employees in this department
            var employeesInDepartment = await _context.Employees
                .Where(e => e.DepartmentId == departmentId && e.ManagerId == managerId)
                .ToListAsync();

            foreach (var employee in employeesInDepartment)
            {
                employee.ManagerId = null; // Will be reassigned if new manager is set
            }
        }

        // Helper method to handle manager assignment
        private async Task HandleManagerAssignment(Employee newManager, int departmentId)
        {
            // Update the employee's role to Manager if not already
            if (newManager.Role != "Manager")
            {
                newManager.Role = "Manager";
                newManager.RoleID = (await _roleManager.FindByNameAsync("Manager"))?.Id;
            }

            // Move manager to this department if needed
            if (newManager.DepartmentId != departmentId)
            {
                newManager.DepartmentId = departmentId;
            }

            // Managers don't have managers
            newManager.ManagerId = null;

            // Update all employees in the department to report to new manager
            var employeesInDepartment = await _context.Employees
                .Where(e => e.DepartmentId == departmentId && e.EmployeeId != newManager.EmployeeId)
                .ToListAsync();

            foreach (var employee in employeesInDepartment)
            {
                employee.ManagerId = newManager.EmployeeId;
            }
        }


        // Departement/Delete/DepartmentId - GET
        [Authorize(Roles = "Admin")]
        public async Task<IActionResult> Delete(int id)
        {
            var department = await _context.Department
                .Include(d => d.Manager)
                .FirstOrDefaultAsync(m => m.DepartmentId == id);

            if (department == null)
                return NotFound();

            return View(department);
        }

        // Departement/Delete/DepartmentId - POST
        [Authorize(Roles = "Admin")]
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var department = await _context.Department.FindAsync(id);

            if (department == null)
            {
                TempData["ToastError"] = "Department not found.";
                return RedirectToAction(nameof(Index));
            }

            // Check if employees are still assigned
            bool hasEmployees = await _context.Employees.AnyAsync(e => e.DepartmentId == id);
            if (hasEmployees)
            {
                TempData["ToastError"] = "Cannot delete department while employees are still assigned.";
                return RedirectToAction(nameof(Index));
            }

            // Log the deletion
            var departmentLog = new DepartmentLogs
            {
                DepartmentId = department.DepartmentId,
                DepartmentName = department.DepartmentName,
                ManagerId = department.ManagerId,
                Operation = "Deleted",
                TimeStamp = DateTime.Now
            };
            _context.departmentLogs.Add(departmentLog);

            //Delete the department
            _context.Department.Remove(department);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Department deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
