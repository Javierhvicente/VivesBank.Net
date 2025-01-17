using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using VivesBankApi.utils.GuuidGenerator;

namespace VivesBankApi.Rest.Product.Base.Models;
[Table("Products")]
public class Product
{
    [Key]
    public String Id { get; set; }
    [Required]
    [MaxLength(100)]
    
    public String Name { get; set; }
    [Required]
    public Type ProductType { get; set; }
    
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow; 
    
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    
    [Required]
    public bool IsDeleted { get; set; }= false;

    public Product(String name, Type productType)
    {
        Id = GuuidGenerator.GenerateHash();
        Name = name;
        ProductType = productType;
    }

    public enum Type
    {
        BankAccount,
        CreditCard,
    }
}