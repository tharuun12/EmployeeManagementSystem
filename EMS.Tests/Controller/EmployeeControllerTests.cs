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
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

namespace EMS.Tests.Controller
{
    public class EmployeeControllerTests
    {
        private AppDbContext GetDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new AppDbContext(options);
        }

        private EmployeeController GetController(
            AppDbContext context,
            Mock<RoleManager<IdentityRole>> roleManagerMock,
            Mock<UserManager<Users>> userManagerMock,
            ClaimsPrincipal user = null)
        {
            var controller = new EmployeeController(context, roleManagerMock.Object, userManagerMock.Object);
            var httpContext = new DefaultHttpContext();
            if (user != null)
                httpContext.User = user;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            controller.TempData = new TempDataDictionary(httpContext, Mock.Of<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>());
            return controller;
        }

        private Mock<RoleManager<IdentityRole>> MockRoleManager()
        {
            var store = new Mock<IRoleStore<IdentityRole>>();
            return new Mock<RoleManager<IdentityRole>>(store.Object, null, null, null, null);
        }

        private Mock<UserManager<Users>> MockUserManager()
        {
            var store = new Mock<IUserStore<Users>>();
            return new Mock<UserManager<Users>>(store.Object, null, null, null, null, null, null, null, null);
        }

        private ClaimsPrincipal GetUserWithRole(string userId, string role)
        {
            var claims = new List<Claim>
            {
                new Claim(ClaimTypes.NameIdentifier, userId),
                new Claim(ClaimTypes.Role, role)
            };
            var identity = new ClaimsIdentity(claims, "mock");
            return new ClaimsPrincipal(identity);
        }

        // 1. Normal Functional Cases

        [Fact]
        public async Task Create_Get_ReturnsViewWithDepartmentsAndRoles()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT" });
            context.SaveChanges();

            var roles = new List<IdentityRole>
                {
                    new IdentityRole("Admin"),
                    new IdentityRole("Manager"),
                    new IdentityRole("Employee")
                };
            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.Roles).Returns(roles.AsQueryable());
            // Add this line to mock ToListAsync for Roles:
            roleManagerMock.Setup(r => r.Roles.ToListAsync(It.IsAny<CancellationToken>()))
                .ReturnsAsync(roles);
            var controller = GetController(context, roleManagerMock, MockUserManager(), GetUserWithRole("1", "Admin"));

            var result = await controller.Create();
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.NotNull(controller.ViewBag.Departments);
            Assert.NotNull(controller.ViewBag.Roles);
        }

        [Fact]
        public async Task Create_Post_InvalidModelState_ReturnsViewWithErrors()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var roleManagerMock = MockRoleManager();
            var controller = GetController(context, roleManagerMock, MockUserManager(), GetUserWithRole("1", "Admin"));
            controller.ModelState.AddModelError("FullName", "Required");

            var model = new EmployeeViewModel();
            var result = await controller.Create(model);
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public async Task Create_Post_EmailAlreadyExists_ShowsError()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Existing User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 2 });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var controller = GetController(context, roleManagerMock, MockUserManager(), GetUserWithRole("1", "Admin"));

            var model = new EmployeeViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            var result = await controller.Create(model);
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.True(controller.TempData.ContainsKey("ToastError"));
        }

        [Fact]
        public async Task Create_Post_ValidModel_AddsEmployeeAndRedirects()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 2 });
            context.Employees.Add(new Employee { EmployeeId = 2, FullName = "Manager", Email = "manager@example.com", PhoneNumber = "1234567891", Role = "Manager", DepartmentId = 1, IsActive = true });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            var result = await controller.Create(model);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("EmployeeList", redirect.ActionName);
            Assert.Single(context.Employees.Where(e => e.Email == "test@example.com"));
        }

        [Fact]
        public async Task Edit_Get_ValidId_ReturnsViewWithEmployee()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var controller = GetController(context, MockRoleManager(), MockUserManager(), GetUserWithRole("1", "Admin"));
            var result = await controller.Edit(1);
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.IsType<Employee>(viewResult.Model);
        }

        [Fact]
        public async Task Edit_Get_InvalidId_ReturnsNotFound()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var controller = GetController(context, MockRoleManager(), MockUserManager(), GetUserWithRole("1", "Admin"));
            var result = await controller.Edit(99);
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task Edit_Post_ValidUpdate_UpdatesEmployeeAndRedirects()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Old Name",
                Email = "old@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 1 });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });
            var userManagerMock = MockUserManager();
            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));

            var updatedEmployee = new Employee
            {
                EmployeeId = 1,
                FullName = "New Name",
                Email = "new@example.com",
                PhoneNumber = "0987654321",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = false
            };

            var result = await controller.Edit(1, updatedEmployee);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("EmployeeList", redirect.ActionName);
            var emp = context.Employees.First(e => e.EmployeeId == 1);
            Assert.Equal("New Name", emp.FullName);
        }

        [Fact]
        public async Task Edit_Post_InvalidModelState_ReturnsViewWithErrors()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var userManagerMock = MockUserManager();
            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));
            controller.ModelState.AddModelError("FullName", "Required");

            var updatedEmployee = new Employee
            {
                EmployeeId = 1,
                FullName = "",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };

            var result = await controller.Edit(1, updatedEmployee);
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.False(controller.ModelState.IsValid);
        }

        [Fact]
        public async Task Edit_Post_InvalidEmployeeId_ReturnsNotFound()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var roleManagerMock = MockRoleManager();
            var userManagerMock = MockUserManager();
            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));

            var updatedEmployee = new Employee
            {
                EmployeeId = 99,
                FullName = "Test",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };

            var result = await controller.Edit(1, updatedEmployee);
            Assert.IsType<NotFoundResult>(result);
        }

        // 2. Data Consistency Cases

        [Fact]
        public async Task Create_Post_ManagerNameUpdatesCorrectly()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 2 });
            context.Employees.Add(new Employee { EmployeeId = 2, FullName = "Manager", Email = "manager@example.com", PhoneNumber = "1234567891", Role = "Manager", DepartmentId = 1, IsActive = true });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            await controller.Create(model);
            var emp = context.Employees.FirstOrDefault(e => e.Email == "test@example.com");
            Assert.NotNull(emp);
            Assert.Equal(2, emp.ManagerId);
        }

        [Fact]
        public async Task Create_Post_CreatesEmployeeLog()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 2 });
            context.Employees.Add(new Employee { EmployeeId = 2, FullName = "Manager", Email = "manager@example.com", PhoneNumber = "1234567891", Role = "Manager", DepartmentId = 1, IsActive = true });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            await controller.Create(model);
            Assert.Single(context.EmployeeLog.Where(l => l.Email == "test@example.com" && l.Operation == "Created"));
        }

        [Fact]
        public async Task Create_Post_ManagerRole_UpdatesDepartmentManagerId()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT" });
            context.Employees.Add(new Employee { EmployeeId = 10, FullName = "Admin", Email = "admin@example.com", PhoneNumber = "1234567899", Role = "Admin", DepartmentId = 1, IsActive = true });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Manager")).ReturnsAsync(new IdentityRole { Id = "role2", Name = "Manager" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("10", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "New Manager",
                Email = "manager2@example.com",
                PhoneNumber = "1234567892",
                Role = "Manager",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            await controller.Create(model);
            var dept = context.Department.First(d => d.DepartmentId == 1);
            Assert.NotNull(dept.ManagerId);
        }

        [Fact]
        public async Task DeleteConfirmed_LocksUserAndUnassignsManager()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Manager",
                Email = "manager@example.com",
                PhoneNumber = "1234567890",
                Role = "Manager",
                DepartmentId = 1,
                IsActive = true
            });
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 1, ManagerName = "Manager" });
            context.SaveChanges();

            var userManagerMock = MockUserManager();
            userManagerMock.Setup(u => u.FindByEmailAsync("manager@example.com"))
                .ReturnsAsync(new Users { Email = "manager@example.com" });
            userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<Users>()))
                .ReturnsAsync(IdentityResult.Success);

            var controller = GetController(context, MockRoleManager(), userManagerMock, GetUserWithRole("1", "Admin"));
            await controller.DeleteConfirmed(1);

            var dept = context.Department.First(d => d.DepartmentId == 1);
            Assert.Null(dept.ManagerId);
            Assert.Null(dept.ManagerName);
        }

        [Fact]
        public async Task Create_Post_InvalidManagerId_PreventsCreation()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 999 }); // Invalid manager
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Employee")).ReturnsAsync(new IdentityRole { Id = "role1", Name = "Employee" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            var result = await controller.Create(model);
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.True(controller.TempData.ContainsKey("ToastError"));
        }

        [Fact]
        public async Task Create_Post_PreventTwoManagersInSameDepartment()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 2 });
            context.Employees.Add(new Employee { EmployeeId = 2, FullName = "Manager", Email = "manager@example.com", PhoneNumber = "1234567891", Role = "Manager", DepartmentId = 1, IsActive = true });
            context.Employees.Add(new Employee { EmployeeId = 10, FullName = "Admin", Email = "admin@example.com", PhoneNumber = "1234567899", Role = "Admin", DepartmentId = 1, IsActive = true });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Manager")).ReturnsAsync(new IdentityRole { Id = "role2", Name = "Manager" });

            var userManagerMock = MockUserManager();

            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("10", "Admin"));
            var model = new EmployeeViewModel
            {
                FullName = "New Manager",
                Email = "manager2@example.com",
                PhoneNumber = "1234567892",
                Role = "Manager",
                IsActive = true,
                DepartmentId = 1,
                LeaveBalance = 20
            };

            // Simulate business logic: prevent two managers in same department
            // (You may need to add this logic in your controller if not present)
            var result = await controller.Create(model);
            var viewResult = Assert.IsType<ViewResult>(result);
            // Should not allow creation, so no new manager assigned
            Assert.True(controller.TempData.ContainsKey("ToastError"));
        }

        [Fact]
        public async Task Edit_ChangeDepartment_EnsuresManagerConsistency()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 1 });
            context.Department.Add(new Department { DepartmentId = 2, DepartmentName = "HR" });
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Manager",
                DepartmentId = 1,
                IsActive = true
            });
            context.SaveChanges();

            var roleManagerMock = MockRoleManager();
            roleManagerMock.Setup(r => r.FindByNameAsync("Manager")).ReturnsAsync(new IdentityRole { Id = "role2", Name = "Manager" });
            var userManagerMock = MockUserManager();
            var controller = GetController(context, roleManagerMock, userManagerMock, GetUserWithRole("1", "Admin"));

            var updatedEmployee = new Employee
            {
                EmployeeId = 1,
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Manager",
                DepartmentId = 2,
                IsActive = true
            };

            var result = await controller.Edit(1, updatedEmployee);
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("EmployeeList", redirect.ActionName);

            // ManagerId in old department should be null
            var oldDept = context.Department.First(d => d.DepartmentId == 1);
            Assert.Null(oldDept.ManagerId);
        }

        [Fact]
        public async Task DeleteConfirmed_PreventDeleteIfManagerAssigned()
        {
            using var context = GetDbContext(Guid.NewGuid().ToString());
            context.Employees.Add(new Employee
            {
                EmployeeId = 1,
                FullName = "Manager",
                Email = "manager@example.com",
                PhoneNumber = "1234567890",
                Role = "Manager",
                DepartmentId = 1,
                IsActive = true
            });
            context.Department.Add(new Department { DepartmentId = 1, DepartmentName = "IT", ManagerId = 1, ManagerName = "Manager" });
            context.SaveChanges();

            var userManagerMock = MockUserManager();
            userManagerMock.Setup(u => u.FindByEmailAsync("manager@example.com"))
                .ReturnsAsync(new Users { Email = "manager@example.com" });
            userManagerMock.Setup(u => u.UpdateAsync(It.IsAny<Users>()))
                .ReturnsAsync(IdentityResult.Success);

            var controller = GetController(context, MockRoleManager(), userManagerMock, GetUserWithRole("1", "Admin"));

            // Simulate business logic: prevent delete if assigned as manager
            // (You may need to add this logic in your controller if not present)
            var result = await controller.DeleteConfirmed(1);
            // Should not delete, department still has manager
            var dept = context.Department.First(d => d.DepartmentId == 1);
            Assert.Equal(1, dept.ManagerId);
        }
    }
}