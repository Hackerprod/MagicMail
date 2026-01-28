using System;
using System.Net;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using DnsClient;

namespace MagicMail.Services
{
    /// <summary>
    /// Detects the outbound identity of the server (public IPv4 + PTR hostname) for HELO/EHLO usage.
    /// </summary>
    public class ServerIdentityResolver
    {
        private static readonly IPAddress[] OpenDnsResolvers = new[]
        {
            IPAddress.Parse("208.67.222.222"),
            IPAddress.Parse("208.67.220.220")
        };

        private readonly ILogger<ServerIdentityResolver> _logger;
        private readonly LookupClient _systemLookupClient;
        private readonly LookupClient _publicLookupClient;
        private readonly SemaphoreSlim _mutex = new(1, 1);

        private bool _attempted;
        private string? _cachedPtrHostname;

        public ServerIdentityResolver(ILogger<ServerIdentityResolver> logger)
        {
            _logger = logger;
            _systemLookupClient = new LookupClient(); // Uses system resolvers for reverse lookups.
            _publicLookupClient = new LookupClient(new LookupClientOptions(OpenDnsResolvers)
            {
                ContinueOnDnsError = true,
                Recursion = true,
                Timeout = TimeSpan.FromSeconds(3),
                UseCache = true
            });
        }

        /// <summary>
        /// Returns the PTR hostname for the current public IPv4 address, caching the result.
        /// </summary>
        public async Task<string?> GetPtrHostnameAsync()
        {
            if (_attempted) return _cachedPtrHostname;

            await _mutex.WaitAsync();
            try
            {
                if (_attempted) return _cachedPtrHostname;

                _cachedPtrHostname = await ResolvePtrHostnameAsync();
                _attempted = true;
                return _cachedPtrHostname;
            }
            finally
            {
                _mutex.Release();
            }
        }

        private async Task<string?> ResolvePtrHostnameAsync()
        {
            var publicIpv4 = await GetPublicIpv4Async();
            if (publicIpv4 == null)
            {
                _logger.LogWarning("Could not determine the public IP; skipping PTR autodetection.");
                return null;
            }

            try
            {
                var response = await _systemLookupClient.QueryReverseAsync(publicIpv4);
                var ptr = response.Answers.PtrRecords()
                    .Select(ptr => ptr.PtrDomainName.Value.TrimEnd('.'))
                    .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value));

                if (string.IsNullOrEmpty(ptr))
                {
                    _logger.LogWarning("PTR lookup for {Ip} returned no records.", publicIpv4);
                }
                else
                {
                    _logger.LogInformation("Detected PTR hostname {Ptr} for IP {Ip}.", ptr, publicIpv4);
                }

                return ptr;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to resolve PTR for {Ip}.", publicIpv4);
                return null;
            }
        }

        private async Task<IPAddress?> GetPublicIpv4Async()
        {
            try
            {
                var response = await _publicLookupClient.QueryAsync("myip.opendns.com", QueryType.A);
                var record = response.Answers.ARecords().FirstOrDefault();
                if (record?.Address != null)
                {
                    return record.Address;
                }

                _logger.LogWarning("OpenDNS returned no A record for myip.opendns.com.");
                return null;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to query OpenDNS for the public IP.");
                return null;
            }
        }
    }
}
