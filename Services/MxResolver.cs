using DnsClient;

namespace MagicMail.Services
{
    public class MxResolver
    {
        private readonly LookupClient _dnsClient;
        private readonly ILogger<MxResolver> _logger;

        public MxResolver(ILogger<MxResolver> logger)
        {
            _logger = logger;
            _dnsClient = new LookupClient(); 
        }

        public async Task<List<string>> GetMxRecordsAsync(string domain)
        {
            try
            {
                var result = await _dnsClient.QueryAsync(domain, QueryType.MX);
                
                // Order by preference (lower is higher priority) and select exchange
                var mxRecords = result.Answers
                    .MxRecords()
                    .OrderBy(mx => mx.Preference)
                    .Select(mx => mx.Exchange.Value)
                    .ToList();

                if (!mxRecords.Any()) 
                {
                    _logger.LogWarning($"No MX records found for {domain}. Fallback to A record not implemented.");
                }

                return mxRecords;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, $"Error resolving MX records for {domain}");
                return new List<string>();
            }
        }
    }
}
