using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Enregistreur_vocal
{
    internal class LMStudio_Client
    {
        readonly HttpClient _client = new();
        const string BaseUrl = "http://localhost:1234/v1";

        public async Task<string> GetChatCompletionAsync(string prompt, string model_dans_LLMStudio)
        {
            var requestBody = new
            {
                model = model_dans_LLMStudio,
                messages = new[] { new { role = "user", content = prompt } },
                temperature = 0.8,
                max_tokens = 5000
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _client.PostAsync($"{BaseUrl}/chat/completions", content);

            response.EnsureSuccessStatusCode();

            var stream = await response.Content.ReadAsStreamAsync();
            using var doc = await JsonDocument.ParseAsync(stream);
            var text = doc.RootElement
                          .GetProperty("choices")[0]
                          .GetProperty("message")
                          .GetProperty("content")
                          .GetString() ?? "";

            return text.Trim();
        }
    }
}