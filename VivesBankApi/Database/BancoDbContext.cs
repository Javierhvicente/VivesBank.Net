using Microsoft.EntityFrameworkCore;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.CreditCard.Models;
using VivesBankApi.Rest.Users.Models;

namespace VivesBankApi.Database;

public class BancoDbContext(DbContextOptions<BancoDbContext> options) : DbContext(options)
{
    
    public DbSet<Account> Cuentas { get; set; }
    public DbSet<User> Users { get; set; }
    public DbSet<Client> Clientes { get; set; }
    public DbSet<Product> Products { get; set; }
    public DbSet<CreditCard> Cards { get; set; }
    
}