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
            await SendToEventHubAsync(userId, request.Message ?? string.Empty, reply ?? string.Empty);

            return Ok(new { reply });
        }
        
        [HttpPost("privacy")]
        public async Task<IActionResult> PrivacyChat([FromBody] ChatRequest request)
        {
            if (string.IsNullOrWhiteSpace(request?.Message))
                return BadRequest(new { reply = "Message cannot be empty." });

            var systemPrompt = "You are a privacy policy assistant. Answer questions and provide information about the privacy policy, data protection, and user rights for this website. Be clear, accurate, and helpful. Here is the privacy policy: Welcome to ExampleShop, and thank you for choosing to shop with us; this Privacy Policy explains how we collect, use, disclose, store, and protect your information when you access or use our website, mobile applications, services, or purchase products from us, and by using our platform you consent to the data practices described herein; we collect personal information you voluntarily provide such as your name, email address, phone number, billing information, shipping address, account credentials, and communication preferences as well as data automatically gathered including IP addresses, browser type, device identifiers, usage patterns, cookies, analytics data, and log files to enhance functionality, improve services, personalize your experience, prevent fraud, deliver targeted promotions, process transactions, fulfill orders, and provide customer support; we may share your information with trusted third-party service providers including payment processors, logistics partners, analytics companies, marketing platforms, hosting providers, and security vendors who process data on our behalf under confidentiality agreements as well as when required by law, to protect our rights, to comply with legal inquiries, to enforce our Terms of Service, or in connection with mergers, acquisitions, or business transfers; we do not sell your personal information but may use aggregated and anonymized data for research, reporting, or product development; we implement industry-standard administrative, physical, and technical safeguards to protect your information from unauthorized access, alteration, disclosure, or destruction, though no method of transmission over the internet is entirely secure and we cannot guarantee absolute security; you may update or delete your account information, opt out of marketing communications, manage cookie preferences, or request access to your data by contacting our support team, subject to verification and applicable legal retention requirements; our services are not intended for children under 16 and we do not knowingly collect data from minors; international users acknowledge that their information may be transferred to and processed in countries with different data protection laws; we retain personal data only for as long as necessary to fulfill the purposes outlined in this policy or as required by law; we may update or amend this Privacy Policy at any time and will post the revised version with an updated effective date, and your continued use of our services constitutes acceptance of any changes; if you have questions, concerns, or data-related requests you may contact our Data Protection Officer via the contact information provided on our website; by using ExampleShop you acknowledge that you have read, understood, and agreed to this Privacy Policy";

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
