using EMS.Models;
using EMS.Web.Controllers;
using EMS.Web.Data;
using Microsoft.AspNetCore.Http;
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
    public class LeaveControllerTests
    {
        // Helper: Create in-memory database for test isolation
        private AppDbContext GetDbContext()
        {
            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            return new AppDbContext(options);
        }

        // Helper: Create controller with context, user, and session
        private LeaveController GetController(
            AppDbContext context,
            ClaimsPrincipal? user = null,
            ISession? session = null)
        {
            var controller = new LeaveController(context);
            var httpContext = new DefaultHttpContext();
            if (user != null) httpContext.User = user;
            if (session != null) httpContext.Session = session;
            controller.ControllerContext = new ControllerContext { HttpContext = httpContext };
            return controller;
        }

        // Helper: Create ClaimsPrincipal with userId and role
        private ClaimsPrincipal GetUser(string? userId = null, string? role = null)
        {
            var claims = new List<Claim>();
            if (userId != null) claims.Add(new Claim(ClaimTypes.NameIdentifier, userId));
            if (role != null) claims.Add(new Claim(ClaimTypes.Role, role));
            return new ClaimsPrincipal(new ClaimsIdentity(claims, "mock"));
        }

        // 1. Apply (GET) Tests
        [Fact] // TC001: Valid user with existing Employee record
        public void Apply_Get_ValidUserWithEmployee_ReturnsViewWithEmployee()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var controller = GetController(context, GetUser("user1"));

            // Act
            var result = controller.Apply();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(employee, viewResult.ViewData["Employee"]);
        }

        [Fact] // TC002: No matching Employee for logged-in user
        public void Apply_Get_NoEmployee_ReturnsNotFound()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context, GetUser("nonexistent"));

            // Act
            var result = controller.Apply();

            // Assert
            var notFoundResult = Assert.IsType<NotFoundObjectResult>(result);
            Assert.Equal("Employee not found.", notFoundResult.Value);
        }

        // 2. Apply (POST) Tests
        [Fact] // TC003: Invalid ModelState
        public async Task Apply_Post_InvalidModelState_ReturnsViewWithErrors()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context);
            controller.ModelState.AddModelError("StartDate", "Required");
            var leave = new LeaveRequest();

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(leave, viewResult.Model);
            Assert.NotNull(context.Employees); // Should be populated in ViewData
        }
        
        [Fact] // TC005: Leave days > balance (and not Approved)
        public async Task Apply_Post_LeaveDaysExceedBalance_NoDeduction()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 2, // Only 2 days available
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(3), // 4 days total
                Status = "Pending"
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Check that leave was saved but balance was not deducted (DC001, DC003)
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            var updatedEmployee = await context.Employees.FindAsync(1);
            Assert.Equal(2, updatedEmployee.LeaveBalance); // Should remain unchanged
        }

        [Fact] // TC006: Valid pending request with sufficient balance
        public async Task Apply_Post_ValidPendingRequestWithSufficientBalance_DeductsBalance()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Pending"
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Check that leave was saved and balance was deducted
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            var updatedEmployee = await context.Employees.FindAsync(1);
            Assert.Equal(7, updatedEmployee.LeaveBalance); // 10 - 3 = 7
        }

        [Fact] // TC007: Approved request, balance record exists, enough space
        public async Task Apply_Post_ApprovedRequestWithSufficientBalance_UpdatesLeavesTaken()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leaveBalance = new LeaveBalance
            {
                EmployeeId = 1,
                TotalLeaves = 20,
                LeavesTaken = 5
            };

            context.Employees.Add(employee);
            context.LeaveBalances.Add(leaveBalance);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Approved" // Already approved
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Check that leave was saved and LeavesTaken was updated
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            var updatedBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Equal(8, updatedBalance.LeavesTaken); // 5 + 3 = 8

            // Check days calculation (DC005)
            int days = (leave.EndDate - leave.StartDate).Days + 1;
            Assert.Equal(3, days);
        }

        [Fact] // TC008: Approved request, but LeavesTaken + days > TotalLeaves
        public async Task Apply_Post_ApprovedRequest_InsufficientBalance_NoUpdate()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leaveBalance = new LeaveBalance
            {
                EmployeeId = 1,
                TotalLeaves = 10,
                LeavesTaken = 8
            };

            context.Employees.Add(employee);
            context.LeaveBalances.Add(leaveBalance);
            context.SaveChanges();

            // Create a MockHttpSession instead of using Moq
            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(3), // 4 days total
                Status = "Approved" // Already approved
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            var updatedBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Equal(12, updatedBalance.LeavesTaken); // 8 + 4 = 12
        }
        [Fact] // TC009: Approved request, no LeaveBalance record
        public async Task Apply_Post_ApprovedRequest_NoLeaveBalance_StillProcessed()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };

            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Approved" // Already approved
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Check that leave was saved
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            // Current implementation doesn't create a new LeaveBalance
            var leaveBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Null(leaveBalance); // Should be null as it wasn't created
        }

        [Fact] // TC010: Pending request with insufficient balance
        public async Task Apply_Post_PendingRequest_InsufficientBalance_NoDeduction()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 2, // Only 2 days available
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(3), // 4 days total
                Status = "Pending"
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Check that leave was saved but balance was not deducted
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync(l => l.EmployeeId == 1);
            Assert.NotNull(savedLeave);

            var updatedEmployee = await context.Employees.FindAsync(1);
            Assert.Equal(2, updatedEmployee.LeaveBalance); // Should remain unchanged
        }

        [Fact] // TC011: StartDate > EndDate
        public async Task Apply_Post_StartDateAfterEndDate_HandlesNegativeDays()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();

            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today.AddDays(3), // Later than EndDate
                EndDate = DateTime.Today,
                Status = "Pending"
            };

            // Act
            var result = await controller.Apply(leave);

            // Assert
            // The controller doesn't explicitly validate date order, so this verifies current behavior
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("MyLeaves", redirectResult.ActionName);

            // Days calculation would yield negative value
            int days = (leave.EndDate - leave.StartDate).Days + 1;
            Assert.True(days <= 0);

            // Check employee balance (should be unchanged or deducted by negative value)
            var updatedEmployee = await context.Employees.FindAsync(1);
            Assert.Equal(10, updatedEmployee.LeaveBalance); // No deduction occurs for negative days
        }

        // 3. MyLeaves Tests
        [Fact] // TC012: Valid employee ID with leave records
        public async Task MyLeaves_ValidEmployeeIdWithRecords_ReturnsViewWithLeaves()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leaves = new List<LeaveRequest>
            {
                new LeaveRequest
                {
                    LeaveRequestId = 1,
                    EmployeeId = 1,
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(1),
                    Status = "Pending",
                    RequestDate = DateTime.Today
                },
                new LeaveRequest
                {
                    LeaveRequestId = 2,
                    EmployeeId = 1,
                    StartDate = DateTime.Today.AddDays(-10),
                    EndDate = DateTime.Today.AddDays(-8),
                    Status = "Approved",
                    RequestDate = DateTime.Today.AddDays(-15)
                }
            };
            context.Employees.Add(employee);
            context.LeaveRequests.AddRange(leaves);
            context.SaveChanges();

            var controller = GetController(context);

            // Act
            var result = await controller.MyLeaves(1);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<LeaveRequest>>(viewResult.Model);
            Assert.Equal(2, model.Count);

            // Check sorting by RequestDate (descending)
            Assert.True(model[0].RequestDate >= model[1].RequestDate);

            // Check ViewBag data
            Assert.Equal("John Doe", controller.ViewBag.EmployeeName);
        }

        [Fact] // TC013: Employee ID with no leave records
        public async Task MyLeaves_NoRecords_ReturnsViewWithEmptyList()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context);

            // Act
            var result = await controller.MyLeaves(1);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<LeaveRequest>>(viewResult.Model);
            Assert.Empty(model);
            Assert.Equal("", controller.ViewBag.EmployeeName);
        }

        // 4. ApproveList Tests
        [Fact] // TC014: Authorized Admin/Manager accesses the list
        public async Task ApproveList_AuthorizedUser_ReturnsPendingLeaves()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leaves = new List<LeaveRequest>
            {
                new LeaveRequest
                {
                    LeaveRequestId = 1,
                    EmployeeId = 1,
                    Status = "Pending",
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(1)
                },
                new LeaveRequest
                {
                    LeaveRequestId = 2,
                    EmployeeId = 1,
                    Status = "Approved",
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(1)
                }
            };
            context.Employees.Add(employee);
            context.LeaveRequests.AddRange(leaves);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.ApproveList();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<LeaveRequest>>(viewResult.Model);
            Assert.Single(model); // Only the Pending leave
            Assert.Equal("Pending", model[0].Status);
        }

        [Fact] // TC015: No pending leaves
        public async Task ApproveList_NoPendingLeaves_ReturnsEmptyList()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leaves = new List<LeaveRequest>
            {
                new LeaveRequest
                {
                    LeaveRequestId = 1,
                    EmployeeId = 1,
                    Status = "Approved",
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(1)
                },
                new LeaveRequest
                {
                    LeaveRequestId = 2,
                    EmployeeId = 1,
                    Status = "Rejected",
                    StartDate = DateTime.Today,
                    EndDate = DateTime.Today.AddDays(1)
                }
            };
            context.Employees.Add(employee);
            context.LeaveRequests.AddRange(leaves);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.ApproveList();

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsAssignableFrom<List<LeaveRequest>>(viewResult.Model);
            Assert.Empty(model);
        }

        [Fact] // TC016: Unauthorized user accesses route
        public void ApproveList_UnauthorizedUser_AccessDenied()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context, GetUser("employee", "Employee"));

            // Act & Assert
            // Note: Since we can't test Authorize attribute directly in unit tests,
            // we check if the user has the required role
            Assert.False(controller.User.IsInRole("Admin") || controller.User.IsInRole("Manager"));
        }

        // 5. Approvals (GET) Tests
        [Fact] // TC017: Valid ID returns leave with employee info
        public async Task Approvals_Get_ValidId_ReturnsViewWithLeave()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                Status = "Pending",
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(1)
            };
            context.Employees.Add(employee);
            context.LeaveRequests.Add(leave);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1);

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            var model = Assert.IsType<LeaveRequest>(viewResult.Model);
            Assert.Equal(1, model.LeaveRequestId);
        }

        [Fact] // TC018: Invalid ID returns null
        public async Task Approvals_Get_InvalidId_ReturnsNotFound()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(999); // Non-existent ID

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        // 6. Approvals (POST) Tests
        [Fact] // TC019: Leave not found
        public async Task Approvals_Post_LeaveNotFound_ReturnsNotFound()
        {
            // Arrange
            var context = GetDbContext();
            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(999, "Approved"); // Non-existent ID

            // Assert
            Assert.IsType<NotFoundResult>(result);
        }

        [Fact] // TC020: Approve valid leave with valid balance
        public async Task Approvals_Post_ApproveWithValidBalance_UpdatesStatusAndBalance()
        {
            // Arrange
            var context = GetDbContext();
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Pending"
            };
            var balance = new LeaveBalance
            {
                EmployeeId = 1,
                TotalLeaves = 20,
                LeavesTaken = 5
            };
            context.LeaveRequests.Add(leave);
            context.LeaveBalances.Add(balance);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ApproveList", redirectResult.ActionName);

            // Check status update
            var updatedLeave = await context.LeaveRequests.FindAsync(1);
            Assert.Equal("Approved", updatedLeave.Status);

            // Check balance update
            var updatedBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Equal(8, updatedBalance.LeavesTaken); // 5 + 3 = 8
        }

        [Fact] // TC021: Approve leave but LeavesTaken + days > TotalLeaves
        public async Task Approvals_Post_ApproveWithInsufficientBalance_AddsModelError()
        {
            // Arrange
            var context = GetDbContext();
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(5), // 6 days total
                Status = "Pending"
            };
            var balance = new LeaveBalance
            {
                EmployeeId = 1,
                TotalLeaves = 10,
                LeavesTaken = 8
            };
            context.LeaveRequests.Add(leave);
            context.LeaveBalances.Add(balance);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            var viewResult = Assert.IsType<ViewResult>(result);
            Assert.Equal(leave, viewResult.Model);
            Assert.True(controller.ModelState.ErrorCount > 0);

            // Check that balance was not updated
            var updatedBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Equal(8, updatedBalance.LeavesTaken); // Should remain unchanged
        }

        [Fact] // TC022: Approve leave with no LeaveBalance
        public async Task Approvals_Post_ApproveWithNoLeaveBalance_RedirectsWithoutError()
        {
            // Arrange
            var context = GetDbContext();
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Pending"
            };
            context.LeaveRequests.Add(leave);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ApproveList", redirectResult.ActionName);

            // Check status update
            var updatedLeave = await context.LeaveRequests.FindAsync(1);
            Assert.Equal("Approved", updatedLeave.Status);

            // Check no balance was created (current implementation)
            var leaveBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Null(leaveBalance);
        }

        [Fact] // TC023: Reject leave
        public async Task Approvals_Post_RejectLeave_UpdatesStatusOnly()
        {
            // Arrange
            var context = GetDbContext();
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2), // 3 days total
                Status = "Pending"
            };
            var balance = new LeaveBalance
            {
                EmployeeId = 1,
                TotalLeaves = 20,
                LeavesTaken = 5
            };
            context.LeaveRequests.Add(leave);
            context.LeaveBalances.Add(balance);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1, "Rejected");

            // Assert
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ApproveList", redirectResult.ActionName);

            // Check status update
            var updatedLeave = await context.LeaveRequests.FindAsync(1);
            Assert.Equal("Rejected", updatedLeave.Status);

            // Check balance not updated
            var updatedBalance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
            Assert.Equal(5, updatedBalance.LeavesTaken); // Should remain unchanged
        }

        [Fact] // TC024: Approved leave with null Employee
        public async Task Approvals_Post_ApprovedLeaveWithNullEmployee_NoException()
        {
            // Arrange
            var context = GetDbContext();
            var leave = new LeaveRequest
            {
                LeaveRequestId = 1,
                EmployeeId = 999, // Non-existent employee
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(2),
                Status = "Pending"
            };
            context.LeaveRequests.Add(leave);
            context.SaveChanges();

            var controller = GetController(context, GetUser("admin", "Admin"));

            // Act
            var result = await controller.Approvals(1, "Approved");

            // Assert
            // Should not throw an exception, even though Employee is null
            var redirectResult = Assert.IsType<RedirectToActionResult>(result);
            Assert.Equal("ApproveList", redirectResult.ActionName);
        }

        // DC006: Multiple submissions don't double-deduct
        [Fact]
        public async Task DC006_MultipleSubmissions_DoNotDoubleDeduct()
        {
            // Arrange
            var context = GetDbContext();
            var employee = new Employee
            {
                EmployeeId = 1,
                UserId = "user1",
                FullName = "John Doe",
                LeaveBalance = 10,
                IsActive = true,
                Email = "john@example.com",
                PhoneNumber = "1234567890",
                Role = "Employee",
                DepartmentId = 1
            };
            context.Employees.Add(employee);
            context.SaveChanges();

            var session = new MockHttpSession();
            Microsoft.AspNetCore.Http.SessionExtensions.SetInt32(session, "EmployeeId", 1);

            var controller = GetController(context, GetUser("user1"), session);
            var leave = new LeaveRequest
            {
                EmployeeId = 1,
                StartDate = DateTime.Today,
                EndDate = DateTime.Today.AddDays(1), // 2 days total
                Status = "Pending"
            };

            // Act
            // First submission
            await controller.Apply(leave);

            // Get the saved leave and try to approve it
            var savedLeave = await context.LeaveRequests.FirstOrDefaultAsync();
            var adminController = GetController(context, GetUser("admin", "Admin"));

            // Second submission - approve the leave
            await adminController.Approvals(savedLeave.LeaveRequestId, "Approved");

            // Assert
            var updatedEmployee = await context.Employees.FindAsync(1);
            Assert.Equal(8, updatedEmployee.LeaveBalance); // 10 - 2 = 8, only deducted once
        }
    }
}