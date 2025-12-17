using Microsoft.EntityFrameworkCore;
using DineInSystem.Models;

public class CartTimeoutService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CartTimeoutService> _logger;
    private Timer? _timer;
    private readonly TimeSpan _cartTimeout = TimeSpan.FromMinutes(30); // 30 minutes timeout

    public CartTimeoutService(IServiceProvider serviceProvider, ILogger<CartTimeoutService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cart Timeout Service started");
        
        // Run cleanup every 5 minutes
        _timer = new Timer(DoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));
        
        return Task.CompletedTask;
    }

    private async void DoWork(object? state)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            
            var cutoffTime = DateTime.Now - _cartTimeout;
            
            // Find carts that are older than the timeout period
            var expiredCarts = await context.Cart
                .Where(c => c.CreatedAt < cutoffTime)
                .Include(c => c.Items)
                .ToListAsync();
            
            if (expiredCarts.Any())
            {
                _logger.LogInformation($"Cleaning up {expiredCarts.Count} expired carts");
                
                // Remove cart items first
                foreach (var cart in expiredCarts)
                {
                    context.CartItems.RemoveRange(cart.Items);
                }
                
                // Remove the carts
                context.Cart.RemoveRange(expiredCarts);
                
                await context.SaveChangesAsync();
                
                _logger.LogInformation($"Successfully cleaned up {expiredCarts.Count} expired carts");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while cleaning up expired carts");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Cart Timeout Service stopped");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}