using EMS.Models;
using EMS.Services.Interface;
using EMS.ViewModels;
using EMS.Web.Data;
using EMS.Web.Models;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;


public class AccountController : Controller
{
    private readonly IConfiguration _config;
    private readonly IEmailService _emailService;
    private readonly UserManager<Users> _userManager;
    private readonly SignInManager<Users> _signInManager;
    private readonly AppDbContext _context;
    private readonly ILogger<AccountController> _logger;

    public AccountController(IEmailService emailService, UserManager<Users> userManager, SignInManager<Users> signInManager, IConfiguration config, AppDbContext context, ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _config = config;
        _emailService = emailService;
        _context = context;
        _logger = logger; 
    }

    // Account/Login
    public IActionResult Login()
    {
        return View();
    }

    // Account/Register - Get
    public IActionResult Register()
    {
        return View();
    }

    // Account/Register - Post
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
            return BadRequest(ModelState);

        var employee = await _context.Employees
            .FirstOrDefaultAsync(e => e.Email == model.Email );
        model.FullName = employee?.FullName;
        model.PhoneNumber = employee?.PhoneNumber;
        if (employee == null)
        {
            ModelState.AddModelError("", "You are not a registered employee. Contact admin.");
            TempData["ToastError"] = "You are not a registered employee. Please contact Admin.";
            return View(model); 
        }

        var user = new Users
        {
            UserName = model.Email,
            Email = model.Email,
            FullName = model.FullName,
            PhoneNumber = model.PhoneNumber
        };

        var result = await _userManager.CreateAsync(user, model.Password);
        if (result.Succeeded)
        {
            user = await _userManager.FindByEmailAsync(user.Email);
        }
        var LoginLogs = new LoginActivityLogs
        {
            userId = user.Id,
            LoginTime = DateTime.UtcNow,
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
            IsSuccessful = result.Succeeded,
            employeeId = employee.EmployeeId,
            Email = employee.Email
        };

        _context.LoginActivityLogs.Add(LoginLogs);
        await _context.SaveChangesAsync();

        if (result.Succeeded)
        {
            await _userManager.AddToRoleAsync(user, employee.Role);
            HttpContext.Session.SetString("LoginTime", DateTime.UtcNow.ToString());
            TempData["ToastSuccess"] = "Registration successful. Please log in.";

            return RedirectToAction("Login");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }
        TempData["ToastError"] = "Registration failed. Please correct the errors.";

        return View(model);
    }
    
    // Account/Login - Post
    [HttpPost]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            TempData["ToastError"] = "Please fill in all required fields.";
            return View(model);
        }

        var user = await _userManager.FindByEmailAsync(model.Email);

        // Check if user exists
        if (user == null)
        {
            TempData["ToastError"] = "Invalid login attempt. User not found.";
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        // Check if user is locked out
        if (user.LockoutEnabled && user.LockoutEnd.HasValue && user.LockoutEnd.Value > DateTimeOffset.UtcNow)
        {
            TempData["ToastError"] = "This account is locked. Contact administrator.";
            ModelState.AddModelError(string.Empty, "This account has been disabled. Please contact administrator.");
            return View(model);
        }

        // Check if password is correct
        if (!await _userManager.CheckPasswordAsync(user, model.Password))
        {
            TempData["ToastError"] = "Invalid login attempt. Please check your password.";
            ModelState.AddModelError(string.Empty, "Invalid login attempt.");
            return View(model);
        }

        // Get roles
        var roles = await _userManager.GetRolesAsync(user);

        // JWT Claims
        var authClaims = new List<Claim>
        {
            new Claim(ClaimTypes.Name, user.UserName),
            new Claim(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),
            new Claim(ClaimTypes.NameIdentifier, user.Id)
        };
        authClaims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

        var authSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_config["JwtSettings:Key"]));
        var token = new JwtSecurityToken(
            issuer: _config["JwtSettings:Issuer"],
            audience: _config["JwtSettings:Audience"],
            expires: DateTime.UtcNow.AddMinutes(60),
            claims: authClaims,
            signingCredentials: new SigningCredentials(authSigningKey, SecurityAlgorithms.HmacSha256)
        );

        var jwtToken = new JwtSecurityTokenHandler().WriteToken(token);

        // Sign in user with cookie-based principal
        var claimsIdentity = new ClaimsIdentity(authClaims, CookieAuthenticationDefaults.AuthenticationScheme);
        var claimsPrincipal = new ClaimsPrincipal(claimsIdentity);
        await HttpContext.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme, claimsPrincipal);

        // Store JWT in cookie
        Response.Cookies.Append("jwtToken", jwtToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            Expires = DateTime.UtcNow.AddMinutes(60)
        });

        // Save login activity
        var employee = await _context.Employees.FirstOrDefaultAsync(e => e.Email == model.Email);
        if (employee != null)
        {
            var log = new LoginActivityLogs
            {
                userId = user.Id,
                LoginTime = DateTime.UtcNow,
                IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString(),
                employeeId = employee.EmployeeId,
                Email = employee.Email
            };
            _context.LoginActivityLogs.Add(log);

            if (string.IsNullOrEmpty(employee.UserId))
            {
                employee.UserId = user.Id;
                _context.Employees.Update(employee);
            }

            await _context.SaveChangesAsync();

            HttpContext.Session.SetString("LoginTime", DateTime.UtcNow.ToString());
            HttpContext.Session.SetInt32("EmployeeId", employee.EmployeeId);
        }
        TempData["ToastSuccess"] = "Login successful!";
        // Redirect based on role
        if (roles.Contains("Admin"))
            return RedirectToAction("Index", "Manager");
        else if (roles.Contains("Manager"))
            return RedirectToAction("Index", "Manager");
        else
            return RedirectToAction("Index", "Employee");
    }


    // /Account/ForgotPassword - Get
    public IActionResult ForgotPassword()
    {
        return View();
    }

    // Account/ForgotPassword - Post (Which trigger the email service)
    [HttpPost]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            return RedirectToAction("ForgotPasswordConfirmation");
        }

        // Generate 6-digit OTP
        var otp = new Random().Next(100000, 999999).ToString();

        // Store OTP & Timestamp in TempData 
        TempData["OTP"] = otp;
        TempData["OTPEmail"] = model.Email;
        TempData["OTPExpiry"] = DateTime.UtcNow.AddMinutes(10).ToString();

        var body = $"Your OTP to reset your EMS password is: <b>{otp}</b><br/>This OTP is valid for 10 minutes.";
        await _emailService.SendEmailAsync(model.Email, "EMS Password Reset OTP", body);
        return RedirectToAction("VerifyOtp", new { email = model.Email });
    }

    // Account/VerifyOtp?email={emailID} - Get
    [HttpGet]
    public IActionResult VerifyOtp(string email)
    {
        return View(new VerifyOtpViewModel { Email = email });
    }

    // Account/VerifyOtp?email={emailID} - Post 
    [HttpPost]
    public IActionResult VerifyOtp(VerifyOtpViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var expectedOtp = TempData["OTP"] as string;
        var otpEmail = TempData["OTPEmail"] as string;
        var expiryStr = TempData["OTPExpiry"] as string;
        DateTime.TryParse(expiryStr, out DateTime expiryTime);

        if (expectedOtp == null || otpEmail != model.Email || model.Otp != expectedOtp || DateTime.UtcNow > expiryTime)
        {
            ModelState.AddModelError("", "Invalid or expired OTP.");
            return View(model);
        }

        // OTP is valid
        TempData["VerifiedEmail"] = model.Email;
        TempData["VerifiedOtp"] = model.Otp;

        return RedirectToAction("ResetPassword");
    }

    // Account/ResetPassword - Get
    [HttpGet]
    public IActionResult ResetPassword()
    {
        var email = TempData["VerifiedEmail"] as string;
        var otp = TempData["VerifiedOtp"] as string;
        if (email == null || otp == null)
            return RedirectToAction("ForgotPassword");

        return View(new ResetPasswordViewModel { Email = email, Otp = otp });
    }

    // /Account/ResetPassword - Post
    [HttpPost]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.FindByEmailAsync(model.Email);
        if (user == null)
        {
            ModelState.AddModelError("", "User not found.");
            return View(model);
        }

        var resetToken = await _userManager.GeneratePasswordResetTokenAsync(user);
        var result = await _userManager.ResetPasswordAsync(user, resetToken, model.NewPassword);

        if (result.Succeeded)
        {
            return RedirectToAction("ResetPasswordConfirmation");
        }

        foreach (var error in result.Errors)
        {
            ModelState.AddModelError("", error.Description);
        }
        return View(model);
    }

    // Account/ResetPasswordConfirmation
    public IActionResult ResetPasswordConfirmation()
    {
        return View();
    }

    // Account/ChangePassword - Get
    public IActionResult ChangePassword()
    {
        return View();
    }

    // Account/ChangePassword - Post
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> ChangePassword(ChangePasswordViewModel model)
    {
        if (!ModelState.IsValid)
            return View(model);

        var user = await _userManager.GetUserAsync(User);
        if (user == null)
            return RedirectToAction("ChangePassword");

        var result = await _userManager.ChangePasswordAsync(user, model.OldPassword, model.NewPassword);

        if (result.Succeeded)
            return RedirectToAction("ForgotPasswordConfirmation");

        foreach (var error in result.Errors)
            ModelState.AddModelError("", error.Description);

        return View(model);
    }

    // Account/ChangePasswordConfirmation - Get
    public IActionResult ChangePasswordConfirmation()
    {
        return View();
    }

    // /Account/ForgotPassword - Get
    public IActionResult ForgotPasswordConfirmation()
    {
        return View();
    }

    [Authorize]
    [HttpPost]
    public async Task<IActionResult> Logout()
    {
        var userId = _userManager.GetUserId(User);


        var loginTimeStr = HttpContext.Session.GetString("LoginTime");
        if (DateTime.TryParse(loginTimeStr, out var loginTime))
        {
            var logoutTime = DateTime.UtcNow;
            var duration = logoutTime - loginTime;

            _logger.LogInformation("User logged out successfully.");
        }

        var loginRecord = await _context.LoginActivityLogs
            .Where(x => x.userId == userId && x.LogoutTime == null)
            .OrderByDescending(x => x.LoginTime)
            .FirstOrDefaultAsync();

        if (loginRecord != null)
        {
            loginRecord.LogoutTime = DateTime.UtcNow;
            await _context.SaveChangesAsync();
        }

        await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
        HttpContext.Session.Clear();
        Response.Cookies.Delete("jwtToken"); 

        return RedirectToAction("Login", "Account");
    }

    [Authorize(Roles = "Admin")]
    public IActionResult AdminDashboard()
    {
        return View();
    }

    [Authorize(Roles = "Manager")]
    public IActionResult ManagerDashboard()
    {
        return View();
    }

    [Authorize(Roles = "Employee")]
    public IActionResult EmployeeDashboard()
    {
        return View();
    }
}
