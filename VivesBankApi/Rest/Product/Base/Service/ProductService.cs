using VivesBankApi.Rest.Product.Base.Dto;
using VivesBankApi.Rest.Product.Base.Exception;

namespace VivesBankApi.Rest.Product.Service;

public class ProductService : IProductService
{
    private readonly ILogger<ProductService> _logger;
    private readonly IProductRepository _productRepository;
    
    public ProductService(ILogger<ProductService> logger, IProductRepository productRepository)
    {
        _logger = logger;
        _productRepository = productRepository;
    }
    
    public async Task<List<ProductResponse>> GetAllProductsAsync()
    {
        _logger.LogInformation("Getting all products");
        
        var products = await _productRepository.GetAllAsync();
        
        return products.Select(product => product.ToDtoResponse()).ToList();        
    }

    public async Task<ProductResponse> GetProductByIdAsync(string productId)
    {
        _logger.LogInformation($"Getting product with id {productId}");
    
        var product = await _productRepository.GetByIdAsync(productId);

        if (product == null)
        {
            _logger.LogError($"Product not found with id {productId}");
            throw new ProductException.ProductNotFoundException(productId);
        }
    
        return product.ToDtoResponse();
    }


    public async Task<ProductResponse> CreateProductAsync(ProductCreateRequest createRequest)
    {
        _logger.LogInformation($"Creating product: {createRequest}");

        var productModel = ProductMapper.FromDtoRequest(createRequest);
        await _productRepository.AddAsync(productModel);
        
        return productModel.ToDtoResponse();
    }

    public async Task<ProductResponse> UpdateProductAsync(string productId, ProductUpdateRequest updateRequest)
    {
        _logger.LogInformation($"Updating product: {updateRequest} by Id: {productId}");
        
        var product = await _productRepository.GetByIdAsync(productId);
        
        if (product == null)
        {
            _logger.LogError($"Product not found with id {productId}");
            throw new ProductException.ProductNotFoundException(productId);
        }

        product.Name = updateRequest.Name;
        product.UpdatedAt = DateTime.UtcNow;

        await _productRepository.UpdateAsync(product);

        return product.ToDtoResponse();
    }

    public async Task DeleteProductAsync(string productId)
    {
        _logger.LogInformation($"Removing product by Id: {productId}");
    
        var product = await _productRepository.GetByIdAsync(productId);
        if (product == null)
        {
            _logger.LogError($"Product not found with id {productId}");
            throw new ProductException.ProductNotFoundException(productId);
        }
    
        await _productRepository.DeleteAsync(productId);
    }
}