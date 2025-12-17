using Microsoft.EntityFrameworkCore;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    // Core Tables with Clear Names
    public DbSet<User> Users { get; set; }
    public DbSet<Table> Tables { get; set; }
    public DbSet<Category> Categories { get; set; }
    public DbSet<MenuItem> Menu { get; set; }
    public DbSet<Addon> Addons { get; set; }
    public DbSet<Order> Orders { get; set; }
    public DbSet<OrderItem> OrderItems { get; set; }
    public DbSet<Cart> Cart { get; set; }
    public DbSet<CartItem> CartItems { get; set; }
    public DbSet<Payment> Payments { get; set; }
    public DbSet<PasswordResetToken> PasswordResetTokens { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure MenuItem ID generation
        modelBuilder.Entity<MenuItem>()
            .Property(m => m.Id)
            .ValueGeneratedNever();

        // Configure relationships
        modelBuilder.Entity<MenuItem>()
            .HasOne(m => m.Category)
            .WithMany(c => c.MenuItems)
            .HasForeignKey(m => m.CategoryId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.Table)
            .WithMany(t => t.Orders)
            .HasForeignKey(o => o.TableId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Order>()
            .HasOne(o => o.User)
            .WithMany(u => u.Orders)
            .HasForeignKey(o => o.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<Cart>()
            .HasOne(c => c.Table)
            .WithMany(t => t.Carts)
            .HasForeignKey(c => c.TableId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.Order)
            .WithMany(o => o.Items)
            .HasForeignKey(oi => oi.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<OrderItem>()
            .HasOne(oi => oi.MenuItem)
            .WithMany(m => m.OrderItems)
            .HasForeignKey(oi => oi.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.Cart)
            .WithMany(c => c.Items)
            .HasForeignKey(ci => ci.CartId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<CartItem>()
            .HasOne(ci => ci.MenuItem)
            .WithMany(m => m.CartItems)
            .HasForeignKey(ci => ci.MenuItemId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<Addon>()
            .HasOne(a => a.MenuItem)
            .WithMany(m => m.Addons)
            .HasForeignKey(a => a.MenuItemId)
            .OnDelete(DeleteBehavior.Cascade);


        modelBuilder.Entity<Payment>()
            .HasOne(p => p.Order)
            .WithMany(o => o.Payments)
            .HasForeignKey(p => p.OrderId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<PasswordResetToken>()
            .HasOne(prt => prt.User)
            .WithMany(u => u.PasswordResetTokens)
            .HasForeignKey(prt => prt.UserId)
            .OnDelete(DeleteBehavior.Cascade);
    }

    public override int SaveChanges()
    {
        GenerateMenuItemIDs();
        return base.SaveChanges();
    }

    public override async Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        GenerateMenuItemIDs();
        return await base.SaveChangesAsync(cancellationToken);
    }

    private void GenerateMenuItemIDs()
    {
        // Find new MenuItems being added
        var newItems = ChangeTracker.Entries<MenuItem>()
            .Where(e => e.State == EntityState.Added)
            .Select(e => e.Entity);

        foreach (var item in newItems)
        {
            if (!string.IsNullOrEmpty(item.Id))
                continue; // Skip if ID already exists

            // Get the category from the database
            var category = Categories.Find(item.CategoryId);
            if (category == null)
                continue;

            string prefix = category.Prefix;

            // Get last MenuItemID for this prefix
            var lastItem = Menu
                .Where(m => m.Id.StartsWith(prefix))
                .OrderByDescending(m => m.Id)
                .FirstOrDefault();

            int nextNumber = 1;

            if (lastItem != null)
            {
                string lastNumberStr = lastItem.Id.Substring(prefix.Length);
                if (int.TryParse(lastNumberStr, out int lastNumber))
                    nextNumber = lastNumber + 1;
            }

            item.Id = $"{prefix}{nextNumber:D3}";
        }
    }
}
