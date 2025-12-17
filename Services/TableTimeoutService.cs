using Microsoft.EntityFrameworkCore;
using DineInSystem.Models;

public class TableTimeoutService : IHostedService, IDisposable
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<TableTimeoutService> _logger;
    private Timer? _timer;
    private readonly TimeSpan _tableTimeout = TimeSpan.FromMinutes(30); // 30 minutes timeout

    public TableTimeoutService(IServiceProvider serviceProvider, ILogger<TableTimeoutService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Table Timeout Service started");
        
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
            
            var cutoffTime = DateTime.Now - _tableTimeout;
            
            // Find tables that have been occupied for too long without activity
            // Exclude tables that have pending orders (pending payment)
            var abandonedTables = await context.Tables
                .Where(t => t.IsOccupied && t.OccupiedAt.HasValue && t.OccupiedAt < cutoffTime)
                .Include(t => t.Carts)
                    .ThenInclude(c => c.Items)
                .Where(t => !context.Orders.Any(o => o.TableId == t.Id && o.Status == "Pending Payment"))
                .ToListAsync();
            
            if (abandonedTables.Any())
            {
                _logger.LogInformation($"Cleaning up {abandonedTables.Count} abandoned tables");
                
                foreach (var table in abandonedTables)
                {
                    // Clear cart items and carts
                    foreach (var cart in table.Carts.ToList())
                    {
                        context.CartItems.RemoveRange(cart.Items);
                        context.Cart.Remove(cart);
                    }
                    
                    // Reset table
                    table.IsOccupied = false;
                    table.Pax = 0;
                    table.CurrentSessionId = null;
                    table.OccupiedAt = null;
                    
                    _logger.LogInformation($"Released abandoned table {table.Id}");
                }
                
                await context.SaveChangesAsync();
                
                _logger.LogInformation($"Successfully cleaned up {abandonedTables.Count} abandoned tables");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error occurred while cleaning up abandoned tables");
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Table Timeout Service stopped");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}
