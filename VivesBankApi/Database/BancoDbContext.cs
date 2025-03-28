﻿using Microsoft.EntityFrameworkCore;
using VivesBankApi.Rest.Clients.Models;
using VivesBankApi.Rest.Product.BankAccounts.Models;
using VivesBankApi.Rest.Product.Base.Models;
using VivesBankApi.Rest.Product.CreditCard.Models;
using VivesBankApi.Rest.Users.Models;

namespace VivesBankApi.Database;

/// <summary>
/// Contexto de base de datos para la aplicación bancaria.
/// Proporciona acceso a las tablas de cuentas, usuarios, clientes, productos y tarjetas de crédito.
/// </summary>
/// <author>Raul Fernandez, Javier Hernandez, Samuel Cortes, Alvaro Herrero, German, Tomas</author>
/// <version>1.0</version>
public class BancoDbContext : DbContext
{
    /// <summary>
    /// Constructor que configura el contexto de la base de datos.
    /// </summary>
    /// <param name="options">Opciones para la configuración del DbContext.</param>
    public BancoDbContext(DbContextOptions<BancoDbContext> options) : base(options) { }

    /// <summary>
    /// Representa la tabla de cuentas en la base de datos.
    /// </summary>
    public DbSet<Account> Accounts { get; set; }

    /// <summary>
    /// Representa la tabla de usuarios en la base de datos.
    /// </summary>
    public DbSet<User> Users { get; set; }

    /// <summary>
    /// Representa la tabla de clientes en la base de datos.
    /// </summary>
    public DbSet<Client> Clients { get; set; }

    /// <summary>
    /// Representa la tabla de productos en la base de datos.
    /// </summary>
    public DbSet<Product> Products { get; set; }

    /// <summary>
    /// Representa la tabla de tarjetas de crédito en la base de datos.
    /// </summary>
    public DbSet<CreditCard> Cards { get; set; }
}