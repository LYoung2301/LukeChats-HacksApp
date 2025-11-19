using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetailMonolith.Data;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Azure.Messaging.EventHubs;
using Azure.Messaging.EventHubs.Producer;

namespace LukeChats_HacksApp.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class ChatbotController : ControllerBase
    {
        private readonly string endpoint = DotNetEnv.Env.GetString("AzureOpenAI_Endpoint") ?? "";
        private readonly string apiKey = DotNetEnv.Env.GetString("AzureOpenAI_Key") ?? "";
        private readonly string model = DotNetEnv.Env.GetString("AzureOpenAI_Model") ?? "model-router";

        private readonly AppDbContext _db;
        private readonly string eventHubConnectionString = DotNetEnv.Env.GetString("EventHub__ConnectionString") ?? "";
        private readonly string eventHubName = DotNetEnv.Env.GetString("EventHub__Name") ?? "";

        public ChatbotController(AppDbContext db)
        {
            _db = db;
        }

        [HttpPost]
        public async Task<IActionResult> Post([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new { reply = "Message cannot be empty." });

            // Key messages by user: use a UserId from request, or fallback to a GUID if not provided
            var userId = request.UserId ?? Guid.NewGuid().ToString();

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

            // Send message and response to Event Hub
            await SendToEventHubAsync(userId, request.Message, reply);

            return Ok(new { reply });
        }

        private async Task SendToEventHubAsync(string userId, string userMessage, string botReply)
        {
            if (string.IsNullOrWhiteSpace(eventHubConnectionString) || string.IsNullOrWhiteSpace(eventHubName))
                return;
            var eventData = new
            {
                UserId = userId,
                Message = userMessage,
                Reply = botReply,
                Timestamp = DateTime.UtcNow
            };
            var eventJson = JsonSerializer.Serialize(eventData);
            await using var producerClient = new EventHubProducerClient(eventHubConnectionString, eventHubName);
            using EventDataBatch eventBatch = await producerClient.CreateBatchAsync();
            eventBatch.TryAdd(new EventData(Encoding.UTF8.GetBytes(eventJson)));
            await producerClient.SendAsync(eventBatch);
        }

        public class ChatRequest
        {
            public string? Message { get; set; }
            public string? UserId { get; set; } // Used for event stream keying
        }
    }
}
