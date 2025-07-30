using EMS.Controllers;
using EMS.Models;
using EMS.ViewModels;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
    public class ManagerControllerTests
    {
        private AppDbContext GetDbContext(string dbName)
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(databaseName: dbName)
                .Options;
            return new AppDbContext(options);
        }
        private UserManager<Users> GetMockUserManager()
        {
            var store = new Mock<IUserStore<Users>>();
            return new UserManager<Users>(
                store.Object, null, null, null, null, null, null, null, null);
        }
        private RoleManager<IdentityRole> GetMockRoleManager()
        {
            var store = new Mock<IRoleStore<IdentityRole>>();
            return new RoleManager<IdentityRole>(
                store.Object, null, null, null, null);
        }

        private ManagerController GetControllerWithUser(AppDbContext context, string userId = "user1")
        {
            var roleManager = GetMockRoleManager();
            var userManager = GetMockUserManager();

            var controller = new ManagerController(context, roleManager, userManager);

            var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userId)
            }, "mock"));

            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = user }
            };

            return controller;
        }


        [Fact]
        public async Task Index_ReturnsViewWithEmployeeProfile_WhenUserExists()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = GetDbContext(dbName);
            var employee = new Employee
            {
                UserId = "user1",
                EmployeeId = 1,
                Department = new Department { DepartmentId = 1, DepartmentName = "IT" },
                DepartmentId = 1,
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                IsActive = true
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Index();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<EmployeeProfileViewModel>(viewResult.Model);
            Assert.Equal(employee.EmployeeId, model.Employee.EmployeeId);
        }
        [Fact]
        public async Task Index_ReturnsUnauthorized_WhenUserIdIsNull()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var roleManager = GetMockRoleManager();
            var userManager = GetMockUserManager();

            var controller = new ManagerController(context, roleManager, userManager);
            controller.ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal() }
            };

            // Act
            var result = await controller.Index();

            // Assert
            Assert.IsType<UnauthorizedResult>(result);
        }

        [Fact]
        public async Task Index_ReturnsNotFound_WhenEmployeeNotFound()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Index();

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Employee not found for current user.", notFound.Value);
        }

        [Fact]
        public async Task ApproveList_ReturnsNotFound_WhenManagerNotFound()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.ApproveList();

            // Assert
            var notFound = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Manager not found.", notFound.Value);
        }

        [Fact]
        public async Task ApproveList_ReturnsViewWithPendingLeaves()
        {
            // Arrange
            var dbName = Guid.NewGuid().ToString();
            using var context = GetDbContext(dbName);

            var manager = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "Manager",
                Email = "manager@example.com",
                PhoneNumber = "1234567890",
                Role = "Manager",
                DepartmentId = 1,
                IsActive = true
            };
            var emp1 = new Employee
            {
                EmployeeId = 2,
                ManagerId = 1,
                FullName = "Emp1",
                Email = "emp1@example.com",
                PhoneNumber = "1234567891",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };
            var emp2 = new Employee
            {
                EmployeeId = 3,
                ManagerId = 1,
                FullName = "Emp2",
                Email = "emp2@example.com",
                PhoneNumber = "1234567892",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };
            context.Employees.AddRange(manager, emp1, emp2);

            var leave1 = new LeaveRequest { LeaveRequestId = 1, EmployeeId = 2, Status = "Pending", StartDate = DateTime.Today, EndDate = DateTime.Today };
            var leave2 = new LeaveRequest { LeaveRequestId = 2, EmployeeId = 3, Status = "Pending", StartDate = DateTime.Today, EndDate = DateTime.Today };
            var leave3 = new LeaveRequest { LeaveRequestId = 3, EmployeeId = 2, Status = "Approved", StartDate = DateTime.Today, EndDate = DateTime.Today };
            context.LeaveRequests.AddRange(leave1, leave2, leave3);
            context.SaveChanges();

            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.ApproveList();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<LeaveRequest>>(viewResult.Model);
            Assert.Equal(2, model.Count);
            Assert.All(model, l => Assert.Equal("Pending", l.Status));
        }
        [Fact]
        public async Task ApprovalsGet_ReturnsNotFound_WhenLeaveNotFound()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Approvals(999);

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task ApprovalsGet_ReturnsView_WhenLeaveFound()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "Test User",
                Email = "test@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1,
                IsActive = true
            };
            context.Employees.Add(employee);

            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                Status = "Pending",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today
            };
            context.LeaveRequests.Add(leave);
            context.SaveChanges();

            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Approvals(1);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.IsType<LeaveRequest>(viewResult.Model);
        }

        [Fact]
        public async Task ApprovalsPost_ReturnsNotFound_WhenLeaveNotFound()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Approvals(999, "Approved");

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact]
        public async Task ApprovalsPost_ApprovesLeaveAndUpdatesBalance()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                Status = "Pending",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(1)
            };
            var balance = new LeaveBalance
            {
                LeaveBalanceId = 1,
                EmployeeId = 1,
                TotalLeaves = 10,
                LeavesTaken = 0
            };
            context.LeaveRequests.Add(leave);
            context.LeaveBalances.Add(balance);
            context.SaveChanges();

            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            var redirect = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal(nameof(controller.ApproveList), redirect.ActionName);

            var updatedBalance = context.LeaveBalances.First();
            Assert.Equal(2, updatedBalance.LeavesTaken); // 2 days
        }

        [Fact]
        public async Task ApprovalsPost_ReturnsViewWithError_WhenInsufficientBalance()
        {
            // Arrange
            using var context = GetDbContext(Guid.NewGuid().ToString());
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                Status = "Pending",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(4)
            };
            var balance = new LeaveBalance
            {
                LeaveBalanceId = 1,
                EmployeeId = 1,
                TotalLeaves = 3,
                LeavesTaken = 2
            };
            context.LeaveRequests.Add(leave);
            context.LeaveBalances.Add(balance);
            context.SaveChanges();

            var controller = GetControllerWithUser(context);

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.IsType<LeaveRequest>(viewResult.Model);
            Assert.True(controller.ModelState.ErrorCount > 0);
        }
    }
}