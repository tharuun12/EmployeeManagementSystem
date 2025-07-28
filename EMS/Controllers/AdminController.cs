using EMS.Data;
using EMS.Models;
using EMS.ViewModels;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMS.Controllers
{
    public class AdminController : Controller
    {
        private readonly AppDbContext _context;
        private readonly RoleManager<IdentityRole> _roleManager;

        public AdminController(AppDbContext context, RoleManager<IdentityRole> roleManager)
        {
            _context = context;
            _roleManager = roleManager;
        }

        // READ
        public IActionResult Index()
        {
            var employees = _context.Employees.ToList();
            return View(employees);
        }

        // UPDATE - GET
        public IActionResult Edit(int id)
        {
            var employee = _context.Employees.Find(id);
            if (employee == null)
                return NotFound();

            var model = new RegisterViewModel
            {
                EmployeeId = employee.EmployeeId,
                Email = employee.Email,
                PhoneNumber = employee.PhoneNumber,
                FullName = employee.FullName,
                //Role = employee.Role,
                DepartmentId = employee.DepartmentId,
                ManagerId = employee.ManagerId,
                LeaveBalance = employee.LeaveBalance
            };

            ViewBag.Departments = _context.Department.ToList();
            ViewBag.Managers = _context.Employees.ToList();
            return View(model);
        }


        // UPDATE - POST
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult Edit(int id, RegisterViewModel model)
        {
            if (ModelState.IsValid)
            {
                var employee = _context.Employees.Find(id);
                if (employee == null)
                    return NotFound();

                employee.Email = model.Email;
                employee.PhoneNumber = model.PhoneNumber;
                employee.FullName = model.FullName;
                //employee.Role = model.Role;
                employee.DepartmentId = model.DepartmentId;
                employee.ManagerId = model.ManagerId;
                employee.LeaveBalance = model.LeaveBalance;

                _context.SaveChanges();
                return RedirectToAction(nameof(Index));
            }

            ViewBag.Departments = _context.Department.ToList();
            ViewBag.Managers = _context.Employees.ToList();
            return View(model);
        }


        // DELETE - GET
        public IActionResult Delete(int id)
        {
            var employee = _context.Employees.Find(id);
            if (employee == null)
                return NotFound();
            return View(employee);
        }

        // DELETE - POST
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public IActionResult DeleteConfirmed(int id)
        {
            var employee = _context.Employees.Find(id);
            if (employee == null)
                return NotFound();

            _context.Employees.Remove(employee);
            _context.SaveChanges();
            return RedirectToAction(nameof(Index));
        }
    }
}
