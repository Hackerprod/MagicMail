using System.Net.Http.Json;
using System.Text.Json;

namespace MagicMail.Client
{
    /// <summary>
    /// A simple, reusable client for the MagicMail API.
    /// Copy this file to your other projects to start sending emails.
    /// </summary>
    public class MagicMailClient
    {
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public MagicMailClient(string baseUrl, string apiKey, HttpClient? httpClient = null)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<SendEmailResult> SendEmailAsync(string to, string subject, string body, string fromEmail, string fromName = "")
        {
            var request = new
            {
                to,
                subject,
                body,
                fromEmail,
                fromName
            };

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, $"{_baseUrl}/api/email/send");
            requestMessage.Headers.Add("X-Api-Key", _apiKey);
            requestMessage.Content = JsonContent.Create(request);

            try 
            {
                var response = await _httpClient.SendAsync(requestMessage);
                var content = await response.Content.ReadAsStringAsync();

                if (response.IsSuccessStatusCode)
                {
                    var result = JsonSerializer.Deserialize<SendSuccessResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                    return new SendEmailResult { Success = true, MessageId = result?.Id ?? 0, Message = result?.Message };
                }
                else
                {
                    return new SendEmailResult { Success = false, Message = $"Error {response.StatusCode}: {content}" };
                }
            }
            catch (Exception ex)
            {
                 return new SendEmailResult { Success = false, Message = $"Exception: {ex.Message}" };
            }
        }

        public class SendEmailResult
        {
            public bool Success { get; set; }
            public string? Message { get; set; }
            public int MessageId { get; set; }
        }

        private class SendSuccessResponse
        {
            public string? Message { get; set; }
            public int Id { get; set; }
        }
    }
}
