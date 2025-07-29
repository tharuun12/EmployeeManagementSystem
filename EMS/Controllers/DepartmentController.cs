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
                // Step 1: Set Manager Name from selected employee
                var selectedEmployee = await _context.Employees
                    .FirstOrDefaultAsync(e => e.EmployeeId == department.ManagerId);
                department.ManagerName = selectedEmployee?.FullName;

                // Step 2: Save the new department
                _context.Department.Add(department);
                await _context.SaveChangesAsync();

                // Step 3: Create a department log
                var departmentLog = new DepartmentLogs
                {
                    DepartmentId = department.DepartmentId, // use directly after SaveChanges
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

            // Repopulate manager dropdown if model is invalid
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
                    var manager = await _context.Employees
                        .FirstOrDefaultAsync(e => e.EmployeeId == department.ManagerId);

                    if (manager != null)
                    {
                        department.ManagerName = manager.FullName;

                        if (manager.Role != "Manager")
                        {
                            manager.Role = "Manager";
                            manager.RoleID = (await _roleManager.FindByNameAsync("Manager"))?.Id;
                        }

                        if (manager.DepartmentId != department.DepartmentId)
                        {
                            manager.DepartmentId = department.DepartmentId;
                        }

                        // ✅ Assign new manager to all employees in that department
                        var employeesInDept = await _context.Employees
                            .Where(e => e.DepartmentId == department.DepartmentId && e.EmployeeId != manager.EmployeeId)
                            .ToListAsync();

                        foreach (var emp in employeesInDept)
                        {
                            emp.ManagerId = manager.EmployeeId;
                        }
                    }
                    else
                    {
                        department.ManagerName = null;
                        department.ManagerId = null;
                    }

                    _context.Update(department);

                    var log = new DepartmentLogs
                    {
                        DepartmentId = department.DepartmentId,
                        DepartmentName = department.DepartmentName,
                        ManagerId = department.ManagerId,
                        Operation = "Update",
                        TimeStamp = DateTime.Now
                    };

                    _context.departmentLogs.Add(log);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!_context.Department.Any(e => e.DepartmentId == id))
                        return NotFound();
                    else
                        throw;
                }

                return RedirectToAction(nameof(Index));
            }

            ViewData["Managers"] = await _context.Employees.ToListAsync();
            return View(department);
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

            // Step 1: Check if employees are still assigned
            bool hasEmployees = await _context.Employees.AnyAsync(e => e.DepartmentId == id);
            if (hasEmployees)
            {
                TempData["ToastError"] = "Cannot delete department while employees are still assigned.";
                return RedirectToAction(nameof(Index));
            }

            // Step 2: Log the deletion
            var departmentLog = new DepartmentLogs
            {
                DepartmentId = department.DepartmentId,
                DepartmentName = department.DepartmentName,
                ManagerId = department.ManagerId,
                Operation = "Deleted",
                TimeStamp = DateTime.Now
            };
            _context.departmentLogs.Add(departmentLog);

            // Step 3: Delete the department
            _context.Department.Remove(department);
            await _context.SaveChangesAsync();

            TempData["ToastSuccess"] = "Department deleted successfully.";
            return RedirectToAction(nameof(Index));
        }
    }
}
