using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using DineInSystem.Services;

/// <summary>
/// Account Controller - Handles user authentication, registration, and password management
/// Manages login, logout, registration, profile management, and password reset functionality
/// </summary>
public class AccountController : Controller
{
    private readonly AppDbContext _context;
    private readonly IEmailService _emailService;
    private readonly IConfiguration _configuration;

    public AccountController(AppDbContext context, IEmailService emailService, IConfiguration configuration)
    {
        _context = context;
        _emailService = emailService;
        _configuration = configuration;
    }

    /// <summary>
    /// Login (GET) - Display login form
    /// </summary>
    /// <param name="returnUrl">URL to redirect to after successful login</param>
    [HttpGet]
    public IActionResult Login(string returnUrl = null)
    {
        // Clear any stale TempData messages from other actions
        TempData.Remove("SuccessMessage");
        TempData.Remove("ErrorMessage");
        
        // Store return URL for after login
        if (!string.IsNullOrEmpty(returnUrl))
        {
            TempData["ReturnUrl"] = returnUrl;
        }
        return View();
    }

    /// <summary>
    /// Login (POST) - Process user login authentication
    /// Validates credentials and creates user session
    /// </summary>
    /// <param name="model">Login credentials (email and password)</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Find user by email
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            
            if (user == null)
            {
                TempData["ErrorMessage"] = "Invalid email or password.";
                return View(model);
            }

            // Check if user is active
            if (!user.IsActive)
            {
                TempData["ErrorMessage"] = "Your account has been deactivated. Please contact an administrator.";
                return View(model);
            }

            // Verify password
            if (!VerifyPassword(model.Password, user.PasswordHash))
            {
                TempData["ErrorMessage"] = "Invalid email or password.";
                return View(model);
            }

            // Set session variables
            HttpContext.Session.SetString("UserId", user.Id.ToString());
            HttpContext.Session.SetString("UserEmail", user.Email);
            HttpContext.Session.SetString("UserRole", user.Role);
            HttpContext.Session.SetString("UserName", user.Name);

            // Check for return URL
            var returnUrl = TempData["ReturnUrl"]?.ToString();
            if (!string.IsNullOrEmpty(returnUrl))
            {
                System.Diagnostics.Debug.WriteLine($"Redirecting to return URL: {returnUrl}");
                return Redirect(returnUrl);
            }
            
            // Redirect based on role
            if (user.Role == "Admin")
            {
                return RedirectToAction("Index", "Admin");
            }
            else
            {
                return RedirectToAction("Table", "Customer");
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred during login: {ex.Message}";
            return View(model);
        }
    }

    [HttpGet]
    /// <summary>
    /// Register (GET) - Display user registration form
    /// </summary>
    public IActionResult Register()
    {
        return View();
    }

    /// <summary>
    /// Register (POST) - Process new user registration
    /// Creates new user account with hashed password
    /// </summary>
    /// <param name="model">User registration information</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Check if email already exists
            var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            if (existingUser != null)
            {
                TempData["ErrorMessage"] = "An account with this email already exists.";
                return View(model);
            }

            // Create new user (default role will be set to Customer, can be changed manually in database)
            var user = new User
            {
                Name = model.FullName,
                Email = model.Email,
                PasswordHash = HashPassword(model.Password),
                Role = "Customer" // Default role, can be changed manually in database
            };

            _context.Users.Add(user);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Account created successfully! Please login with your credentials.";
            return RedirectToAction("Login");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred during registration: {ex.Message}";
            return View(model);
        }
    }


    [HttpGet]
    /// <summary>
    /// Logout - Clear user session and redirect to home page
    /// </summary>
    public IActionResult Logout()
    {
        // Clear session
        HttpContext.Session.Clear();
        
        return RedirectToAction("Index", "Home");
    }

    [HttpGet]
    /// <summary>
    /// Profile - Display user profile information
    /// Shows current user details and order history
    /// </summary>
    public async Task<IActionResult> Profile()
    {
        var userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login");
        }

        var user = await _context.Users.FindAsync(int.Parse(userId));
        if (user == null)
        {
            TempData["ErrorMessage"] = "User not found.";
            return RedirectToAction("Login");
        }

        var model = new UserProfileViewModel
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            CreatedAt = user.CreatedAt
        };

        return View(model);
    }

    /// <summary>
    /// Update Profile - Process user profile updates
    /// </summary>
    /// <param name="model">Updated profile information</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(UserProfileViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Profile", model);
        }

        try
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
            {
                TempData["ErrorMessage"] = "User not found.";
                return RedirectToAction("Profile");
            }

            // Check if email is being changed and if it already exists
            if (user.Email != model.Email)
            {
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && u.Id != model.UserId);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Email", "An account with this email already exists.");
                    return View("Profile", model);
                }
            }

            user.Name = model.Name;
            user.Email = model.Email;

            await _context.SaveChangesAsync();

            // Update session
            HttpContext.Session.SetString("UserName", user.Name);
            HttpContext.Session.SetString("UserEmail", user.Email);

            TempData["SuccessMessage"] = "Profile updated successfully!";
            return RedirectToAction("Profile");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"An error occurred while updating profile: {ex.Message}";
            return View("Profile", model);
        }
    }

    [HttpGet]
    /// <summary>
    /// Forgot Password (GET) - Display forgot password form
    /// </summary>
    public IActionResult ForgotPassword()
    {
        return View();
    }

    /// <summary>
    /// Forgot Password (POST) - Process password reset request
    /// Generates reset token and sends email with reset link
    /// </summary>
    /// <param name="model">Email address for password reset</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ForgotPassword(ForgotPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Find user by email
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email);
            
            if (user == null)
            {
                // Check if email format is valid first
                if (!IsValidEmail(model.Email))
                {
                    ModelState.AddModelError("Email", "Please enter a valid email address.");
                    return View(model);
                }
                
                // Don't reveal that the email doesn't exist for security
                TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
                return RedirectToAction("Login");
            }

            // Generate reset token
            var resetToken = GenerateSecureToken();
            var expiryHours = _configuration.GetValue<int>("ApplicationSettings:PasswordResetExpiryHours", 24);
            
            // Create password reset token record
            var passwordResetToken = new PasswordResetToken
            {
                Token = resetToken,
                UserId = user.Id,
                ExpiresAt = DateTime.Now.AddHours(expiryHours),
                IsUsed = false
            };

            // Remove any existing unused tokens for this user
            var existingTokens = await _context.PasswordResetTokens
                .Where(t => t.UserId == user.Id && !t.IsUsed)
                .ToListAsync();
            _context.PasswordResetTokens.RemoveRange(existingTokens);

            // Add new token
            _context.PasswordResetTokens.Add(passwordResetToken);
            await _context.SaveChangesAsync();

            // Send email
            var currentHost = $"{Request.Scheme}://{Request.Host}";
            await _emailService.SendPasswordResetEmailAsync(user.Email, resetToken, user.Name, currentHost);

            TempData["SuccessMessage"] = "If an account with that email exists, a password reset link has been sent.";
            return RedirectToAction("Login");
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred while processing your request. Please try again later.";
            return View(model);
        }
    }

    [HttpGet]
    /// <summary>
    /// Reset Password (GET) - Display password reset form
    /// </summary>
    /// <param name="token">Password reset token</param>
    /// <param name="email">User email address</param>
    public async Task<IActionResult> ResetPassword(string token, string email)
    {
        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(email))
        {
            TempData["ErrorMessage"] = "Invalid reset link.";
            return RedirectToAction("Login");
        }

        try
        {
            // Find the reset token
            var resetToken = await _context.PasswordResetTokens
                .Include(prt => prt.User)
                .FirstOrDefaultAsync(prt => prt.Token == token && prt.User.Email == email && !prt.IsUsed);

            if (resetToken == null || resetToken.ExpiresAt < DateTime.Now)
            {
                TempData["ErrorMessage"] = "Invalid or expired reset link. Please request a new password reset.";
                return RedirectToAction("Login");
            }

            var model = new ResetPasswordViewModel
            {
                Token = token,
                Email = email
            };

            return View(model);
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred while processing your request.";
            return RedirectToAction("Login");
        }
    }

    /// <summary>
    /// Reset Password (POST) - Process password reset with new password
    /// Validates token and updates user password
    /// </summary>
    /// <param name="model">New password information</param>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetPassword(ResetPasswordViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            // Find the reset token
            var resetToken = await _context.PasswordResetTokens
                .Include(prt => prt.User)
                .FirstOrDefaultAsync(prt => prt.Token == model.Token && prt.User.Email == model.Email && !prt.IsUsed);

            if (resetToken == null || resetToken.ExpiresAt < DateTime.Now)
            {
                TempData["ErrorMessage"] = "Invalid or expired reset link. Please request a new password reset.";
                return RedirectToAction("Login");
            }

            // Update user password
            var user = resetToken.User;
            user.PasswordHash = HashPassword(model.Password);

            // Mark token as used
            resetToken.IsUsed = true;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Your password has been reset successfully. Please log in with your new password.";
            return RedirectToAction("Login");
        }
        catch (Exception)
        {
            TempData["ErrorMessage"] = "An error occurred while resetting your password. Please try again.";
            return View(model);
        }
    }

    // Helper method to generate secure token
    private string GenerateSecureToken()
    {
        using var rng = RandomNumberGenerator.Create();
        var tokenBytes = new byte[32];
        rng.GetBytes(tokenBytes);
        return Convert.ToBase64String(tokenBytes).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    // Helper method to validate email format
    private bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }


    // Helper method to hash passwords
    private string HashPassword(string password)
    {
        using (var sha256 = SHA256.Create())
        {
            var hashedBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(password));
            return Convert.ToBase64String(hashedBytes);
        }
    }

    // Helper method to verify passwords
    private bool VerifyPassword(string password, string hashedPassword)
    {
        var hashedInput = HashPassword(password);
        return hashedInput == hashedPassword;
    }

    // Helper method to check if user is logged in
    public static bool IsUserLoggedIn(HttpContext context)
    {
        return !string.IsNullOrEmpty(context.Session.GetString("UserId"));
    }

    // Helper method to get current user role
    public static string GetCurrentUserRole(HttpContext context)
    {
        return context.Session.GetString("UserRole") ?? "Guest";
    }

    // Helper method to get current user ID
    public static int? GetCurrentUserId(HttpContext context)
    {
        var UserIdString = context.Session.GetString("UserId");
        return int.TryParse(UserIdString, out int UserId) ? UserId : null;
    }

}