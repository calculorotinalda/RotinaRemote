using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using RotinaRemote.Core.Logging;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Core.Services
{
    public static class GeoLocationService
    {
        private static GeoLocationInfo? _cachedInfo;
        private static readonly HttpClient _httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };

        public static async Task<GeoLocationInfo> GetGeoLocationAsync(string? callerDeviceId = null, CancellationToken ct = default)
        {
            if (_cachedInfo != null && !string.IsNullOrWhiteSpace(_cachedInfo.City))
            {
                var cached = new GeoLocationInfo
                {
                    Ip = _cachedInfo.Ip,
                    City = _cachedInfo.City,
                    Country = _cachedInfo.Country,
                    Location = _cachedInfo.Location,
                    CallerDeviceId = callerDeviceId ?? _cachedInfo.CallerDeviceId
                };
                return cached;
            }

            var info = new GeoLocationInfo
            {
                CallerDeviceId = callerDeviceId ?? string.Empty,
                Ip = "Desconhecido",
                City = "Desconhecida",
                Country = "Desconhecido",
                Location = "Localização Desconhecida"
            };

            // Provider 1: ip-api.com
            try
            {
                using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts.CancelAfter(2500);

                var response = await _httpClient.GetStringAsync("http://ip-api.com/json/", cts.Token);
                using var doc = JsonDocument.Parse(response);
                var root = doc.RootElement;

                if (root.TryGetProperty("status", out var statusElem) && statusElem.GetString() == "success")
                {
                    string ip = root.TryGetProperty("query", out var q) ? q.GetString() ?? "" : "";
                    string city = root.TryGetProperty("city", out var c) ? c.GetString() ?? "" : "";
                    string country = root.TryGetProperty("country", out var cntry) ? cntry.GetString() ?? "" : "";
                    string region = root.TryGetProperty("regionName", out var r) ? r.GetString() ?? "" : "";

                    info.Ip = !string.IsNullOrWhiteSpace(ip) ? ip : "Desconhecido";
                    info.City = !string.IsNullOrWhiteSpace(city) ? city : "Desconhecida";
                    info.Country = !string.IsNullOrWhiteSpace(country) ? country : "Desconhecido";
                    
                    if (!string.IsNullOrWhiteSpace(city) && !string.IsNullOrWhiteSpace(country))
                    {
                        info.Location = !string.IsNullOrWhiteSpace(region) && !region.Equals(city, StringComparison.OrdinalIgnoreCase)
                            ? $"{city} ({region}), {country}"
                            : $"{city}, {country}";
                    }
                    else
                    {
                        info.Location = country;
                    }

                    _cachedInfo = info;
                    AppLogger.LogInfo("GeoLocationService", $"Localização obtida com sucesso: IP={info.Ip}, Cidade={info.City}, País={info.Country}");
                    return info;
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("GeoLocationService", $"Provider 1 (ip-api.com) falhou: {ex.Message}");
            }

            // Provider 2: ipapi.co
            try
            {
                using var cts2 = CancellationTokenSource.CreateLinkedTokenSource(ct);
                cts2.CancelAfter(2500);

                var response2 = await _httpClient.GetStringAsync("https://ipapi.co/json/", cts2.Token);
                using var doc2 = JsonDocument.Parse(response2);
                var root2 = doc2.RootElement;

                string ip2 = root2.TryGetProperty("ip", out var q2) ? q2.GetString() ?? "" : "";
                string city2 = root2.TryGetProperty("city", out var c2) ? c2.GetString() ?? "" : "";
                string country2 = root2.TryGetProperty("country_name", out var cntry2) ? cntry2.GetString() ?? "" : "";
                string region2 = root2.TryGetProperty("region", out var r2) ? r2.GetString() ?? "" : "";

                info.Ip = !string.IsNullOrWhiteSpace(ip2) ? ip2 : info.Ip;
                info.City = !string.IsNullOrWhiteSpace(city2) ? city2 : info.City;
                info.Country = !string.IsNullOrWhiteSpace(country2) ? country2 : info.Country;

                if (!string.IsNullOrWhiteSpace(city2) && !string.IsNullOrWhiteSpace(country2))
                {
                    info.Location = $"{city2}, {country2}";
                }

                _cachedInfo = info;
                AppLogger.LogInfo("GeoLocationService", $"Localização obtida via Provider 2: IP={info.Ip}, Cidade={info.City}, País={info.Country}");
                return info;
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning("GeoLocationService", $"Provider 2 (ipapi.co) falhou: {ex.Message}");
            }

            _cachedInfo = info;
            return info;
        }
    }
}
