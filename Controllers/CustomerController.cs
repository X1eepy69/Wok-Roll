using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using DineInSystem.Models;
using Microsoft.AspNetCore.Authorization;

/// <summary>
/// Customer Controller - Handles all customer-facing operations for the Dine-In System
/// Manages table selection, menu browsing, cart operations, and order placement
/// </summary>
[RequireCustomerOrGuest]
public class CustomerController : Controller
{
    private readonly AppDbContext _context;

    public CustomerController(AppDbContext context)
    {
        _context = context;
    }

    /// <summary>
    /// Customer Index - Redirects to table selection page
    /// </summary>
    public IActionResult Index()
    {
        return RedirectToAction("Table");
    }

    /// <summary>
    /// Guest Table Access - Allows guests to access table selection without login
    /// Sets up guest session and redirects to table selection
    /// </summary>
    [AllowAnonymous]
    public IActionResult GuestTable()
    {
        // Allow guest access without login
        HttpContext.Session.SetString("UserRole", "Guest");
        HttpContext.Session.SetString("UserName", "Guest User");
        var tables = _context.Tables.ToList();
        return View("Table", tables);
    }

    /// <summary>
    /// Table Selection - Display available tables for customer selection
    /// Handles both logged-in users and guest access
    /// </summary>
    public IActionResult Table()
    {
        // Check if user is logged in or is a guest
        var userId = HttpContext.Session.GetString("UserId");
        var userRole = HttpContext.Session.GetString("UserRole");
        
        // If no user ID and not a guest, redirect to login
        if (string.IsNullOrEmpty(userId) && userRole != "Guest")
        {
            TempData["ReturnUrl"] = Url.Action("Table", "Customer");
            // Clear any error messages before redirecting to login
            TempData.Remove("ErrorMessage");
            return RedirectToAction("Login", "Account");
        }

        var tables = _context.Tables.ToList();
        
        // Add session information to each table for frontend
        var currentSessionId = HttpContext.Session.Id;
        ViewBag.CurrentSessionId = currentSessionId;
        ViewBag.TableId = null; // Reset TableId for table selection page
        
        return View(tables);
    }

    /// <summary>
    /// Add Order - Display menu items for creating a new order for a table
    /// Shows categorized menu with filtering and addon options
    /// </summary>
    /// <param name="tableId">ID of the selected table</param>
    public async Task<IActionResult> AddOrder(int tableId)
    {
        // Clear any previous error messages
        TempData.Remove("ErrorMessage");
        
        // Check if user is logged in or is a guest first
        var userId = HttpContext.Session.GetString("UserId");
        var userRole = HttpContext.Session.GetString("UserRole");
        
        // If no user ID and not a guest, redirect to login
        if (string.IsNullOrEmpty(userId) && userRole != "Guest")
        {
            TempData["ReturnUrl"] = Url.Action("AddOrder", "Customer", new { tableId });
            return RedirectToAction("Login", "Account");
        }

        // Validate table ownership
        if (!CanAccessTableInternal(tableId))
        {
            TempData["ErrorMessage"] = "You don't have access to this table.";
            return RedirectToAction("Table");
        }

        var vm = new AddOrderViewModel
        {
            TableId = tableId,
            MenuItems = _context.Menu
                .Include(m => m.Category)
                .Where(m => m.IsAvailable)
                .ToList()
        };
        
        // Get categories for filter buttons
        var categories = await _context.Categories
            .Where(c => c.IsActive)
            .OrderBy(c => c.DisplayOrder)
            .ThenBy(c => c.Name)
            .ToListAsync();
        ViewBag.Categories = categories;
        ViewBag.TableId = tableId; // Set TableId for navigation
        return View(vm);
    }

    [HttpPost]
    /// <summary>
    /// Add To Cart - Add a menu item to the customer's cart
    /// </summary>
    /// <param name="TableId">ID of the table</param>
    /// <param name="MenuItemId">ID of the menu item to add</param>
    /// <param name="quantity">Quantity of the item</param>
    public IActionResult AddToCart(int TableId, string MenuItemId, int quantity)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            // Get or create cart for this table
            var cart = _context.Cart
                .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
                .FirstOrDefault(c => c.TableId == TableId);

            if (cart == null)
            {
                cart = new Cart
                {
                    TableId = TableId,
                    CreatedAt = DateTime.Now,
                    Items = new List<CartItem>()
                };
                _context.Cart.Add(cart);
                _context.SaveChanges(); // Save to get CartID
            }

            // Check if item already exists in cart
            var existingItem = cart.Items.FirstOrDefault(i => i.MenuItemId == MenuItemId);
            if (existingItem != null)
            {
                existingItem.Quantity += quantity;
            }
            else
            {
                cart.Items.Add(new CartItem
                {
                    CartId = cart.Id,
                    MenuItemId = MenuItemId,
                    Quantity = quantity
                });
            }

            _context.SaveChanges();

            var menuItem = _context.Menu.FirstOrDefault(m => m.Id == MenuItemId);
            TempData["SuccessMessage"] = $"{menuItem?.Name} added to cart!";
            return RedirectToAction("AddOrder", new { tableId = TableId });
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error adding item to cart: {ex.Message}";
            return RedirectToAction("AddOrder", new { tableId = TableId });
        }
    }

    /// <summary>
    /// Cart View - Display current cart contents for a table
    /// Shows cart items with quantities, prices, and total
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult Cart(int tableId)
    {
        // Clear any previous error messages
        TempData.Remove("ErrorMessage");
        
        // Check if user is logged in or is a guest first
        var userId = HttpContext.Session.GetString("UserId");
        var userRole = HttpContext.Session.GetString("UserRole");
        
        // If no user ID and not a guest, redirect to login
        if (string.IsNullOrEmpty(userId) && userRole != "Guest")
        {
            TempData["ReturnUrl"] = Url.Action("Cart", "Customer", new { tableId });
            return RedirectToAction("Login", "Account");
        }

        // Validate table ownership
        if (!CanAccessTableInternal(tableId))
        {
            TempData["ErrorMessage"] = "You don't have access to this table.";
            return RedirectToAction("Table");
        }

        var cart = _context.Cart
            .Include(c => c.Items)
            .ThenInclude(i => i.MenuItem)
            .Include(c => c.Table)
            .FirstOrDefault(c => c.TableId == tableId);

        if (cart == null)
        {
            // Create an empty cart object for display
            var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table == null)
            {
                TempData["ErrorMessage"] = "Table not found.";
                return RedirectToAction("Table");
            }
            
            cart = new Cart
            {
                TableId = tableId,
                Table = table,
                Items = new List<CartItem>()
            };
        }

        var subtotal = cart.Items.Sum(i => i.UnitPrice * i.Quantity);
        var tax = subtotal * 0.06m;
        var total = subtotal + tax;

        var model = new CartViewModel
        {
            TableId = tableId,
            Items = cart.Items,
            Subtotal = subtotal,
            Tax = tax,
            Total = total
        };

        ViewBag.TableId = tableId; // Set TableId for navigation
        return View(model);
    }

    [HttpPost]
    /// <summary>
    /// Update Cart Item - Modify quantity of an item in the cart
    /// </summary>
    /// <param name="cartItemId">ID of the cart item to update</param>
    /// <param name="quantity">New quantity</param>
    public IActionResult UpdateCartItem(int cartItemId, int quantity)
    {
        try
        {
            var cartItem = _context.CartItems
                .Include(ci => ci.Cart)
                .FirstOrDefault(ci => ci.Id == cartItemId);

            if (cartItem == null)
            {
                return Json(new { success = false, message = "Item not found" });
            }

            // Validate table ownership
            if (!CanAccessTableInternal(cartItem.Cart.TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            if (quantity <= 0)
            {
                _context.CartItems.Remove(cartItem);
            }
            else
            {
                cartItem.Quantity = quantity;
            }

            _context.SaveChanges();

            var cart = _context.Cart
                .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
                .FirstOrDefault(c => c.Id == cartItem.CartId);

            var subtotal = cart.Items.Sum(i => i.MenuItem.Price * i.Quantity);
            var tax = subtotal * 0.06m;
            var total = subtotal + tax;
            var cartCount = cart.Items.Count;

            return Json(new { 
                success = true, 
                subtotal = subtotal.ToString("F2"), 
                tax = tax.ToString("F2"), 
                total = total.ToString("F2"),
                cartCount = cartCount
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Remove Cart Item - Remove an item from the cart
    /// </summary>
    /// <param name="cartItemId">ID of the cart item to remove</param>
    public IActionResult RemoveCartItem(int cartItemId)
    {
        try
        {
            var cartItem = _context.CartItems
                .Include(ci => ci.Cart)
                .FirstOrDefault(ci => ci.Id == cartItemId);

            if (cartItem == null)
            {
                return Json(new { success = false, message = "Item not found" });
            }

            // Validate table ownership
            if (!CanAccessTableInternal(cartItem.Cart.TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            var cartId = cartItem.CartId;
            _context.CartItems.Remove(cartItem);
            _context.SaveChanges();

            var cart = _context.Cart
                .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
                .FirstOrDefault(c => c.Id == cartId);

            var subtotal = cart.Items.Sum(i => i.MenuItem.Price * i.Quantity);
            var tax = subtotal * 0.06m;
            var total = subtotal + tax;
            var cartCount = cart.Items.Count;

            return Json(new { 
                success = true, 
                subtotal = subtotal.ToString("F2"), 
                tax = tax.ToString("F2"), 
                total = total.ToString("F2"),
                cartCount = cartCount
            });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Clear Cart - Remove all items from the cart
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult ClearCart(int tableId)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(tableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            var cart = _context.Cart
                .Include(c => c.Items)
                .FirstOrDefault(c => c.TableId == tableId);

            if (cart != null)
            {
                _context.CartItems.RemoveRange(cart.Items);
                _context.Cart.Remove(cart);
                _context.SaveChanges();
            }

            return Json(new { success = true, message = "Cart cleared successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = $"Error clearing cart: {ex.Message}" });
        }
    }

    [HttpPost]
    /// <summary>
    /// Place Order - Process order placement and payment
    /// Handles payment validation and order creation
    /// </summary>
    /// <param name="model">Order and payment information</param>
    public async Task<IActionResult> PlaceOrder(CheckoutViewModel model)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(model.TableId))
            {
                TempData["ErrorMessage"] = "You don't have access to this table.";
                return RedirectToAction("Table");
            }

            var cart = _context.Cart
                .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
                .FirstOrDefault(c => c.TableId == model.TableId);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Cart is empty.";
                return RedirectToAction("Cart", new { tableId = model.TableId });
            }

            // Determine order status based on payment method
            string orderStatus;
            if (model.PaymentMethod == "Pay at Counter")
            {
                orderStatus = "Pending Payment";
            }
            else // Credit Card or Debit Card
            {
                orderStatus = "Completed";
            }

            // Create order
            var order = new Order
            {
                TableId = model.TableId,
                Status = orderStatus,
                Type = "Dine-In",
                OrderDate = DateTime.Now,
                TotalAmount = model.Total,
                Items = new List<OrderItem>()
            };

            // Convert cart items to order items
            foreach (var cartItem in cart.Items)
            {
                order.Items.Add(new OrderItem
                {
                    MenuItemId = cartItem.MenuItemId,
                    Quantity = cartItem.Quantity,
                    Subtotal = cartItem.UnitPrice * cartItem.Quantity,
                    SpecialInstructions = cartItem.SpecialInstructions
                });
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Create payment record
            var payment = new Payment
            {
                OrderId = order.Id,
                Amount = model.Total,
                Method = model.PaymentMethod,
                PaymentDate = DateTime.Now
            };

            _context.Payments.Add(payment);

            // Clear cart
            _context.CartItems.RemoveRange(cart.Items);
            _context.Cart.Remove(cart);

            await _context.SaveChangesAsync();

            if (orderStatus == "Completed")
            {
                TempData["SuccessMessage"] = "Order placed and payment completed successfully!";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
            else
            {
                TempData["SuccessMessage"] = "Order placed successfully! Please pay at the counter.";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error placing order: {ex.Message}";
            return RedirectToAction("Cart", new { tableId = model.TableId });
        }
    }

    [HttpGet]
    /// <summary>
    /// Get Cart Count - Return the number of items in the cart (AJAX)
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult GetCartCount(int tableId)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(tableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            var cart = _context.Cart
                .Include(c => c.Items)
                .FirstOrDefault(c => c.TableId == tableId);

            var cartCount = cart?.Items.Sum(i => i.Quantity) ?? 0;
            return Json(new { success = true, cartCount = cartCount });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"GetCartCount Error: {ex.Message}");
            return Json(new { success = false, message = $"Error getting cart count: {ex.Message}" });
        }
    }

    [HttpGet]
    /// <summary>
    /// Get Cart Info - Return cart summary information (AJAX)
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult GetCartInfo(int tableId)
    {
        // Validate table ownership
        if (!CanAccessTableInternal(tableId))
        {
            return Json(new { success = false, message = "You don't have access to this table." });
        }

        var cart = _context.Cart
            .FirstOrDefault(c => c.TableId == tableId);

        if (cart != null)
        {
            return Json(new { 
                hasCart = true, 
                createdAt = cart.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ") 
            });
        }

        return Json(new { hasCart = false });
    }

    [HttpGet]
    /// <summary>
    /// Checkout - Display checkout form for order completion
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult Checkout(int tableId)
    {
        // Clear any previous error messages
        TempData.Remove("ErrorMessage");
        
        // Validate table ownership
        if (!CanAccessTableInternal(tableId))
        {
            TempData["ErrorMessage"] = "You don't have access to this table.";
            return RedirectToAction("Table");
        }

        var cart = _context.Cart
            .Include(c => c.Items)
            .ThenInclude(i => i.MenuItem)
            .Include(c => c.Table)
            .FirstOrDefault(c => c.TableId == tableId);

        if (cart == null || !cart.Items.Any())
        {
            TempData["ErrorMessage"] = "Cart is empty.";
            return RedirectToAction("Cart", new { tableId = tableId });
        }

        var subtotal = cart.Items.Sum(i => i.MenuItem.Price * i.Quantity);
        var tax = subtotal * 0.06m;
        var total = subtotal + tax;

        var model = new CheckoutViewModel
        {
            TableId = tableId,
            Cart = cart,
            Subtotal = subtotal,
            Tax = tax,
            Total = total
        };

        ViewBag.TableId = tableId; // Set TableId for navigation
        return View(model);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    /// <summary>
    /// Process Payment - Handle payment processing and order finalization
    /// Validates payment details and creates final order
    /// </summary>
    /// <param name="model">Payment and order information</param>
    public async Task<IActionResult> ProcessPayment(CheckoutViewModel model)
    {
        if (!ModelState.IsValid)
        {
            return View("Checkout", model);
        }

        try
        {
            var cart = _context.Cart
                .Include(c => c.Items)
                .ThenInclude(i => i.MenuItem)
                .Include(c => c.Table)
                .FirstOrDefault(c => c.TableId == model.TableId);

            if (cart == null || !cart.Items.Any())
            {
                TempData["ErrorMessage"] = "Cart is empty.";
                return RedirectToAction("Cart", new { tableId = model.TableId });
            }

            // Validate table ownership
            if (!CanAccessTableInternal(model.TableId))
            {
                TempData["ErrorMessage"] = "You don't have access to this table.";
                return RedirectToAction("Table");
            }

            // Determine order status based on payment method
            string orderStatus;
            if (model.PaymentMethod == "Pay at Counter")
            {
                orderStatus = "Pending Payment";
            }
            else // Credit Card or Debit Card
            {
                orderStatus = "Completed";
            }

            // Create order
            var order = new Order
            {
                TableId = model.TableId,
                Status = orderStatus,
                Type = "Dine-In",
                OrderDate = DateTime.Now,
                TotalAmount = model.Total,
                Items = new List<OrderItem>()
            };

            // Convert cart items to order items
            foreach (var cartItem in cart.Items)
            {
                order.Items.Add(new OrderItem
                {
                    MenuItemId = cartItem.MenuItemId,
                    Quantity = cartItem.Quantity,
                    Subtotal = cartItem.UnitPrice * cartItem.Quantity,
                    SpecialInstructions = cartItem.SpecialInstructions
                });
            }

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            // Create payment record
            var payment = new Payment
            {
                OrderId = order.Id,
                Amount = model.Total,
                Method = model.PaymentMethod,
                PaymentDate = DateTime.Now
            };

            _context.Payments.Add(payment);

            // Clear cart
            _context.CartItems.RemoveRange(cart.Items);
            _context.Cart.Remove(cart);

            await _context.SaveChangesAsync();

            if (orderStatus == "Completed")
            {
                TempData["SuccessMessage"] = "Order placed and payment completed successfully!";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
            else
            {
                TempData["SuccessMessage"] = "Order placed successfully! Please pay at the counter.";
                return RedirectToAction("OrderConfirmation", new { orderId = order.Id });
            }
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error processing payment: {ex.Message}";
            return View("Checkout", model);
        }
    }

    [HttpGet]
    /// <summary>
    /// Order History - Display order history for a specific table
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    public IActionResult OrderHistory(int tableId)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(tableId))
            {
                TempData["ErrorMessage"] = "You don't have access to this table.";
                return RedirectToAction("Table");
            }

            var orders = _context.Orders
                .Where(o => o.TableId == tableId)
                .Include(o => o.Items)
                .ThenInclude(oi => oi.MenuItem)
                .OrderByDescending(o => o.OrderDate)
                .ToList();

            ViewBag.TableId = tableId;
            return View(orders);
        }
        catch (Exception ex)
        {
            TempData["ErrorMessage"] = $"Error loading order history: {ex.Message}";
            return RedirectToAction("Table");
        }
    }

    [HttpGet]
    /// <summary>
    /// Order Confirmation - Display order confirmation after successful placement
    /// </summary>
    /// <param name="orderId">ID of the completed order</param>
    public IActionResult OrderConfirmation(int orderId)
    {
        var order = _context.Orders
            .Include(o => o.Items)
            .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .FirstOrDefault(o => o.Id == orderId);

        if (order == null)
        {
            TempData["ErrorMessage"] = "Order not found.";
            return RedirectToAction("Table");
        }

        return View(order);
    }

    [HttpGet]
    /// <summary>
    /// Order History - Display user's order history
    /// Shows all orders for logged-in users
    /// </summary>
    public IActionResult History()
    {
        var userId = HttpContext.Session.GetString("UserId");
        if (string.IsNullOrEmpty(userId))
        {
            return RedirectToAction("Login", "Account");
        }

        // Get user's order history
        var orders = _context.Orders
            .Include(o => o.Items)
            .ThenInclude(oi => oi.MenuItem)
            .Include(o => o.Table)
            .Include(o => o.Payments)
            .Where(o => o.TableId != null) // This is a simplified approach
            .OrderByDescending(o => o.OrderDate)
            .ToList();

        return View(orders);
    }

    [HttpPost]
    /// <summary>
    /// Set Pax - Set the number of people for a table
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    /// <param name="pax">Number of people</param>
    public IActionResult SetPax(int tableId, int pax)
    {
        try
        {
            var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
            
            if (table == null)
            {
                return Json(new { success = false, message = "Table not found" });
            }

            // Get current session ID
            var currentSessionId = HttpContext.Session.Id;
            
            // Check if table is already occupied by someone else
            if (table.IsOccupied && table.CurrentSessionId != currentSessionId)
            {
                return Json(new { success = false, message = "This table is currently occupied by another customer." });
            }

            // Update pax and mark table as occupied by current session
            table.Pax = pax;
            table.IsOccupied = true;
            table.CurrentSessionId = currentSessionId;
            table.OccupiedAt = DateTime.Now;
            
            _context.SaveChanges();

            return Json(new { success = true });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }


    [HttpPost]
    /// <summary>
    /// Get Addons - Retrieve available addons for a menu item (AJAX)
    /// </summary>
    /// <param name="menuItemId">ID of the menu item</param>
    public IActionResult GetAddons(string menuItemId)
    {
        try
        {
            var addons = _context.Addons
                .Where(a => a.MenuItemId == menuItemId)
                .Select(a => new {
                    id = a.Id,
                    name = a.Name,
                    price = a.Price,
                    isRequired = a.IsRequired,
                    type = a.Type
                })
                .ToList();

            return Json(new { success = true, addons = addons });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, addons = new List<object>() });
        }
    }

    [HttpPost]
    /// <summary>
    /// Get Addons For Menu Item - Get addons with conflict information (AJAX)
    /// </summary>
    /// <param name="menuItemId">ID of the menu item</param>
    public IActionResult GetAddonsForMenuItem(string menuItemId)
    {
        try
        {
            var addons = _context.Addons
                .Where(a => a.MenuItemId == menuItemId)
                .Select(a => new {
                    id = a.Id,
                    name = a.Name,
                    price = a.Price,
                    isRequired = a.IsRequired,
                    type = a.Type,
                    conflictingAddons = a.ConflictingAddons
                })
                .ToList();

            return Json(new { success = true, addons = addons });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message, addons = new List<object>() });
        }
    }

    [HttpPost]
    /// <summary>
    /// Get Cart Item Details - Retrieve detailed information about a cart item (AJAX)
    /// </summary>
    /// <param name="cartItemId">ID of the cart item</param>
    public IActionResult GetCartItemDetails(string cartItemId)
    {
        try
        {
            var cartItem = _context.CartItems
                .Include(ci => ci.MenuItem)
                .Include(ci => ci.Cart)
                .FirstOrDefault(ci => ci.Id.ToString() == cartItemId);

            if (cartItem == null)
            {
                return Json(new { success = false, message = "Cart item not found" });
            }

            // Validate table ownership
            if (!CanAccessTableInternal(cartItem.Cart.TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            var itemData = new
            {
                cartItemId = cartItem.Id,
                menuItemId = cartItem.MenuItemId,
                menuItemName = cartItem.MenuItem?.Name ?? "Unknown Item",
                category = cartItem.MenuItem?.Category?.Name ?? "Unknown Category",
                quantity = cartItem.Quantity,
                unitPrice = cartItem.MenuItem?.Price ?? 0, // Base price without addons
                specialInstructions = cartItem.SpecialInstructions
            };

            return Json(new { success = true, item = itemData });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Update Cart Item Details - Update cart item with addons and special instructions
    /// </summary>
    /// <param name="cartItemId">ID of the cart item to update</param>
    /// <param name="quantity">New quantity</param>
    /// <param name="specialInstructions">Special instructions for the item</param>
    /// <param name="selectedAddons">JSON string of selected addons</param>
    public IActionResult UpdateCartItemDetails(string cartItemId, int quantity, string specialInstructions, string selectedAddons)
    {
        try
        {
            var cartItem = _context.CartItems
                .Include(ci => ci.MenuItem)
                .Include(ci => ci.Cart)
                .FirstOrDefault(ci => ci.Id.ToString() == cartItemId);

            if (cartItem == null)
            {
                return Json(new { success = false, message = "Cart item not found" });
            }

            // Validate table ownership
            if (!CanAccessTableInternal(cartItem.Cart.TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            // Parse selected addons and calculate addon total
            var addonTotal = 0m;
            var addonDetails = "";
            if (!string.IsNullOrEmpty(selectedAddons))
            {
                var addonIds = selectedAddons.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var addonList = new List<string>();
                
                foreach (var addonId in addonIds)
                {
                    var addon = _context.Addons.FirstOrDefault(a => a.Id.ToString() == addonId);
                    if (addon != null)
                    {
                        addonTotal += addon.Price;
                        addonList.Add($"{addon.Name} (+${addon.Price:F2})");
                    }
                }
                
                if (addonList.Any())
                {
                    addonDetails = string.Join(", ", addonList);
                }
            }

            // Create combined instructions with special separator for addons
            var combinedInstructions = "";
            if (!string.IsNullOrEmpty(specialInstructions) && !string.IsNullOrEmpty(addonDetails))
            {
                combinedInstructions = $"{specialInstructions}|||{addonDetails}";
            }
            else if (!string.IsNullOrEmpty(specialInstructions))
            {
                combinedInstructions = specialInstructions;
            }
            else if (!string.IsNullOrEmpty(addonDetails))
            {
                combinedInstructions = $"|||{addonDetails}";
            }

            // Update cart item
            cartItem.Quantity = quantity;
            cartItem.UnitPrice = cartItem.MenuItem.Price + addonTotal;
            cartItem.SpecialInstructions = string.IsNullOrEmpty(combinedInstructions) ? null : combinedInstructions;

            _context.SaveChanges();

            return Json(new { success = true, message = "Item updated successfully" });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Add To Cart With Addons - Add menu item to cart with addons and special instructions
    /// </summary>
    /// <param name="TableId">ID of the table</param>
    /// <param name="MenuItemId">ID of the menu item</param>
    /// <param name="quantity">Quantity of the item</param>
    /// <param name="specialInstructions">Special instructions</param>
    /// <param name="selectedAddons">JSON string of selected addons</param>
    public IActionResult AddToCartWithAddons(int TableId, string MenuItemId, int quantity, string specialInstructions, string selectedAddons)
    {
        try
        {
            // Validate table ownership
            if (!CanAccessTableInternal(TableId))
            {
                return Json(new { success = false, message = "You don't have access to this table." });
            }

            var table = _context.Tables.FirstOrDefault(t => t.Id == TableId);
            if (table == null)
            {
                return Json(new { success = false, message = "Table not found." });
            }

            var cart = _context.Cart
                .Include(c => c.Items)
                .FirstOrDefault(c => c.TableId == TableId);

            if (cart == null)
            {
                cart = new Cart { TableId = TableId, CreatedAt = DateTime.Now };
                _context.Cart.Add(cart);
                _context.SaveChanges(); // Save to get CartId
            }

            var menuItem = _context.Menu.FirstOrDefault(m => m.Id == MenuItemId);
            if (menuItem == null)
            {
                return Json(new { success = false, message = "Menu item not found." });
            }

            // Parse selected addons
            var addonTotal = 0m;
            var addonDetails = "";
            if (!string.IsNullOrEmpty(selectedAddons))
            {
                var addonIds = selectedAddons.Split(',', StringSplitOptions.RemoveEmptyEntries);
                var addonList = new List<string>();
                
                foreach (var addonId in addonIds)
                {
                    var addon = _context.Addons.FirstOrDefault(a => a.Id.ToString() == addonId);
                    if (addon != null)
                    {
                        addonTotal += addon.Price;
                        addonList.Add($"{addon.Name} (+${addon.Price:F2})");
                    }
                }
                
                if (addonList.Any())
                {
                    addonDetails = string.Join(", ", addonList);
                }
            }

            // Create combined instructions with special separator for addons
            var combinedInstructions = "";
            if (!string.IsNullOrEmpty(specialInstructions) && !string.IsNullOrEmpty(addonDetails))
            {
                combinedInstructions = $"{specialInstructions}|||{addonDetails}";
            }
            else if (!string.IsNullOrEmpty(specialInstructions))
            {
                combinedInstructions = specialInstructions;
            }
            else if (!string.IsNullOrEmpty(addonDetails))
            {
                combinedInstructions = $"|||{addonDetails}";
            }

            // Calculate total unit price (menu item + addons)
            decimal unitPrice = menuItem.Price + addonTotal;

            // Check if same item with same addons/instructions already exists
            var existingItem = cart.Items.FirstOrDefault(ci => 
                ci.MenuItemId == MenuItemId && 
                ci.SpecialInstructions == combinedInstructions);

            if (existingItem != null)
            {
                // Combine quantities for same item
                existingItem.Quantity += quantity;
            }
            else
            {
                // Create new cart item
                var cartItem = new CartItem
                {
                    CartId = cart.Id,
                    MenuItemId = MenuItemId,
                    Quantity = quantity,
                    UnitPrice = unitPrice,
                    SpecialInstructions = string.IsNullOrEmpty(combinedInstructions) ? null : combinedInstructions
                };

                cart.Items.Add(cartItem);
            }

            _context.SaveChanges();

            // Get updated cart count
            var cartCount = cart.Items.Sum(ci => ci.Quantity);

            return Json(new { 
                success = true, 
                message = $"{menuItem.Name} added to cart successfully!",
                cartCount = cartCount
            });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"AddToCartWithAddons Error: {ex.Message}");
            Console.WriteLine($"Stack Trace: {ex.StackTrace}");
            return Json(new { success = false, message = $"Error adding item to cart: {ex.Message}" });
        }
    }

    [HttpPost]
    /// <summary>
    /// Update Table Status - Update table occupancy status and pax count
    /// </summary>
    /// <param name="tableId">ID of the table</param>
    /// <param name="isOccupied">Whether the table is occupied</param>
    /// <param name="pax">Number of people</param>
    public IActionResult UpdateTableStatus(int tableId, bool isOccupied, int pax)
    {
        try
        {
            var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table == null)
            {
                return Json(new { success = false, message = "Table not found." });
            }

            table.IsOccupied = isOccupied;
            table.Pax = pax;
            _context.SaveChanges();

            return Json(new { success = true, message = "Table status updated successfully." });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UpdateTableStatus Error: {ex.Message}");
            return Json(new { success = false, message = $"Error updating table status: {ex.Message}" });
        }
    }

    private bool CanAccessTableInternal(int tableId)
    {
        var currentSessionId = HttpContext.Session.Id;
        var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
        
        if (table == null)
        {
            return false;
        }
        
        // Allow access if table is not occupied, or if occupied by current session
        bool hasAccess = !table.IsOccupied || table.CurrentSessionId == currentSessionId;
        
        // Debug logging (remove in production)
        if (!hasAccess)
        {
            Console.WriteLine($"Access denied for table {tableId}: IsOccupied={table.IsOccupied}, CurrentSessionId={table.CurrentSessionId}, RequestSessionId={currentSessionId}");
        }
        
        return hasAccess;
    }

    [HttpPost]
    /// <summary>
    /// Can Access Table - Check if current user/guest can access a specific table
    /// </summary>
    /// <param name="tableId">ID of the table to check</param>
    public IActionResult CanAccessTable(int tableId)
    {
        try
        {
            var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table == null)
            {
                return Json(new { success = false, message = "Table not found." });
            }

            var currentSessionId = HttpContext.Session.Id;
            
            // Check if table is available or owned by current session
            if (!table.IsOccupied || table.CurrentSessionId == currentSessionId)
            {
                return Json(new { 
                    success = true, 
                    canAccess = true,
                    isOwnedByCurrentSession = table.CurrentSessionId == currentSessionId,
                    occupiedAt = table.OccupiedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
            else
            {
                return Json(new { 
                    success = true, 
                    canAccess = false,
                    message = "This table is currently occupied by another customer.",
                    occupiedAt = table.OccupiedAt?.ToString("yyyy-MM-ddTHH:mm:ss")
                });
            }
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }

    [HttpPost]
    /// <summary>
    /// Release Table - Release table occupancy and clear associated data
    /// </summary>
    /// <param name="tableId">ID of the table to release</param>
    public IActionResult ReleaseTable(int tableId)
    {
        try
        {
            var table = _context.Tables.FirstOrDefault(t => t.Id == tableId);
            if (table == null)
            {
                return Json(new { success = false, message = "Table not found." });
            }

            var currentSessionId = HttpContext.Session.Id;
            
            // Only allow releasing if owned by current session
            if (table.CurrentSessionId != currentSessionId)
            {
                return Json(new { success = false, message = "You can only release tables you own." });
            }

            // Clear cart items first
            var cart = _context.Cart.FirstOrDefault(c => c.TableId == tableId);
            if (cart != null)
            {
                _context.CartItems.RemoveRange(cart.Items);
                _context.Cart.Remove(cart);
            }

            // Reset table
            table.IsOccupied = false;
            table.Pax = 0;
            table.CurrentSessionId = null;
            table.OccupiedAt = null;
            
            _context.SaveChanges();

            return Json(new { success = true, message = "Table released successfully." });
        }
        catch (Exception ex)
        {
            return Json(new { success = false, message = ex.Message });
        }
    }
}