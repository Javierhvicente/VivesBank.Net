using Microsoft.AspNetCore.Mvc;
using VivesBankApi.Rest.Product.Base.Dto;
using VivesBankApi.Rest.Product.Base.Exception;
using VivesBankApi.Rest.Product.Service;

namespace VivesBankApi.Rest.Product.Base.Controller;

[ApiController]
[Route("api/[controller]")]
public class ProductController : ControllerBase
{
    private readonly ProductService _productService;
    private readonly ILogger _logger;
    
    public ProductController(ProductService productService, ILogger<ProductController> logger)
    {
        _productService = productService;
        _logger = logger;
    }

    [HttpGet]
    public async Task<ActionResult<List<ProductResponse>>> GetAllProductsAsync()
    {
        _logger.LogInformation("Getting all products");
        var products = await _productService.GetAllProductsAsync();
        return Ok(products);
    }
    
    [HttpGet("{productId}")]
    public async Task<ActionResult<ProductResponse>> GetProductByIdAsync(string productId)
    {
        _logger.LogInformation($"Getting product with id {productId}");
        try
        {
            var result = await _productService.GetProductByIdAsync(productId);

            if (result == null) return NotFound();

            return Ok(result);
        }
        catch (ProductException.ProductNotFoundException)
        {
            return NotFound();
        }
    }

    [HttpPost]
    public async Task<ActionResult<ProductResponse>> CreateProductAsync(ProductCreateRequest request)
    {
        _logger.LogInformation("Creating a new product");
        return await _productService.CreateProductAsync(request);
    }

    [HttpPut("{productId}")]
    public async Task<ActionResult<ProductResponse>> UpdateProductAsync(string productId, ProductUpdateRequest request)
    {
        _logger.LogInformation($"Updating product with id {productId}");
        var result = await _productService.UpdateProductAsync(productId, request);

        if (result == null) return NotFound();

        return Ok(result);
    }

    [HttpDelete("{productId}")]
    public async Task<IActionResult> DeleteProductAsync(string productId)
    {
        _logger.LogInformation($"Deleting product with id {productId}");
        await _productService.DeleteProductAsync(productId);
        return NoContent();
    }

}