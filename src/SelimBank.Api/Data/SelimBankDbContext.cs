using Microsoft.EntityFrameworkCore;

namespace SelimBank.Api.Data;

public sealed class SelimBankDbContext(DbContextOptions<SelimBankDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Account> Accounts => Set<Account>();
    public DbSet<Card> Cards => Set<Card>();
    public DbSet<Transaction> Transactions => Set<Transaction>();
    public DbSet<Complaint> Complaints => Set<Complaint>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Foreign keys without navigation properties: endpoints always filter by CustomerId explicitly,
        // which keeps the ownership check visible in each query.
        modelBuilder.Entity<Account>().HasOne<Customer>().WithMany().HasForeignKey(a => a.CustomerId);

        modelBuilder.Entity<Card>().HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId);
        modelBuilder.Entity<Card>().HasOne<Account>().WithMany().HasForeignKey(c => c.AccountId);
        modelBuilder.Entity<Card>().Property(c => c.Kind).HasConversion<string>();
        modelBuilder.Entity<Card>().Property(c => c.Status).HasConversion<string>();

        modelBuilder.Entity<Transaction>().HasOne<Account>().WithMany().HasForeignKey(t => t.AccountId);
        modelBuilder.Entity<Transaction>().HasOne<Card>().WithMany().HasForeignKey(t => t.CardId);
        modelBuilder.Entity<Transaction>().HasIndex(t => new { t.AccountId, t.TimestampUtc });

        modelBuilder.Entity<Complaint>().HasOne<Customer>().WithMany().HasForeignKey(c => c.CustomerId);
        modelBuilder.Entity<Complaint>().HasOne<Transaction>().WithMany().HasForeignKey(c => c.TransactionId);
        modelBuilder.Entity<Complaint>().Property(c => c.Status).HasConversion<string>();
    }
}
