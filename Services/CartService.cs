using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using RetailMonolith.Models;

namespace RetailMonolith.Services
{
    public class CartService : ICartService
    {
        private readonly AppDbContext _db;
        public CartService(AppDbContext db) => _db = db;

        public async Task RemoveFromCartAsync(string customerId, string sku, CancellationToken ct = default)
        {
            var cart = await _db.Carts.Include(c => c.Lines).FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
            if (cart is null) return;
            var line = cart.Lines.FirstOrDefault(l => l.Sku == sku);
            if (line is null) return;
            cart.Lines.Remove(line);
            _db.Remove(line);
            await _db.SaveChangesAsync(ct);
        }

        public async Task AddToCartAsync(string customerId, int productId, int quantity = 1, CancellationToken ct = default)
        {
            var cart = await GetOrCreateCartAsync(customerId, ct);
            var product = await _db.Products.FindAsync(new object[] { productId }, ct);
            if (product is null)
            {
                throw new InvalidOperationException("Invalid product ID");
            }
            var existing = cart.Lines.FirstOrDefault(line => line.Sku == product.Sku);
            if (existing is not null)
            {
                existing.Quantity += quantity;
            }
            else
            {
                var line = new CartLine
                {
                    CartId = cart.Id,
                    Sku = product.Sku,
                    Name = product.Name,
                    UnitPrice = product.Price,
                    Quantity = quantity
                };
                cart.Lines.Add(line);
            }
            await _db.SaveChangesAsync(ct);
        }

        public async Task ClearCartAsync(string customerId, CancellationToken ct = default)
        {
            var cart = await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
            if (cart is null) return;
            _db.Carts.Remove(cart);
            await _db.SaveChangesAsync(ct);
        }

        public async Task<Cart> GetCartWithLinesAsync(string customerId, CancellationToken ct = default)
        {
            return await _db.Carts
                .Include(c => c.Lines)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct) ?? new Cart { CustomerId = customerId };
        }

        public async Task<Cart> GetOrCreateCartAsync(string customerId, CancellationToken ct = default)
        {
            var cart = await _db.Carts
                .Include(c => c.Lines)
                .OrderBy(c => c.Id)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId, ct);
            if (cart is null)
            {
                cart = new Cart { CustomerId = customerId };
                _db.Carts.Add(cart);
                await _db.SaveChangesAsync(ct);
            }
            return cart;
        }
    }
}
