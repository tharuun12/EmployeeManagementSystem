using EMS.Controllers;
using EMS.Models;
using EMS.ViewModels;
using EMS.Web.Controllers;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Moq;
using EMS.Helpers;



namespace EMS.Tests
{
    public class EmployeeControllerTests
    {
        private AppDbContext GetDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(dbName)
                .Options;
            return new AppDbContext(options);
        }

        private EmployeeController GetController(AppDbContext context)
        {
            var userStore = new Mock<IUserStore<Users>>();
            var userManager = new Mock<UserManager<Users>>(
                userStore.Object, null, null, null, null, null, null, null, null
            );

            var roles = new List<IdentityRole>
                {
                    new IdentityRole("Admin"),
                    new IdentityRole("Employee")
                };

            var asyncRoles = new TestAsyncEnumerable<IdentityRole>(roles);

            var roleStore = new Mock<IRoleStore<IdentityRole>>();
            var roleManager = new Mock<RoleManager<IdentityRole>>(
                roleStore.Object, null, null, null, null
            );

            // Return async-capable IQueryable
            roleManager.Setup(r => r.Roles).Returns(asyncRoles);

            var tempData = new Mock<ITempDataDictionary>();

            return new EmployeeController(context, roleManager.Object, userManager.Object)
            {
                TempData = tempData.Object
            };
        }


        [Fact]
        public async Task Create_GET_ReturnsViewWithDropdowns()
        {
            var context = GetDbContext("Create_GET_Test");
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT" });
            context.SaveChanges();

            var controller = GetController(context);
            var result = await controller.Create();

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.NotNull(controller.ViewBag.Departments);
        }

        [Fact]
        public async Task Create_POST_DuplicateEmail_ShowsToast()
        {
            var context = GetDbContext("Duplicate_Email_Test");

            context.Employees.Add(new Employee
            {
                FullName = "John Doe",
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var controller = GetController(context);

            var model = new EmployeeViewModel
            {
                FullName = "New John",
                Email = "john@example.com", // Duplicate
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };

            var result = await controller.Create(model);

            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.True(controller.TempData.ContainsKey("ToastMessage"));
        }



        [Fact]
        public async Task Edit_UpdatesEmployeeCorrectly()
        {
            var context = GetDbContext("Edit_Test");
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Original",
                Email = "edit@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var controller = GetController(context);
            var employee = context.Employees.First();
            employee.FullName = "Updated Name";

            var result = await controller.Edit(1, employee);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("EmployeeList", redirect.ActionName);

            var updated = await context.Employees.FindAsync(1);
            Assert.Equal("Updated Name", updated.FullName);
        }

        [Fact]
        public async Task DeleteConfirmed_RemovesEmployee()
        {
            var context = GetDbContext("DeleteConfirmed_Test");
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "To Delete",
                Email = "delete@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var controller = GetController(context);
            var result = await controller.DeleteConfirmed(1);

            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("EmployeeList", redirect.ActionName);

            var employee = await context.Employees.FindAsync(1);
            Assert.Null(employee);
        }
    }
}