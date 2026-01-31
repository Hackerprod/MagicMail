using DnsClient;
using DnsClient.Protocol;
using MagicMail.Data;

namespace MagicMail.Services
{
    public class DnsVerificationResult
    {
        public bool SpfValid { get; set; }
        public bool DkimValid { get; set; }
        public bool DmarcValid { get; set; }
        public bool MxValid { get; set; }
        public bool AllValid => SpfValid && DkimValid && DmarcValid && MxValid;
        
        public string? SpfRecord { get; set; }
        public string? DkimRecord { get; set; }
        public string? DmarcRecord { get; set; }
        public string? MxRecord { get; set; }
        
        public List<string> Issues { get; set; } = new();
    }

    public class DnsVerificationService
    {
        private readonly ILogger<DnsVerificationService> _logger;
        private readonly ILookupClient _dnsClient;

        public DnsVerificationService(ILogger<DnsVerificationService> logger)
        {
            _logger = logger;
            _dnsClient = new LookupClient(new LookupClientOptions
            {
                UseCache = false,
                Timeout = TimeSpan.FromSeconds(5)
            });
        }

        public async Task<DnsVerificationResult> VerifyDomainAsync(Domain domain, string expectedServerIp)
        {
            var result = new DnsVerificationResult();
            var domainName = domain.DomainName;

            // 1. Verify SPF
            try
            {
                var spfRecords = await _dnsClient.QueryAsync(domainName, QueryType.TXT);
                var spfTxt = spfRecords.Answers.TxtRecords()
                    .SelectMany(t => t.Text)
                    .FirstOrDefault(t => t.StartsWith("v=spf1"));

                if (!string.IsNullOrEmpty(spfTxt))
                {
                    result.SpfRecord = spfTxt;
                    // Check if our IP is authorized
                    result.SpfValid = spfTxt.Contains(expectedServerIp) || spfTxt.Contains("mx");
                    if (!result.SpfValid)
                        result.Issues.Add($"SPF exists but doesn't include IP {expectedServerIp}");
                }
                else
                {
                    result.Issues.Add("SPF record not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify SPF for {Domain}", domainName);
                result.Issues.Add($"SPF lookup failed: {ex.Message}");
            }

            // 2. Verify DKIM
            try
            {
                var dkimHost = $"{domain.DkimSelector}._domainkey.{domainName}";
                var dkimRecords = await _dnsClient.QueryAsync(dkimHost, QueryType.TXT);
                var dkimTxt = dkimRecords.Answers.TxtRecords()
                    .SelectMany(t => t.Text)
                    .FirstOrDefault(t => t.Contains("v=DKIM1"));

                if (!string.IsNullOrEmpty(dkimTxt))
                {
                    result.DkimRecord = dkimTxt;
                    // Extract the public key from our domain config and check if it matches
                    var ourKey = domain.DkimPublicKey
                        .Replace("-----BEGIN PUBLIC KEY-----", "")
                        .Replace("-----END PUBLIC KEY-----", "")
                        .Replace("\r", "").Replace("\n", "").Trim();
                    
                    result.DkimValid = dkimTxt.Contains(ourKey.Substring(0, Math.Min(50, ourKey.Length)));
                    if (!result.DkimValid)
                        result.Issues.Add("DKIM exists but public key doesn't match");
                }
                else
                {
                    result.Issues.Add($"DKIM record not found at {dkimHost}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify DKIM for {Domain}", domainName);
                result.Issues.Add($"DKIM lookup failed: {ex.Message}");
            }

            // 3. Verify DMARC
            try
            {
                var dmarcHost = $"_dmarc.{domainName}";
                var dmarcRecords = await _dnsClient.QueryAsync(dmarcHost, QueryType.TXT);
                var dmarcTxt = dmarcRecords.Answers.TxtRecords()
                    .SelectMany(t => t.Text)
                    .FirstOrDefault(t => t.StartsWith("v=DMARC1"));

                if (!string.IsNullOrEmpty(dmarcTxt))
                {
                    result.DmarcRecord = dmarcTxt;
                    result.DmarcValid = true;
                }
                else
                {
                    result.Issues.Add("DMARC record not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify DMARC for {Domain}", domainName);
                result.Issues.Add($"DMARC lookup failed: {ex.Message}");
            }

            // 4. Verify MX
            try
            {
                var mxRecords = await _dnsClient.QueryAsync(domainName, QueryType.MX);
                var mxRecord = mxRecords.Answers.MxRecords().FirstOrDefault();

                if (mxRecord != null)
                {
                    result.MxRecord = mxRecord.Exchange.Value;
                    // Verify that MX points to our server (resolve and check IP)
                    var mxIp = await _dnsClient.QueryAsync(mxRecord.Exchange, QueryType.A);
                    var resolvedIp = mxIp.Answers.ARecords().FirstOrDefault()?.Address.ToString();
                    
                    result.MxValid = resolvedIp == expectedServerIp;
                    if (!result.MxValid && resolvedIp != null)
                        result.Issues.Add($"MX resolves to {resolvedIp}, expected {expectedServerIp}");
                    else if (resolvedIp == null)
                        result.Issues.Add($"MX {mxRecord.Exchange.Value} doesn't resolve to an IP");
                }
                else
                {
                    result.Issues.Add("MX record not found");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to verify MX for {Domain}", domainName);
                result.Issues.Add($"MX lookup failed: {ex.Message}");
            }

            _logger.LogInformation("DNS Verification for {Domain}: SPF={Spf}, DKIM={Dkim}, DMARC={Dmarc}, MX={Mx}",
                domainName, result.SpfValid, result.DkimValid, result.DmarcValid, result.MxValid);

            return result;
        }
    }
}
