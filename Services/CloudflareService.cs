using System.Net.Http.Headers;
using MagicMail.Settings;
using Microsoft.Extensions.Options;

namespace MagicMail.Services
{
    public class CloudflareService
    {
        private readonly HttpClient _httpClient;
        private readonly AdminSettings _settings;
        private readonly ILogger<CloudflareService> _logger;

        public CloudflareService(HttpClient httpClient, IOptions<AdminSettings> settings, ILogger<CloudflareService> logger)
        {
            _httpClient = httpClient;
            _settings = settings.Value;
            _logger = logger;
            _httpClient.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _settings.CloudflareApiKey);
        }

        public async Task<string?> GetZoneIdAsync(string domain)
        {
            try
            {
                var response = await _httpClient.GetAsync($"zones?name={domain}");
                var json = await response.Content.ReadAsStringAsync();

                if (!response.IsSuccessStatusCode)
                {
                    _logger.LogError($"Cloudflare API Error ({response.StatusCode}): {json}");
                    return null;
                }

                var cfResponse = System.Text.Json.JsonSerializer.Deserialize<CloudflareResponse<List<Zone>>>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var zone = cfResponse?.Result?.FirstOrDefault();
                
                if (zone == null)
                {
                    _logger.LogWarning($"Cloudflare zone not found for domain: {domain}. Response: {json}");
                }

                return zone?.Id;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error getting Zone ID for {domain}");
                return null;
            }
        }

        public async Task<bool> CreateDnsRecordAsync(string zoneId, string type, string name, string content)
        {
            var record = new
            {
                type = type,
                name = name,
                content = content,
                ttl = 1, // Auto
                proxied = false
            };

            try
            {
                var response = await _httpClient.PostAsJsonAsync($"zones/{zoneId}/dns_records", record);
                if (response.IsSuccessStatusCode) return true;
                
                var error = await response.Content.ReadAsStringAsync();
                _logger.LogError($"Failed to add DNS record {name}: {error}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error creating DNS record {name}");
                return false;
            }
        }

        public async Task<bool> DeleteDnsRecordsForDomainAsync(string zoneId)
        {
            // 1. Delete DKIM (default._domainkey)
            await DeleteRecordByNameAndType(zoneId, "TXT", "default._domainkey");
            
            // 2. Delete DMARC (_dmarc)
            await DeleteRecordByNameAndType(zoneId, "TXT", "_dmarc");

            // 3. SPF (@) - Only delete if it looks like ours (v=spf1 ip4...)
            // This is trickier, so for safety we might skip automatic SPF deletion or check content.
            // For now, let's stick to safe specific records.
            
            return true;
        }

        private async Task DeleteRecordByNameAndType(string zoneId, string type, string name)
        {
            try
            {
                // List records
                var response = await _httpClient.GetAsync($"zones/{zoneId}/dns_records?type={type}&name={name}");
                var json = await response.Content.ReadAsStringAsync();
                var cfResponse = System.Text.Json.JsonSerializer.Deserialize<CloudflareResponse<List<DnsRecord>>>(json, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                
                if (cfResponse?.Result != null)
                {
                    foreach (var record in cfResponse.Result)
                    {
                         _logger.LogInformation($"Deleting DNS record {record.Name} ({record.Id})");
                         await _httpClient.DeleteAsync($"zones/{zoneId}/dns_records/{record.Id}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error deleting DNS record {name}");
            }
        }

        // Inner classes for JSON deserialization
        public class CloudflareResponse<T>
        {
            public T? Result { get; set; }
            public bool Success { get; set; }
            public List<ApiError>? Errors { get; set; }
        }

        public class ApiError
        {
            public int Code { get; set; }
            public string? Message { get; set; }
        }

        public class Zone
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
        }

        public class DnsRecord
        {
            public string? Id { get; set; }
            public string? Name { get; set; }
        }
    }
}
