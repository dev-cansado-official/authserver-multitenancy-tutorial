using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace EvoAuth.Api.Authorization
{
    public sealed class AuthServerPermissionService : IPermissionService
    {
        private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

        private readonly IMemoryCache _cache;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IConfiguration _configuration;

        public AuthServerPermissionService(
            IMemoryCache cache,
            IHttpClientFactory httpClientFactory,
            IConfiguration configuration)
        {
            _cache = cache;
            _httpClientFactory = httpClientFactory;
            _configuration = configuration;
        }

        public async Task<bool> HasPermissionAsync(
            ClaimsPrincipal user,
            Guid tenantId,
            string requiredPermission,
            string accessToken,
            CancellationToken ct)
        {
            var sub = user.FindFirst("sub")?.Value;
            var clientId = user.FindFirst("client_id")?.Value;

            if (string.IsNullOrWhiteSpace(sub) || string.IsNullOrWhiteSpace(clientId))
                return false;

            var cacheKey = $"perm:{sub}:{tenantId}:{clientId}";
            if (!_cache.TryGetValue<HashSet<string>>(cacheKey, out var permissions))
            {
                permissions = await FetchPermissionsAsync(tenantId, accessToken, ct);
                _cache.Set(cacheKey, permissions, TimeSpan.FromMinutes(1));
            }

            permissions ??= new HashSet<string>(StringComparer.Ordinal);
            return permissions.Contains(requiredPermission);
        }

        private async Task<HashSet<string>> FetchPermissionsAsync(Guid tenantId, string accessToken, CancellationToken ct)
        {
            var authority = _configuration["Auth:Authority"];
            if (string.IsNullOrWhiteSpace(authority))
                return new HashSet<string>(StringComparer.Ordinal);

            var baseAddress = authority.EndsWith("/", StringComparison.Ordinal) ? authority : $"{authority}/";
            var client = _httpClientFactory.CreateClient("authserver-permissions");
            client.BaseAddress = new Uri(baseAddress, UriKind.Absolute);

            using var request = new HttpRequestMessage(HttpMethod.Get, $"tenants/{tenantId}/me/permissions");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);

            using var response = await client.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return new HashSet<string>(StringComparer.Ordinal);

            await using var stream = await response.Content.ReadAsStreamAsync(ct);
            var payload = await JsonSerializer.DeserializeAsync<PermissionsPayload>(stream, JsonOptions, ct);

            return payload?.Permissions is null
                ? new HashSet<string>(StringComparer.Ordinal)
                : payload.Permissions.ToHashSet(StringComparer.Ordinal);
        }

        private sealed class PermissionsPayload
        {
            public List<string> Permissions { get; set; } = new();
        }
    }
}
