using EMS.Models;
using EMS.Web.Controllers;
using EMS.Web.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Moq;
using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Xunit;

public class LeaveControllerTests
{
    private async Task<AppDbContext> GetInMemoryDbContextAsync()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new AppDbContext(options);

        var employee = new Employee
        {
            EmployeeId = 1,
            FullName = "John Doe",
            Email = "john@example.com",
            PhoneNumber = "1234567890",
            Role = "Developer",
            UserId = "uid123",
            LeaveBalance = 10
        };

        var balance = new LeaveBalance
        {
            EmployeeId = 1,
            TotalLeaves = 12,
            LeavesTaken = 2
        };

        context.Employees.Add(employee);
        context.LeaveBalances.Add(balance);
        await context.SaveChangesAsync();

        return context;
    }


    private LeaveController SetupController(AppDbContext context, string userId = "user1")
    {
        var controller = new LeaveController(context);

        var user = new ClaimsPrincipal(new ClaimsIdentity(new[]
        {
            new Claim(ClaimTypes.NameIdentifier, userId)
        }, "mock"));

        controller.ControllerContext = new ControllerContext
        {
            HttpContext = new DefaultHttpContext { User = user }
        };

        controller.ControllerContext.HttpContext.Session = new Mock<ISession>().Object;

        return controller;
    }

    [Fact]
    public async Task Apply_Post_ValidLeave_CreatesLeaveRequest()
    {
        var context = await GetInMemoryDbContextAsync();
        var controller = SetupController(context);

        var leave = new LeaveRequest
        {
            EmployeeId = 1,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(2),
            Reason = "Medical",
            Status = "Pending"
        };

        var result = await controller.Apply(leave);

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("MyLeaves", redirect.ActionName);
        Assert.Single(await context.LeaveRequests.ToListAsync());
    }

    [Fact]
    public async Task MyLeaves_ReturnsLeavesForEmployee()
    {
        var context = await GetInMemoryDbContextAsync();
        context.LeaveRequests.Add(new LeaveRequest
        {
            LeaveRequestId = 1,
            EmployeeId = 1,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(1),
            Status = "Pending",
            Reason = "Test"
        });
        await context.SaveChangesAsync();

        var controller = SetupController(context);
        var result = await controller.MyLeaves(1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<LeaveRequest>>(view.Model);
        Assert.Single(model);
    }

    [Fact]
    public async Task ApproveList_ReturnsPendingLeaves()
    {
        var context = await GetInMemoryDbContextAsync();
        context.LeaveRequests.Add(new LeaveRequest
        {
            LeaveRequestId = 1,
            EmployeeId = 1,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(1),
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var controller = SetupController(context);
        var result = await controller.ApproveList();

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsAssignableFrom<List<LeaveRequest>>(view.Model);
        Assert.Single(model);
    }

    [Fact]
    public async Task Approvals_Get_ValidId_ReturnsView()
    {
        var context = await GetInMemoryDbContextAsync();
        context.LeaveRequests.Add(new LeaveRequest
        {
            LeaveRequestId = 1,
            EmployeeId = 1,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(1),
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var controller = SetupController(context);
        var result = await controller.Approvals(1);

        var view = Assert.IsType<ViewResult>(result);
        var model = Assert.IsType<LeaveRequest>(view.Model);
        Assert.Equal(1, model.LeaveRequestId);
    }

    [Fact]
    public async Task Approvals_Post_ApprovesLeaveAndUpdatesBalance()
    {
        var context = await GetInMemoryDbContextAsync();
        context.LeaveRequests.Add(new LeaveRequest
        {
            LeaveRequestId = 1,
            EmployeeId = 1,
            StartDate = DateTime.Today,
            EndDate = DateTime.Today.AddDays(1),
            Status = "Pending"
        });
        await context.SaveChangesAsync();

        var controller = SetupController(context);
        var result = await controller.Approvals(1, "Approved");

        var redirect = Assert.IsType<RedirectToActionResult>(result);
        Assert.Equal("ApproveList", redirect.ActionName);

        var leave = await context.LeaveRequests.FindAsync(1);
        Assert.Equal("Approved", leave.Status);

        var balance = await context.LeaveBalances.FirstOrDefaultAsync(b => b.EmployeeId == 1);
        Assert.Equal(2, balance.LeavesTaken); // 1 day + 1 for inclusive
    }
}