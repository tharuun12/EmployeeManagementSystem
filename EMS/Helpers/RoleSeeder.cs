using Microsoft.AspNetCore.Identity;
using EMS.ViewModels;
using EMS.Models;
using EMS.Web.Models;
namespace EMS.Helpers
{
    public static class RoleSeeder
    {
        public static async Task SeedRolesAndAdmin(IServiceProvider serviceProvider)
        {
            var roleManager = serviceProvider.GetRequiredService<RoleManager<IdentityRole>>();
            var userManager = serviceProvider.GetRequiredService<UserManager<Users>>();


            string[] roles = { "Admin", "Manager", "Employee" };

            foreach (var role in roles)
            {
                if (!await roleManager.RoleExistsAsync(role))
                    await roleManager.CreateAsync(new IdentityRole(role));
            }

            //// Seed Admin User
            //var adminEmail = "admin@ems.com";
            //var adminUser = await userManager.FindByEmailAsync(adminEmail);
            //if (adminUser == null)
            //{
            //    adminUser = new IdentityUser { UserName = adminEmail, Email = adminEmail, EmailConfirmed = true };
            //    await userManager.CreateAsync(adminUser, "Admin@123");
            //    await userManager.AddToRoleAsync(adminUser, "Admin");
            //}

            //// Seed Manager User
            //var managerEmail = "manager@ems.com";
            //var managerUser = await userManager.FindByEmailAsync(managerEmail);
            //if (managerUser == null)
            //{
            //    managerUser = new IdentityUser { UserName = managerEmail, Email = managerEmail, EmailConfirmed = true };
            //    await userManager.CreateAsync(managerUser, "Manager@123");
            //    await userManager.AddToRoleAsync(managerUser, "Manager");
            //}

            //// Seed Employee User
            //var empEmail = "employee@ems.com";
            //var empUser = await userManager.FindByEmailAsync(empEmail);
            //if (empUser == null)
            //{
            //    empUser = new IdentityUser { UserName = empEmail, Email = empEmail, EmailConfirmed = true };
            //    await userManager.CreateAsync(empUser, "Employee@123");
            //    await userManager.AddToRoleAsync(empUser, "Employee");
            //}
        }
    }
}
