using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

/// <summary>
/// Admin Controller - Handles all administrative operations for the Dine-In System
/// Manages tables, menu items, users, orders, categories, and addons
/// </summary>
[RequireAdmin]
public class AdminController : Controller
{
    private readonly AppDbContext _context;
    private readonly IWebHostEnvironment _hostEnvironment;
    private readonly ILogger<AdminController> _logger;

    public AdminController(AppDbContext context, IWebHostEnvironment hostEnvironment, ILogger<AdminController> logger)
    {
        _context = context;
        _hostEnvironment = hostEnvironment;
        _logger = logger;
    }

    /// <summary>
    /// Admin Dashboard - Main landing page showing system overview and statistics
    /// Displays total counts, recent orders, recent users, and table status
    /// </summary>
    public IActionResult Index()
    {
        var tables = _context.Tables.ToList();
        var model = new AdminDashboardViewModel
        {
            TotalTables = tables.Count,
            TotalMenuItems = _context.Menu.Count(),
            TotalUsers = _context.Users.Count(),
            TotalOrders = _context.Orders.Count(),
            Tables = tables,
            RecentOrders = _context.Orders
                .Include(o => o.Items)
                .Include(o => o.User)
                .OrderByDescending(o => o.OrderDate)
                .Take(10)
                .ToList(),
            RecentUsers = _context.Users
                .OrderByDescending(u => u.CreatedAt)
                .Take(10)
                .ToList()
        };
        return View(model);
    }

    /// <summary>
    /// Table Details - View detailed information about a specific table
    /// Shows table status, current orders, order history, and can set table as occupied
    /// </summary>
    /// <param name="tableId">ID of the table to view</param>
    /// <param name="pax">Number of people (optional - sets table as occupied if provided)</param>
    public IActionResult TableDetails(int tableId, int? pax = null)
    {
        var table = _context.Tables
            .Include(t => t.Orders)
                .ThenInclude(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
            .FirstOrDefault(t => t.Id == tableId);

        if (table == null)
            return NotFound();

        // If pax is provided and table is not occupied, set the table as occupied
        if (pax.HasValue && !table.IsOccupied)
        {
            table.Pax = pax.Value;
            table.IsOccupied = true;
            table.OccupiedAt = DateTime.Now;
            _context.SaveChanges();
        }

        var model = new TableDetailsViewModel
        {
            Table = table,
            CurrentOrder = table.Orders?.FirstOrDefault(o => o.Status == "Pending"),
            OrderHistory = table.Orders?.OrderByDescending(o => o.OrderDate).ToList() ?? new List<Order>()
        };

        return View(model);
    }

    /// <summary>
    /// Clear Table - Reset table to unoccupied state and remove all associated data
    /// Removes all carts, orders, and order items associated with the table
    /// </summary>
    /// <param name="tableId">ID of the table to clear</param>
    [HttpPost]
    public IActionResult ClearTable(int tableId)
    {
        try
        {
            var table = _context.Tables
                .Include(t => t.Orders)
                    .ThenInclude(o => o.Items)
                .Include(t => t.Carts)
                    .ThenInclude(c => c.Items)
                .FirstOrDefault(t => t.Id == tableId);

            if (table == null)
            {
                return Json(new { success = false, message = "Table not found." });
            }

            // Remove all cart items and carts first
            foreach (var cart in table.Carts.ToList())
            {
                _context.CartItems.RemoveRange(cart.Items);
                _context.Cart.Remove(cart);
            }

            // Remove all order items and orders
            foreach (var order in table.Orders.ToList())
            {
                _context.OrderItems.RemoveRange(order.Items);
                _context.Orders.Remove(order);
            }

            // Reset pax and occupancy
            table.Pax = 0;
            table.IsOccupied = false;
            _context.SaveChanges();

            return Json(new { success = true, message = "Table cleared successfully!" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error clearing table: {ex.Message}" });
        }
    }

    /// <summary>
    /// Add Order - Display menu items for creating a new order for a specific table
    /// Shows categorized menu items with filtering capabilities
    /// </summary>
    /// <param name="tableId">ID of the table to create order for</param>
    public async Task<IActionResult> AddOrder(int tableId)
    {
        var vm = new AddOrderViewModel
        {
            TableId = tableId,
            MenuItems = await _context.Menu
                .Include(m => m.Category)
                .ToListAsync()
        };
        
        // Get categories for filter tabs
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        ViewBag.Categories = categories;
        
        return View(vm); // Views/Admin/AddOrder.cshtml
    }

    /// <summary>
    /// Add Order Item - Add a menu item to an existing order for a table
    /// Creates order if none exists, or adds item to existing pending order
    /// </summary>
    /// <param name="TableId">ID of the table</param>
    /// <param name="MenuItemId">ID of the menu item to add</param>
    /// <param name="quantity">Quantity of the item</param>
    [HttpPost]
    public IActionResult AddOrderItem(int TableId, string MenuItemId, int quantity)
    {
        try
        {
            var table = _context.Tables
                .Include(t => t.Orders)
                .ThenInclude(o => o.Items)
                .FirstOrDefault(t => t.Id == TableId);

            if (table == null)
            {
                TempData["ErrorMessage"] = "Table not found.";
                return RedirectToAction("Index");
            }

            // Find or create active order
            var order = table.Orders.FirstOrDefault(o => o.Status == "Pending");
            if (order == null)
            {
                order = new Order
                {
                    TableId = TableId,
                    Status = "Pending",
                    Type = "Dine-In",
                    OrderDate = DateTime.Now,
                    Items = new List<OrderItem>()
                };
                _context.Orders.Add(order);
                _context.SaveChanges(); // Save to get OrderId
            }

            // Add menu item to order
            var menuItem = _context.Menu.FirstOrDefault(m => m.Id == MenuItemId);
            if (menuItem == null)
            {
                TempData["ErrorMessage"] = "Menu item not found.";
                return RedirectToAction("AddOrder", new { tableId = TableId });
            }

            var existingItem = order.Items.FirstOrDefault(oi => oi.MenuItemId == MenuItemId);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
                existingItem.Subtotal = existingItem.Quantity * menuItem.Price;
            }
            else
            {
                order.Items.Add(new OrderItem
                {
                    OrderId = order.Id,
                    MenuItemId = MenuItemId,
                    Quantity = quantity,
                    Subtotal = menuItem.Price * quantity
                });
            }

            // Recalculate order total (subtotal + 6% tax)
            var subtotal = order.Items.Sum(oi => oi.Subtotal);
            var tax = subtotal * 0.06m;
            order.TotalAmount = subtotal + tax;

            _context.SaveChanges();

            TempData["SuccessMessage"] = $"{menuItem.Name} added to order successfully!";
            return RedirectToAction("TableDetails", new { tableId = TableId });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error adding item to order: {ex.Message}";
            return RedirectToAction("AddOrder", new { tableId = TableId });
        }
    }


    [HttpPost]
    /// <summary>
    /// Remove Order Item - Remove an item from an order via AJAX
    /// </summary>
    /// <param name="id">ID of the order item to remove</param>
    public IActionResult RemoveOrderItemAjax(int id)
    {
        var orderItem = _context.OrderItems
            .Include(oi => oi.Order)
            .FirstOrDefault(oi => oi.Id == id);

        if (orderItem == null)
        {
            return Json(new { success = false, message = "Item not found" });
        }

        var order = orderItem.Order;
        _context.OrderItems.Remove(orderItem);
        _context.SaveChanges();

        // Recalculate order total safely (subtotal + 6% tax)
        var subtotal = _context.OrderItems
            .Where(oi => oi.OrderId == order.Id)
            .Sum(oi => (decimal?)oi.Subtotal) ?? 0;
        
        var tax = subtotal * 0.06m;
        order.TotalAmount = subtotal + tax;

        _context.SaveChanges();

        var grandTotal = order.TotalAmount;

        return Json(new
        {
            success = true,
            subtotal = subtotal.ToString("0.00"),
            tax = tax.ToString("0.00"),
            total = grandTotal.ToString("0.00")
        });
    }

    /// <summary>
    /// Users Management - Display all users in the system
    /// Shows user list with pagination and search capabilities
    /// </summary>
    public async Task<IActionResult> Users()
    {
        var users = await _context.Users
            .Include(u => u.Orders)
            .OrderBy(u => u.Name)
            .ToListAsync();

        return View(users);
    }

    /// <summary>
    /// User Details - View detailed information about a specific user
    /// Shows user profile, order history, and account status
    /// </summary>
    /// <param name="id">ID of the user to view</param>
    public async Task<IActionResult> UserDetails(int id)
    {
        var user = await _context.Users
            .Include(u => u.Orders)
                .ThenInclude(o => o.Items)
            .ThenInclude(oi => oi.MenuItem)
            .Include(u => u.Orders)
            .ThenInclude(o => o.Table)
            .FirstOrDefaultAsync(u => u.Id == id);

        if (user == null)
        {
            return NotFound();
        }

        return View(user);
    }

    /// <summary>
    /// Order Details - View detailed information about a specific order
    /// Shows order items, payment status, and order timeline
    /// </summary>
    /// <param name="orderId">ID of the order to view</param>
    public async Task<IActionResult> OrderDetails(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.User)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    /// <summary>
    /// Print Order - Generate a printable version of an order
    /// Creates a print-friendly layout for kitchen or customer receipt
    /// </summary>
    /// <param name="orderId">ID of the order to print</param>
    public async Task<IActionResult> PrintOrder(int orderId)
    {
        var order = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.User)
            .Include(o => o.Payments)
            .FirstOrDefaultAsync(o => o.Id == orderId);

        if (order == null)
        {
            return NotFound();
        }

        return View(order);
    }

    [HttpGet]
    /// <summary>
    /// Edit User (GET) - Display form to edit user information
    /// </summary>
    /// <param name="id">ID of the user to edit</param>
    public async Task<IActionResult> EditUser(int id)
    {
        var user = await _context.Users.FindAsync(id);
        if (user == null)
        {
            return NotFound();
        }

        var editUserViewModel = new EditUserViewModel
        {
            UserId = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            IsActive = user.IsActive
        };

        return View(editUserViewModel);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Edit User (POST) - Process user information updates
    /// </summary>
    /// <param name="model">Updated user information</param>
    public async Task<IActionResult> EditUser(EditUserViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View(model);
        }

        try
        {
            var user = await _context.Users.FindAsync(model.UserId);
            if (user == null)
            {
                return NotFound();
            }

            // Check if email is being changed and if it's already taken
            if (user.Email != model.Email)
            {
                var existingUser = await _context.Users.FirstOrDefaultAsync(u => u.Email == model.Email && u.Id != model.UserId);
                if (existingUser != null)
                {
                    ModelState.AddModelError("Email", "This email is already in use.");
                    return View(model);
                }
            }

            // Update user information
            user.Name = model.Name;
            user.Email = model.Email;
            user.Role = model.Role;
            user.IsActive = model.IsActive;

            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "User updated successfully!";
            return RedirectToAction("UserDetails", new { id = user.Id });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error updating user: {ex.Message}";
            return View(model);
        }
    }

    [HttpPost]
    /// <summary>
    /// Toggle User Status - Enable/disable a user account
    /// </summary>
    /// <param name="id">ID of the user to toggle</param>
    public async Task<IActionResult> ToggleUserStatus(int id)
    {
        try
        {
            var user = await _context.Users.FindAsync(id);
            if (user == null)
            {
                return Json(new { success = false, message = "User not found" });
            }

            user.IsActive = !user.IsActive;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                isActive = user.IsActive,
                message = user.IsActive ? "User activated successfully" : "User deactivated successfully"
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }



    [HttpPost]
    /// <summary>
    /// Delete Addon - Remove an addon from the system
    /// </summary>
    /// <param name="AddonId">ID of the addon to delete</param>
    public async Task<IActionResult> DeleteAddon(int AddonId)
    {
        try
        {
            var addon = await _context.Addons.FindAsync(AddonId);
            if (addon == null)
            {
                return Json(new { success = false, message = "Addon not found" });
            }

            // Clean up bidirectional relationships before deleting
            if (!string.IsNullOrEmpty(addon.ConflictingAddons))
            {
                await RemoveBidirectionalConflicts(AddonId, addon.ConflictingAddons);
            }

            _context.Addons.Remove(addon);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Addon deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditAddon(Addon addon)
    {
        try
        {
            var existingAddon = await _context.Addons.FindAsync(addon.Id);
            if (existingAddon == null)
            {
                return Json(new { success = false, message = "Addon not found" });
            }

            // Store old conflicting addons to clean up relationships
            var oldConflictingAddons = existingAddon.ConflictingAddons;

            // Update the addon properties
            existingAddon.Name = addon.Name;
            existingAddon.Price = addon.Price;
            existingAddon.Type = addon.Type;
            existingAddon.IsRequired = addon.IsRequired;
            existingAddon.ConflictingAddons = addon.ConflictingAddons;

            await _context.SaveChangesAsync();

            // Update bidirectional conflicting relationships
            await UpdateBidirectionalConflicts(existingAddon.Id, oldConflictingAddons, addon.ConflictingAddons);

            return Json(new { success = true, message = "Addon updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    private async Task CreateBidirectionalConflicts(int addonId, string conflictingAddonsString)
    {
        if (string.IsNullOrEmpty(conflictingAddonsString))
            return;

        var conflictingIds = conflictingAddonsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => int.TryParse(id.Trim(), out int result) ? result : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id.Value)
            .ToList();

        foreach (var conflictId in conflictingIds)
        {
            var conflictingAddon = await _context.Addons.FindAsync(conflictId);
            if (conflictingAddon != null)
            {
                // Add this addon to the conflicting addon's conflicts list
                var existingConflicts = conflictingAddon.ConflictingAddons ?? "";
                var conflictsList = string.IsNullOrEmpty(existingConflicts) 
                    ? new List<string>() 
                    : existingConflicts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();

                if (!conflictsList.Contains(addonId.ToString()))
                {
                    conflictsList.Add(addonId.ToString());
                    conflictingAddon.ConflictingAddons = string.Join(",", conflictsList);
                }
            }
        }

        await _context.SaveChangesAsync();
    }

    private async Task UpdateBidirectionalConflicts(int addonId, string oldConflictingAddons, string newConflictingAddons)
    {
        // Remove old bidirectional relationships
        if (!string.IsNullOrEmpty(oldConflictingAddons))
        {
            var oldConflictingIds = oldConflictingAddons.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id.Trim(), out int result) ? result : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToList();

            foreach (var conflictId in oldConflictingIds)
            {
                var conflictingAddon = await _context.Addons.FindAsync(conflictId);
                if (conflictingAddon != null)
                {
                    var existingConflicts = conflictingAddon.ConflictingAddons ?? "";
                    var conflictsList = string.IsNullOrEmpty(existingConflicts) 
                        ? new List<string>() 
                        : existingConflicts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();

                    conflictsList.Remove(addonId.ToString());
                    conflictingAddon.ConflictingAddons = conflictsList.Any() ? string.Join(",", conflictsList) : null;
                }
            }
        }

        // Create new bidirectional relationships
        if (!string.IsNullOrEmpty(newConflictingAddons))
        {
            await CreateBidirectionalConflicts(addonId, newConflictingAddons);
        }

        await _context.SaveChangesAsync();
    }

    private async Task RemoveBidirectionalConflicts(int addonId, string conflictingAddonsString)
    {
        if (string.IsNullOrEmpty(conflictingAddonsString))
            return;

        var conflictingIds = conflictingAddonsString.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(id => int.TryParse(id.Trim(), out int result) ? result : (int?)null)
            .Where(id => id.HasValue)
            .Select(id => id.Value)
            .ToList();

        foreach (var conflictId in conflictingIds)
        {
            var conflictingAddon = await _context.Addons.FindAsync(conflictId);
            if (conflictingAddon != null)
            {
                var existingConflicts = conflictingAddon.ConflictingAddons ?? "";
                var conflictsList = string.IsNullOrEmpty(existingConflicts) 
                    ? new List<string>() 
                    : existingConflicts.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(c => c.Trim()).ToList();

                conflictsList.Remove(addonId.ToString());
                conflictingAddon.ConflictingAddons = conflictsList.Any() ? string.Join(",", conflictsList) : null;
            }
        }

        await _context.SaveChangesAsync();
    }

    /// <summary>
    /// Menu Management - Display all menu items with category grouping
    /// Shows available and unavailable items with management options
    /// </summary>
    public async Task<IActionResult> Menu()
    {
        var Menu = await _context.Menu
            .Include(m => m.Addons)
            .Include(m => m.Category)
            .OrderBy(m => m.Category.DisplayOrder)
            .ThenBy(m => m.Category.Name)
            .ThenBy(m => m.IsAvailable ? 0 : 1) // Available items first
            .ThenBy(m => m.Name)
            .ToListAsync();
        
        // Get categories for the filter dropdown
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        
        ViewBag.Categories = categories;
        return View(Menu);
    }

    /// <summary>
    /// Transaction History - View all completed orders and payments
    /// Shows financial transaction records with filtering options
    /// </summary>
    public async Task<IActionResult> TransactionHistory()
    {
        var orders = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .Include(o => o.User)
            .OrderByDescending(o => o.OrderDate)
            .ToListAsync();

        return View(orders);
    }

    // Category Management Actions
    [HttpGet]
    /// <summary>
    /// Categories Management - Display all menu categories
    /// Shows category list with status and management options
    /// </summary>
    public async Task<IActionResult> Categories()
    {
        var categories = await _context.Categories
            .Include(c => c.MenuItems)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();

        var viewModel = categories.Select(c => new CategoryViewModel
        {
            Id = c.Id,
            Name = c.Name,
            Prefix = c.Prefix,
            Description = c.Description,
            IsActive = c.IsActive,
            DisplayOrder = c.DisplayOrder,
            MenuItemCount = c.MenuItems.Count,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt
        }).ToList();

        return View(viewModel);
    }

    [HttpGet]
    /// <summary>
    /// Create Category (GET) - Display form to create a new menu category
    /// </summary>
    public async Task<IActionResult> CreateCategory()
    {
        // Get the next available display order
        var hasCategories = await _context.Categories.AnyAsync(c => c.IsActive);
        var maxOrder = 0;
        
        if (hasCategories)
        {
            maxOrder = await _context.Categories
                .Where(c => c.IsActive)
                .MaxAsync(c => c.DisplayOrder);
        }
        
        var category = new Category
        {
            DisplayOrder = maxOrder + 1
        };
        
        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Create Category (POST) - Process new category creation
    /// </summary>
    /// <param name="category">New category information</param>
    public async Task<IActionResult> CreateCategory(Category category)
    {
        try
        {
            if (ModelState.IsValid)
            {
                // Check if prefix already exists
                var existingCategoryByPrefix = await _context.Categories
                    .FirstOrDefaultAsync(c => c.Prefix == category.Prefix);
                
                if (existingCategoryByPrefix != null)
                {
                    ModelState.AddModelError("Prefix", "A category with this prefix already exists.");
                    return View(category);
                }

                // Check if display order already exists
                var existingCategoryByOrder = await _context.Categories
                    .FirstOrDefaultAsync(c => c.DisplayOrder == category.DisplayOrder);
                
                if (existingCategoryByOrder != null)
                {
                    ModelState.AddModelError("DisplayOrder", $"Display order {category.DisplayOrder} is already used by category '{existingCategoryByOrder.Name}'. Please choose a different order number.");
                    return View(category);
                }

                category.CreatedAt = DateTime.UtcNow;
                _context.Categories.Add(category);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Category '{category.Name}' created successfully!";
                return RedirectToAction("Categories");
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error creating category: {ex.Message}";
        }

        return View(category);
    }

    [HttpGet]
    public async Task<IActionResult> EditCategory(int id)
    {
        var category = await _context.Categories.FindAsync(id);
        if (category == null)
        {
            return NotFound();
        }

        return View(category);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditCategory(Category category)
    {
        try
        {
            if (ModelState.IsValid)
            {
                // Check if prefix already exists (excluding current category)
                var existingCategoryByPrefix = await _context.Categories
                    .FirstOrDefaultAsync(c => c.Prefix == category.Prefix && c.Id != category.Id);
                
                if (existingCategoryByPrefix != null)
                {
                    ModelState.AddModelError("Prefix", "A category with this prefix already exists.");
                    return View(category);
                }

                // Check if display order already exists (excluding current category)
                var existingCategoryByOrder = await _context.Categories
                    .FirstOrDefaultAsync(c => c.DisplayOrder == category.DisplayOrder && c.Id != category.Id);
                
                if (existingCategoryByOrder != null)
                {
                    ModelState.AddModelError("DisplayOrder", $"Display order {category.DisplayOrder} is already used by category '{existingCategoryByOrder.Name}'. Please choose a different order number.");
                    return View(category);
                }

                category.UpdatedAt = DateTime.UtcNow;
                _context.Categories.Update(category);
                await _context.SaveChangesAsync();

                TempData["SuccessMessage"] = $"Category '{category.Name}' updated successfully!";
                return RedirectToAction("Categories");
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error updating category: {ex.Message}";
        }

        return View(category);
    }

    [HttpPost]
    public async Task<IActionResult> DeleteCategory(int id)
    {
        try
        {
            var category = await _context.Categories
                .Include(c => c.MenuItems)
                .FirstOrDefaultAsync(c => c.Id == id);

            if (category == null)
            {
                return Json(new { success = false, message = "Category not found" });
            }

            // Check if category has menu items
            if (category.MenuItems.Any())
            {
                return Json(new { success = false, message = $"Cannot delete category '{category.Name}' because it has {category.MenuItems.Count} menu items. Please reassign or delete the menu items first." });
            }

            _context.Categories.Remove(category);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Category '{category.Name}' deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error deleting category: {ex.Message}" });
        }
    }

    [HttpPost]
    public async Task<IActionResult> ToggleCategoryStatus(int id)
    {
        try
        {
            var category = await _context.Categories.FindAsync(id);
            if (category == null)
            {
                return Json(new { success = false, message = "Category not found" });
            }

            category.IsActive = !category.IsActive;
            category.UpdatedAt = DateTime.UtcNow;
            
            await _context.SaveChangesAsync();

            var status = category.IsActive ? "activated" : "deactivated";
            return Json(new { success = true, message = $"Category '{category.Name}' {status} successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error updating category status: {ex.Message}" });
        }
    }

    [HttpGet]
    public async Task<IActionResult> PendingOrders()
    {
        var pendingOrders = await _context.Orders
            .Include(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .Include(o => o.User)
            .Where(o => o.Status == "Pending Payment")
            .OrderBy(o => o.OrderDate)
            .ToListAsync();

        // Group orders by table and session
        var groupedOrders = pendingOrders
            .GroupBy(o => new { o.TableId, SessionId = o.Table?.CurrentSessionId })
            .Select(group => new GroupedPendingOrderViewModel
            {
                TableId = group.Key.TableId ?? 0,
                TableNumber = group.First().Table?.Number.ToString(),
                SessionId = group.Key.SessionId,
                FirstOrderDate = group.Min(o => o.OrderDate),
                LastOrderDate = group.Max(o => o.OrderDate),
                CombinedItems = group.SelectMany(o => o.Items).ToList(),
                TotalAmount = group.Sum(o => o.TotalAmount),
                PaymentMethod = group.First().Payments.FirstOrDefault()?.Method ?? "Unknown",
                OriginalOrderIds = group.Select(o => o.Id).ToList(),
                User = group.First().User
            })
            .OrderBy(g => g.FirstOrderDate)
            .ToList();

        return View(groupedOrders);
    }

    [HttpPost]
    public async Task<IActionResult> MarkOrderAsPaid(string orderIds)
    {
        try
        {
            // Parse the comma-separated order IDs
            var orderIdList = orderIds.Split(',', StringSplitOptions.RemoveEmptyEntries)
                .Select(id => int.TryParse(id.Trim(), out int result) ? result : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id.Value)
                .ToList();

            if (!orderIdList.Any())
            {
                return Json(new { success = false, message = "No valid order IDs provided" });
            }

            var orders = await _context.Orders
                .Include(o => o.Payments)
                .Where(o => orderIdList.Contains(o.Id))
                .ToListAsync();

            if (orders.Count != orderIdList.Count)
            {
                return Json(new { success = false, message = "Some orders not found" });
            }

            // Verify all orders are in pending payment status
            var nonPendingOrders = orders.Where(o => o.Status != "Pending Payment").ToList();
            if (nonPendingOrders.Any())
            {
                return Json(new { success = false, message = $"Some orders are not in pending payment status: {string.Join(", ", nonPendingOrders.Select(o => o.Id))}" });
            }

            // Update all orders status to Completed
            foreach (var order in orders)
            {
                order.Status = "Completed";
            }

            await _context.SaveChangesAsync();

            return Json(new { success = true, message = $"Successfully marked {orders.Count} orders as paid" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error marking orders as paid: {ex.Message}" });
        }
    }

    [HttpGet]
    /// <summary>
    /// Create Menu Item (GET) - Display form to create a new menu item
    /// </summary>
    public async Task<IActionResult> CreateMenuItem()
    {
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        
        ViewBag.Categories = categories;
        return View();
    }

    [HttpPost]
    /// <summary>
    /// Get Next Category ID - Generate next available ID for a menu item in a category
    /// </summary>
    /// <param name="category">Category prefix to generate ID for</param>
    public IActionResult GetNextCategoryId(string category)
    {
        try
        {
            // Define category prefixes
            var categoryPrefixes = new Dictionary<string, string>
            {
                { "Main Dish", "M" },
                { "Side Dish", "S" },
                { "Dessert", "D" },
                { "Beverage", "B" }
            };

            if (!categoryPrefixes.ContainsKey(category))
            {
                return Json(new { success = false, message = "Invalid category" });
            }

            var prefix = categoryPrefixes[category];
            
            // Get all existing IDs for this category
            var existingIds = _context.Menu
                .Where(m => m.Id.StartsWith(prefix))
                .Select(m => m.Id)
                .ToList();

            // Find the next available number
            int nextNumber = 1;
            while (existingIds.Contains($"{prefix}{nextNumber:D3}"))
            {
                nextNumber++;
            }

            var nextId = $"{prefix}{nextNumber:D3}";
            
            return Json(new { success = true, nextId = nextId });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Create Menu Item (POST) - Process new menu item creation with image upload
    /// Handles category-based folder structure and image management
    /// </summary>
    /// <param name="menuItem">New menu item information</param>
    public async Task<IActionResult> CreateMenuItem(MenuItem menuItem)
    {
        try
        {
            // Validate category exists
            var category = await _context.Categories.FindAsync(menuItem.CategoryId);
            if (category == null)
                {
                    TempData["ErrorMessage"] = "Invalid category selected.";
                var categories = await _context.Categories
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.DisplayOrder)
                    .ThenBy(c => c.Name)
                    .ToListAsync();
                ViewBag.Categories = categories;
                    return View(menuItem);
            }

            // ID will be auto-generated in AppDbContext.SaveChanges()
            menuItem.Id = string.Empty; // Let the system generate it

            // Handle image upload with category-based folder structure
            if (menuItem.ImageFile != null && menuItem.ImageFile.Length > 0)
            {
                // Create category folder path
                var categoryFolder = Path.Combine("wwwroot", "Images", category.Name);
                
                // Create directory if it doesn't exist
                if (!Directory.Exists(categoryFolder))
                {
                    Directory.CreateDirectory(categoryFolder);
                }
                
                var fileName = Guid.NewGuid().ToString() + Path.GetExtension(menuItem.ImageFile.FileName);
                var filePath = Path.Combine(categoryFolder, fileName);
                
                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    await menuItem.ImageFile.CopyToAsync(stream);
                }
                
                menuItem.ImagePath = $"/Images/{category.Name}/{fileName}";
            }
            else
            {
                menuItem.ImagePath = "/Images/default-food.jpg"; // Default image
            }

            // Set timestamps
            menuItem.CreatedAt = DateTime.Now;
            menuItem.UpdatedAt = DateTime.Now;

            // Add to database
            _context.Menu.Add(menuItem);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Menu item '{menuItem.Name}' created successfully with ID: {menuItem.Id}";
            return RedirectToAction("Menu");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error creating menu item: {ex.Message}";
            Console.WriteLine($"Error creating menu item: {ex}");
            var categories = await _context.Categories
                .Where(c => c.IsActive)
                .OrderBy(c => c.DisplayOrder)
                .ThenBy(c => c.Name)
                .ToListAsync();
            ViewBag.Categories = categories;
            return View(menuItem);
        }
    }

    [HttpGet]
    /// <summary>
    /// Edit Menu Item (GET) - Display form to edit an existing menu item
    /// </summary>
    /// <param name="id">ID of the menu item to edit</param>
    public async Task<IActionResult> EditMenuItem(string id)
    {
        var menuItem = await _context.Menu
            .Include(m => m.Addons)
            .Include(m => m.Category)
            .FirstOrDefaultAsync(m => m.Id == id);
        
        if (menuItem == null)
        {
            return NotFound();
        }

        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        
        ViewBag.Categories = categories;
        return View(menuItem);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Edit Menu Item (POST) - Process menu item updates with image replacement
    /// Handles old image deletion and new image storage in category folders
    /// </summary>
    /// <param name="menuItem">Updated menu item information</param>
    public async Task<IActionResult> EditMenuItem(MenuItem menuItem)
    {
        try
        {
            // Basic validation
            if (string.IsNullOrEmpty(menuItem.Id))
            {
                TempData["ErrorMessage"] = "Menu item ID is required.";
                return RedirectToAction("Menu");
            }

            var existingItem = await _context.Menu.FindAsync(menuItem.Id);
            if (existingItem == null)
            {
                TempData["ErrorMessage"] = "Menu item not found.";
                return RedirectToAction("Menu");
            }

            // Update the item
            existingItem.Name = menuItem.Name;
            existingItem.Description = menuItem.Description;
            existingItem.Price = menuItem.Price;
            existingItem.CategoryId = menuItem.CategoryId;
            existingItem.IsAvailable = menuItem.IsAvailable;
            existingItem.UpdatedAt = DateTime.Now;

            // Handle image upload if provided
            if (menuItem.ImageFile != null && menuItem.ImageFile.Length > 0)
            {
                // Get the category for folder structure
                var category = await _context.Categories.FindAsync(menuItem.CategoryId);
                if (category != null)
                {
                    // Delete old image if it exists
                    if (!string.IsNullOrEmpty(existingItem.ImagePath) && 
                        !existingItem.ImagePath.Contains("default-food.jpg"))
                    {
                        var oldImagePath = existingItem.ImagePath.Replace("/Images/", "wwwroot/Images/");
                        var oldFullPath = Path.Combine(_hostEnvironment.ContentRootPath, oldImagePath);
                        
                if (System.IO.File.Exists(oldFullPath))
                {
                    System.IO.File.Delete(oldFullPath);
                }
                        
                        // Check if old category folder is empty and delete it if so
                        var oldCategoryFolder = Path.GetDirectoryName(oldFullPath);
                        if (Directory.Exists(oldCategoryFolder) && 
                            !Directory.EnumerateFileSystemEntries(oldCategoryFolder).Any())
                        {
                            Directory.Delete(oldCategoryFolder);
                        }
                    }

                    // Create new category folder path
                    var categoryFolder = Path.Combine("wwwroot", "Images", category.Name);
                    
                    // Create directory if it doesn't exist
                    if (!Directory.Exists(categoryFolder))
                    {
                        Directory.CreateDirectory(categoryFolder);
                    }
                    
                    var fileName = Guid.NewGuid().ToString() + Path.GetExtension(menuItem.ImageFile.FileName);
                    var filePath = Path.Combine(categoryFolder, fileName);
                    
                    using (var stream = new FileStream(filePath, FileMode.Create))
                    {
                        await menuItem.ImageFile.CopyToAsync(stream);
                    }
                    
                    existingItem.ImagePath = $"/Images/{category.Name}/{fileName}";
                }
            }

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Menu item updated successfully!";
            
            return RedirectToAction("Menu");
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error updating menu item: {ex.Message}";
            return RedirectToAction("Menu");
        }
    }

    [HttpPost]
    /// <summary>
    /// Delete Menu Item - Remove a menu item and its associated image
    /// Automatically deletes image file and cleans up empty category folders
    /// </summary>
    /// <param name="id">ID of the menu item to delete</param>
    public async Task<IActionResult> DeleteMenuItem(string id)
    {
        try
        {
            var menuItem = await _context.Menu.FindAsync(id);
            if (menuItem == null)
            {
                return Json(new { success = false, message = "Menu item not found" });
            }

            // Delete associated image file if it exists
            if (!string.IsNullOrEmpty(menuItem.ImagePath) && 
                !menuItem.ImagePath.Contains("default-food.jpg"))
            {
                var imagePath = menuItem.ImagePath.Replace("/Images/", "wwwroot/Images/");
                var fullPath = Path.Combine(_hostEnvironment.ContentRootPath, imagePath);
                
                if (System.IO.File.Exists(fullPath))
                {
                    System.IO.File.Delete(fullPath);
                }
                
                // Check if category folder is empty and delete it if so
                var categoryFolder = Path.GetDirectoryName(fullPath);
                if (Directory.Exists(categoryFolder) && 
                    !Directory.EnumerateFileSystemEntries(categoryFolder).Any())
                {
                    Directory.Delete(categoryFolder);
                }
            }

            _context.Menu.Remove(menuItem);
            await _context.SaveChangesAsync();

            return Json(new { success = true, message = "Menu item deleted successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Toggle Menu Item Availability - Enable/disable a menu item
    /// </summary>
    /// <param name="id">ID of the menu item to toggle</param>
    public async Task<IActionResult> ToggleMenuItemAvailability(string id)
    {
        try
        {
            var menuItem = await _context.Menu.FindAsync(id);
            if (menuItem == null)
            {
                return Json(new { success = false, message = "Menu item not found" });
            }

            menuItem.IsAvailable = !menuItem.IsAvailable;
            menuItem.UpdatedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return Json(new { 
                success = true, 
                isAvailable = menuItem.IsAvailable, 
                message = menuItem.IsAvailable ? "Menu item activated" : "Menu item deactivated" 
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpGet]
    /// <summary>
    /// Create Addon (GET) - Display form to create a new addon
    /// </summary>
    /// <param name="menuItemId">Optional menu item ID to associate with</param>
    public async Task<IActionResult> CreateAddon(string menuItemId = null)
    {
        ViewBag.MenuItems = await _context.Menu.ToListAsync();
        ViewBag.ExistingAddons = await _context.Addons
            .Include(a => a.MenuItem)
            .ToListAsync();
        
        // If MenuItemId is provided, get the menu item details
        if (!string.IsNullOrEmpty(menuItemId))
        {
            var menuItem = await _context.Menu.FirstOrDefaultAsync(m => m.Id == menuItemId);
            if (menuItem != null)
            {
                ViewBag.SelectedMenuItem = menuItem;
                ViewBag.MenuItemId = menuItemId;
            }
        }
        
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Create Addon (POST) - Process new addon creation
    /// </summary>
    /// <param name="addon">New addon information</param>
    public async Task<IActionResult> CreateAddon(Addon addon)
    {
        try
        {
            // Debug: Log the addon data
            Console.WriteLine($"Creating addon: Name={addon.Name}, MenuItemId={addon.MenuItemId}, Price={addon.Price}, IsRequired={addon.IsRequired}");

            // Validate required fields
            if (string.IsNullOrEmpty(addon.Name))
            {
                TempData["ErrorMessage"] = "Addon name is required.";
                ViewBag.MenuItems = await _context.Menu.ToListAsync();
                ViewBag.ExistingAddons = await _context.Addons
                    .Include(a => a.MenuItem)
                    .ToListAsync();
                return View(addon);
            }

            if (string.IsNullOrEmpty(addon.MenuItemId))
            {
                TempData["ErrorMessage"] = "Please select a menu item.";
                ViewBag.MenuItems = await _context.Menu.ToListAsync();
                ViewBag.ExistingAddons = await _context.Addons
                    .Include(a => a.MenuItem)
                    .ToListAsync();
                return View(addon);
            }

            // Set default values
            addon.Type = addon.IsRequired ? "Required" : "Optional";

            _context.Addons.Add(addon);
            await _context.SaveChangesAsync();

            // Create bidirectional conflicting relationships
            if (!string.IsNullOrEmpty(addon.ConflictingAddons))
            {
                await CreateBidirectionalConflicts(addon.Id, addon.ConflictingAddons);
            }

            TempData["SuccessMessage"] = $"Addon '{addon.Name}' created successfully!";
            return RedirectToAction("MenuItemAddons", new { MenuItemId = addon.MenuItemId });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error creating addon: {ex}");
            TempData["ErrorMessage"] = $"Error creating addon: {ex.Message}";
            ViewBag.MenuItems = await _context.Menu.ToListAsync();
            ViewBag.ExistingAddons = await _context.Addons
                .Include(a => a.MenuItem)
                .ToListAsync();
            return View(addon);
        }
    }

    [HttpGet]
    /// <summary>
    /// Menu Item Addons - View and manage addons for a specific menu item
    /// </summary>
    /// <param name="MenuItemId">ID of the menu item</param>
    public async Task<IActionResult> MenuItemAddons(string MenuItemId)
    {
        var menuItem = await _context.Menu
            .Include(m => m.Addons)
            .FirstOrDefaultAsync(m => m.Id == MenuItemId);

        if (menuItem == null)
        {
            return NotFound();
        }

        return View(menuItem);
    }

    [HttpPost]
    public async Task<IActionResult> GetConflictingAddons(string menuItemId)
    {
        try
        {
            var addons = await _context.Addons
                .Where(a => a.MenuItemId == menuItemId)
                .Select(a => new {
                    id = a.Id,
                    name = a.Name,
                    price = a.Price
                })
                .ToListAsync();

            return Json(new { success = true, addons = addons });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, addons = new List<object>() });
        }
    }
}