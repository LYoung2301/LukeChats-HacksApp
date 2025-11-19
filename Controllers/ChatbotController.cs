using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace LukeChats_HacksApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly string endpoint = Environment.GetEnvironmentVariable("AZURE_OPENAI_ENDPOINT") ?? "";
        private readonly string apiKey = Environment.GetEnvironmentVariable("AZURE_OPENAI_KEY") ?? "";
        private readonly string model = Environment.GetEnvironmentVariable("AZURE_OPENAI_MODEL") ?? "model-router";

        private readonly AppDbContext _db;

        public ChatbotController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new { reply = "Message cannot be empty." });

            // Get current products
            var products = await _db.Products.Where(p => p.IsActive).ToListAsync();
            var productList = string.Join("\n", products.Select(p => $"- {p.Name}: {p.Description} (£{p.Price})"));
            var systemPrompt = $"You are a helpful assistant. Here are the current products available:\n{productList}\nRecommend products to users based on their needs.";

            using var httpClient = new HttpClient();
            httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
            httpClient.DefaultRequestHeaders.Add("api-key", apiKey);

            var payload = new
            {
                messages = new[]
                {
                    new { role = "system", content = systemPrompt },
                    new { role = "user", content = request.Message }
                },
                model = model
            };
            var json = JsonSerializer.Serialize(payload);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await httpClient.PostAsync(endpoint, content);
            if (!response.IsSuccessStatusCode)
            {
                return Ok(new { reply = "Sorry, something went wrong." });
            }
            var responseJson = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseJson);
            var reply = doc.RootElement
                .GetProperty("choices")[0]
                .GetProperty("message")
                .GetProperty("content")
                .GetString();
            return Ok(new { reply });
        }

        public class ChatRequest
        {
            public string? Message { get; set; }
        }
    }
}
