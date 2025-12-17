using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.AspNetCore.Http;

// Core Tables with Clear Names

// Users table - stores all users (customers, admins, guests)
[Table("Users")]
public class User
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Required]
    [StringLength(100)]
    public string Email { get; set; } = string.Empty;

    [Required]
    public string PasswordHash { get; set; } = string.Empty;

    [Required]
    [StringLength(20)]
    public string Role { get; set; } = string.Empty; // Admin, Customer, Guest

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public bool IsActive { get; set; } = true;

    // Navigation properties
    public List<Order> Orders { get; set; } = new();
    public List<PasswordResetToken> PasswordResetTokens { get; set; } = new();
}

// PasswordResetTokens table - for password reset functionality
[Table("PasswordResetTokens")]
public class PasswordResetToken
{
    [Key]
    public int Id { get; set; }

    [Required]
    [StringLength(255)]
    public string Token { get; set; } = string.Empty;

    [ForeignKey("User")]
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; } = false;
}

// Tables table - restaurant dining tables
[Table("Tables")]
public class Table
{
    [Key]
    public int Id { get; set; }
    
    [Required]
    public int Number { get; set; }
    
    public bool IsOccupied { get; set; }
    public int Pax { get; set; } // Number of people
    public string? CurrentSessionId { get; set; } // Session ID of current user
    public DateTime? OccupiedAt { get; set; } // When table was occupied

    // Navigation
    public List<Order> Orders { get; set; } = new();
    public List<Cart> Carts { get; set; } = new();
}

// Menu table - food items
[Table("Menu")]
public class MenuItem
{
    [Key]
    [StringLength(10)]
    public string Id { get; set; } = string.Empty; // M001, B001, etc.

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    public string? Description { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; }

    [Required]
    public int CategoryId { get; set; }
    
    // Navigation property
    public virtual Category Category { get; set; } = null!;

    public string? ImagePath { get; set; } = string.Empty;

    public bool IsAvailable { get; set; } = true;
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime? UpdatedAt { get; set; }

    // Not stored in DB, used only for uploads
    [NotMapped]
    public IFormFile? ImageFile { get; set; }

    // Navigation properties
    public List<Addon> Addons { get; set; } = new();
    public List<OrderItem> OrderItems { get; set; } = new();
    public List<CartItem> CartItems { get; set; } = new();
}

// Addons table - menu item customizations
[Table("Addons")]
public class Addon
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("MenuItem")]
    [Required]
    [StringLength(10)]
    public string MenuItemId { get; set; } = string.Empty;
    
    [NotMapped]
    public MenuItem MenuItem { get; set; } = null!;

    [Required]
    [StringLength(100)]
    public string Name { get; set; } = string.Empty;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Price { get; set; } = 0;

    public bool IsRequired { get; set; } = false;
    public string Type { get; set; } = "Optional"; // Optional, Required, Choice
    
    [StringLength(500)]
    public string? ConflictingAddons { get; set; } // Comma-separated list of conflicting addon IDs
}

// Orders table - completed orders
[Table("Orders")]
public class Order
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Table")]
    public int? TableId { get; set; }
    public Table? Table { get; set; }

    [ForeignKey("User")]
    public int? UserId { get; set; } // Nullable for guest orders
    public User? User { get; set; }

    public DateTime OrderDate { get; set; }
    
    [Required]
    [StringLength(20)]
    public string Type { get; set; } = string.Empty; // Dine-In, Takeaway, Delivery

    [Required]
    [StringLength(20)]
    public string Status { get; set; } = string.Empty; // Pending, Completed, Cancelled

    [Column(TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    // Navigation properties
    public List<OrderItem> Items { get; set; } = new();
    public List<Payment> Payments { get; set; } = new();
}

// OrderItems table - items within an order
[Table("OrderItems")]
public class OrderItem
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Order")]
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [ForeignKey("MenuItem")]
    [Required]
    [StringLength(10)]
    public string MenuItemId { get; set; } = string.Empty;
    public MenuItem MenuItem { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal Subtotal { get; set; }

    public string? SpecialInstructions { get; set; } // JSON string for addons

}


// Cart table - shopping cart for each table
[Table("Cart")]
public class Cart
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Table")]
    public int TableId { get; set; }
    public Table Table { get; set; } = null!;

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    // Navigation properties
    public List<CartItem> Items { get; set; } = new();
}

// CartItems table - items in shopping cart
[Table("CartItems")]
public class CartItem
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Cart")]
    public int CartId { get; set; }
    public Cart Cart { get; set; } = null!;

    [ForeignKey("MenuItem")]
    [Required]
    [StringLength(10)]
    public string MenuItemId { get; set; } = string.Empty;
    public MenuItem MenuItem { get; set; } = null!;

    public int Quantity { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; } = 0;

    public string? SpecialInstructions { get; set; } // JSON string for addons
}

// Payments table - payment records
[Table("Payments")]
public class Payment
{
    [Key]
    public int Id { get; set; }

    [ForeignKey("Order")]
    public int OrderId { get; set; }
    public Order Order { get; set; } = null!;

    [Column(TypeName = "decimal(18,2)")]
    public decimal Amount { get; set; }

    [Required]
    [StringLength(20)]
    public string Method { get; set; } = string.Empty; // Cash, Credit Card, etc.

    public DateTime PaymentDate { get; set; }
}

// ViewModels (not stored in database)

public class AddOrderViewModel
{
    public int TableId { get; set; }
    public List<MenuItem> MenuItems { get; set; } = new();
    public string? SearchTerm { get; set; }
    public string? SelectedCategory { get; set; }
}

public class LoginViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;
}

public class RegisterViewModel
{
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
    public string FullName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Password and confirmation password do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class UserProfileViewModel
{
    public int UserId { get; set; }
    
    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    [DataType(DataType.Password)]
    public string? CurrentPassword { get; set; }

    [DataType(DataType.Password)]
    public string? NewPassword { get; set; }

    [DataType(DataType.Password)]
    [Compare("NewPassword", ErrorMessage = "Password and confirmation password do not match")]
    public string? ConfirmNewPassword { get; set; }

    public DateTime CreatedAt { get; set; }
    public int TotalOrders { get; set; }
    public decimal TotalSpent { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;
}

public class ResetPasswordViewModel
{
    [Required]
    public string Token { get; set; } = string.Empty;
    
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Password is required")]
    [StringLength(100, MinimumLength = 6, ErrorMessage = "Password must be at least 6 characters long")]
    [DataType(DataType.Password)]
    public string Password { get; set; } = string.Empty;

    [Required(ErrorMessage = "Please confirm your password")]
    [DataType(DataType.Password)]
    [Compare("Password", ErrorMessage = "Password and confirmation password do not match")]
    public string ConfirmPassword { get; set; } = string.Empty;
}

public class EditUserViewModel
{
    public int UserId { get; set; }

    [Required(ErrorMessage = "Full name is required")]
    [StringLength(100, ErrorMessage = "Full name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;

    [Required(ErrorMessage = "Email is required")]
    [EmailAddress(ErrorMessage = "Please enter a valid email address")]
    public string Email { get; set; } = string.Empty;

    [Required(ErrorMessage = "Role is required")]
    public string Role { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
}

public class CheckoutViewModel
{
    public int TableId { get; set; }
    public Cart? Cart { get; set; }
    public decimal Subtotal { get; set; }
    public decimal TaxRate { get; set; } = 0.06m;
    public decimal Tax { get; set; }
    public decimal Total { get; set; }

    [Required(ErrorMessage = "Please select a payment method")]
    public string PaymentMethod { get; set; } = string.Empty;

    // Credit card fields - only required when PaymentMethod is Credit Card or Debit Card
    [RequiredIfCardPayment(nameof(PaymentMethod), ErrorMessage = "Cardholder name is required for card payments")]
    [StringLength(100, ErrorMessage = "Cardholder name cannot exceed 100 characters")]
    public string? CardholderName { get; set; }

    [RequiredIfCardPayment(nameof(PaymentMethod), ErrorMessage = "Card number is required for card payments")]
    [RegularExpression(@"^\d{4}\s\d{4}\s\d{4}\s\d{4}$", ErrorMessage = "Please enter a valid 16-digit card number")]
    public string? CardNumber { get; set; }

    [RequiredIfCardPayment(nameof(PaymentMethod), ErrorMessage = "Expiry date is required for card payments")]
    [RegularExpression(@"^(0[1-9]|1[0-2])\/\d{2}$", ErrorMessage = "Please enter expiry date in MM/YY format (01-12/YY)")]
    [ValidExpiryDate(ErrorMessage = "Card has expired. Please enter a valid expiry date.")]
    public string? ExpiryDate { get; set; }

    [RequiredIfCardPayment(nameof(PaymentMethod), ErrorMessage = "CVV is required for card payments")]
    [RegularExpression(@"^\d{3}$", ErrorMessage = "CVV must be exactly 3 digits")]
    public string? CVV { get; set; }

    // Helper properties for display
    public string TableNumber => Cart?.Table?.Number.ToString() ?? "Unknown";
    public List<CartItem> CartItems => Cart?.Items ?? new List<CartItem>();
}

public class PaymentResult
{
    public bool Success { get; set; }
    public string? TransactionId { get; set; }
    public string? ErrorMessage { get; set; }
}

public class CartViewModel
{
    public int TableId { get; set; }
    public List<CartItem> Items { get; set; } = new();
    public decimal Subtotal { get; set; }
    public decimal Tax { get; set; }
    public decimal Total { get; set; }
}

// Custom validation attribute for card expiry date
public class ValidExpiryDateAttribute : ValidationAttribute
{
    public override bool IsValid(object? value)
    {
        if (value == null || string.IsNullOrEmpty(value.ToString()))
            return true; // Let Required attribute handle null/empty values

        var expiryDate = value.ToString();
        
        // Check if format matches MM/YY
        if (!System.Text.RegularExpressions.Regex.IsMatch(expiryDate ?? "", @"^(0[1-9]|1[0-2])\/\d{2}$"))
            return false;

        try
        {
            var parts = (expiryDate ?? "").Split('/');
            if (parts.Length != 2) return false;
            
            var month = int.Parse(parts[0]);
            var year = int.Parse(parts[1]) + 2000; // Convert YY to YYYY

            var expiryDateTime = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            var currentDate = DateTime.Now.Date;

            return expiryDateTime >= currentDate;
        }
        catch
        {
            return false;
        }
    }

    public override string FormatErrorMessage(string name)
    {
        return "Card has expired. Please enter a valid expiry date.";
    }
}

// Custom validation attribute for conditional required fields
public class RequiredIfCardPaymentAttribute : ValidationAttribute
{
    private readonly string _paymentMethodProperty;

    public RequiredIfCardPaymentAttribute(string paymentMethodProperty)
    {
        _paymentMethodProperty = paymentMethodProperty;
    }

    protected override ValidationResult? IsValid(object? value, ValidationContext validationContext)
    {
        var paymentMethodProperty = validationContext.ObjectType.GetProperty(_paymentMethodProperty);
        if (paymentMethodProperty == null)
            return new ValidationResult($"Unknown property: {_paymentMethodProperty}");

        var paymentMethodValue = paymentMethodProperty.GetValue(validationContext.ObjectInstance)?.ToString();
        
        // Only validate if payment method is Credit Card or Debit Card
        if (paymentMethodValue == "Credit Card" || paymentMethodValue == "Debit Card")
        {
            if (value == null || string.IsNullOrWhiteSpace(value.ToString()))
            {
                return new ValidationResult(ErrorMessage ?? "This field is required for card payments.");
            }
        }

        return ValidationResult.Success;
    }
}

public class AdminDashboardViewModel
{
    public int TotalTables { get; set; }
    public int TotalMenuItems { get; set; }
    public int TotalUsers { get; set; }
    public int TotalOrders { get; set; }
    public List<Table> Tables { get; set; } = new();
    public List<Order> RecentOrders { get; set; } = new();
    public List<User> RecentUsers { get; set; } = new();
}

public class TableDetailsViewModel
{
    public Table Table { get; set; } = null!;
    public Order? CurrentOrder { get; set; }
    public List<Order> OrderHistory { get; set; } = new();
}

public class GroupedPendingOrderViewModel
{
    public int TableId { get; set; }
    public string? TableNumber { get; set; }
    public string? SessionId { get; set; }
    public DateTime FirstOrderDate { get; set; }
    public DateTime LastOrderDate { get; set; }
    public List<OrderItem> CombinedItems { get; set; } = new();
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = string.Empty;
    public List<int> OriginalOrderIds { get; set; } = new();
    public User? User { get; set; }
}

[Table("Categories")]
public class Category
{
    public int Id { get; set; }
    
    [Required(ErrorMessage = "Category name is required")]
    [StringLength(100, ErrorMessage = "Category name cannot exceed 100 characters")]
    public string Name { get; set; } = string.Empty;
    
    [Required(ErrorMessage = "Prefix is required")]
    [StringLength(10, ErrorMessage = "Prefix cannot exceed 10 characters")]
    public string Prefix { get; set; } = string.Empty;
    
    [StringLength(500, ErrorMessage = "Description cannot exceed 500 characters")]
    public string? Description { get; set; }
    
    public bool IsActive { get; set; } = true;
    
    [Range(0, 999, ErrorMessage = "Display order must be between 0 and 999")]
    public int DisplayOrder { get; set; } = 0;
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    
    public DateTime? UpdatedAt { get; set; }
    
    // Navigation properties
    public virtual ICollection<MenuItem> MenuItems { get; set; } = new List<MenuItem>();
}

public class CategoryViewModel
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Prefix { get; set; } = string.Empty;
    public string? Description { get; set; }
    public bool IsActive { get; set; }
    public int DisplayOrder { get; set; }
    public int MenuItemCount { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}

