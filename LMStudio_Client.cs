using System.Net.Http;
using System.Text;
using System.Text.Json;


namespace Enregistreur_vocal
{
    internal class LMStudio_Client
    {

        private readonly HttpClient _client = new();
        private const string BaseUrl = "http://localhost:1234/v1";

        public async Task<string> GetChatCompletionAsync(string prompt)
        {
            var requestBody = new
            {
                model = "openai/gpt-oss-20b",// "gpt-4o-mini",          // ou ton modèle dans LMStudio
                messages = new[]
                {
                new { role = "user", content = prompt }
            },
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
