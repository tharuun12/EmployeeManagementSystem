using EMS.Models;
using EMS.Web.Data;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMS.Web.Controllers
{
    public class DepartmentController : Controller
    {
        private readonly AppDbContext _context;

        public DepartmentController(AppDbContext context)
        {
            _context = context;
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
            var selectedEmployee = await _context.Employees
                .FirstOrDefaultAsync(e => e.EmployeeId == department.ManagerId);
            department.ManagerName = selectedEmployee?.FullName;
            if (ModelState.IsValid)
            {
                _context.Department.Add(department);
                await _context.SaveChangesAsync();

                // Getting DepartmentId in async way 
                var departmentIdValue = await _context.Department.FirstOrDefaultAsync(e => e.DepartmentName == department.DepartmentName);

                var departmentLogValue = new DepartmentLogs
                {
                    DepartmentId = departmentIdValue.DepartmentId,
                    DepartmentName = department.DepartmentName,
                    ManagerId = department?.ManagerId,
                    Operation = "Created",
                    TimeStamp = DateTime.Now
                };
                _context.departmentLogs.Add(departmentLogValue);
                await _context.SaveChangesAsync();
                return RedirectToAction(nameof(Index));
            }
            ViewData["Managers"] = _context.Employees.ToList();
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
                //var departmentLog = await _context.departmentLogs.FirstOrDefaultAsync(e => e.DepartmentId == id);

                try
                {
                    var departmentLogValue = new DepartmentLogs
                    {
                        DepartmentId = department.DepartmentId,
                        DepartmentName = department.DepartmentName,
                        ManagerId = department.ManagerId,
                        Operation = "Update",
                        TimeStamp = DateTime.Now
                    };
                    _context.departmentLogs.Add(departmentLogValue);
                    _context.Update(department);
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

            ViewData["Managers"] = _context.Employees.ToList();
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

            if (department != null)
            {
                bool hasEmployees = await _context.Employees.AnyAsync(e => e.DepartmentId == id);

                if (hasEmployees)
                {
                    TempData["ToastMessage"] = "Cannot delete this department because employees are still assigned.";
                    return RedirectToAction(nameof(Index));
                }

                var departmentLogValue = new DepartmentLogs
                {
                    DepartmentId = department.DepartmentId,
                    DepartmentName = department.DepartmentName,
                    ManagerId = department.ManagerId,
                    Operation = "Delete",
                    TimeStamp = DateTime.Now
                };

                _context.departmentLogs.Add(departmentLogValue);
                _context.Department.Remove(department);
                await _context.SaveChangesAsync();
            }

            return RedirectToAction(nameof(Index));
        }

    }
}
