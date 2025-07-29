using EMS.Models;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace EMS.Helpers
{
    public static class DbSeeder
    {
        public static async Task SeedDefaultAdminAsync(IServiceProvider services)
        {
            using (var scope = services.CreateScope())
            {
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var userManager = scope.ServiceProvider.GetRequiredService<UserManager<Users>>();
                var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();

                string adminEmail = "defaultadmin@ems.com";
                string adminPassword = "Default@123";
                string adminRole = "Admin";

                // 1. Create Role if not exists
                if (!await roleManager.RoleExistsAsync(adminRole))
                {
                    await roleManager.CreateAsync(new IdentityRole(adminRole));
                }

                // 2. Create Department if it doesn't exist
                var adminDepartment = await dbContext.Department.FirstOrDefaultAsync(d => d.DepartmentId == 1);
                if (adminDepartment == null)
                {
                    adminDepartment = new Department
                    {
                        DepartmentName = "Admin Department"
                        //DepartmentName = "Default Admin Department"
                    };
                    dbContext.Department.Add(adminDepartment);
                    await dbContext.SaveChangesAsync();
                }


                // 2. Create Admin User if not exists
                var adminUser = await userManager.FindByEmailAsync(adminEmail);
                if (adminUser == null)
                {
                    adminUser = new Users
                    {
                        FullName = "Default Admin",
                        UserName = adminEmail,
                        Email = adminEmail,
                        EmailConfirmed = true,
                        PhoneNumber = "9999999999",
                    };

                    var result = await userManager.CreateAsync(adminUser, adminPassword);
                    if (result.Succeeded)
                    {
                        await userManager.AddToRoleAsync(adminUser, adminRole);
                    }
                }

                // 3. Insert Admin into Employee table if not exists
                bool employeeExists = await dbContext.Employees.AnyAsync(e => e.Email == adminEmail);
                if (!employeeExists)
                {
                    var employee = new Employee
                    {
                        FullName = adminUser.FullName,
                        Email = adminUser.Email,
                        PhoneNumber = adminUser.PhoneNumber,
                        Role = "Admin",
                        IsActive = true,
                        DepartmentId = 1, // Assume default DepartmentId 1
                        ManagerId = null,
                        UserId = adminUser.Id,
                        LeaveBalance = 30 // Default leave balance
                    };

                    dbContext.Employees.Add(employee);
                    await dbContext.SaveChangesAsync();
                }
            }
        }

    }
}
