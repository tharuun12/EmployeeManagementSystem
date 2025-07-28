using EMS.ViewModels;
using EMS.Web.Data;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace EMS.Controllers
{
    public class DashboardController : Controller
    {
        private readonly AppDbContext _context;

        public DashboardController(AppDbContext context)
        {
            _context = context;
        }

        // Dashboard/Index - Get ( Get all the analytics of the Employee and Departement )
        public async Task<IActionResult> Index()
        {
            var totalEmployees = await _context.Employees.CountAsync();
            var activeEmployees = await _context.Employees.CountAsync(e => e.IsActive == true);
            var totalDepartments = await _context.Department.CountAsync();

            var recentEmployees = await _context.Employees
                //.OrderByDescending(e => e.CreatedDate)
                .Take(5)
                .ToListAsync();

            //var departmentStatss = await _context.Department
            //    .Select(d => new
            //    {
            //        Name = d.DepartmentName,
            //        EmployeeCount = _context.Employees.Count(e => e.DepartmentId == d.DepartmentId)
            //    }).ToListAsync();

            var departmentStats = await _context.Department
                .Include(d => d.Employees)
                .Select(d => new DepartmentStatsViewModel
                {
                    Name = d.DepartmentName,
                    EmployeeCount = d.Employees!.Count()
                }).ToListAsync();
            Console.WriteLine(departmentStats.Count);
            Console.WriteLine(departmentStats);

            ViewBag.TotalEmployees = totalEmployees;
            ViewBag.ActiveEmployees = activeEmployees;
            ViewBag.TotalDepartments = totalDepartments;

            ViewBag.RecentEmployees = recentEmployees;
            ViewBag.DepartmentStats = departmentStats;

            return View(departmentStats);
        }
    }
}
