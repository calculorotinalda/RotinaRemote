using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using RotinaRemote.Core.Configuration;
using RotinaRemote.Core.Models;

namespace RotinaRemote.Core.Services
{
    public class LicenseService
    {
        public static LicenseService Instance { get; } = new LicenseService();

        // Chaves Mestre Oficiais de Teste e Avaliação Premium
        public const string MasterTestKey = "ROTINA-PREM-TEST-2026-A8B9-C1D2";
        public const string MasterTestKeySimple = "ROTINA-PREMIUM-TEST-KEY-2026";
        private const string SecretSalt = "ROTINA_REMOTE_PREMIUM_SALT_2026_ADM";

        public LicenseInfo CurrentLicense { get; private set; } = new LicenseInfo();
        public bool IsPremium => CurrentLicense.IsPremium;
        public event Action? LicenseChanged;

        public LicenseService()
        {
            CurrentLicense = CreateFreeLicense();
        }

        public void Initialize(string? configuredKey)
        {
            if (string.IsNullOrWhiteSpace(configuredKey))
            {
                CurrentLicense = CreateFreeLicense();
            }
            else
            {
                var val = ValidateKey(configuredKey.Trim());
                if (val.IsValid)
                {
                    CurrentLicense = val.License;
                }
                else
                {
                    CurrentLicense = CreateFreeLicense();
                }
            }
            LicenseChanged?.Invoke();
        }

        public (bool Success, string Message) ActivateLicense(string key, AppConfig? config = null)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, "Por favor introduza uma chave de licença válida.");
            }

            string cleanKey = key.Trim();
            var val = ValidateKey(cleanKey);
            if (!val.IsValid)
            {
                return (false, "Chave de licença inválida. Verifique o código e tente novamente.");
            }

            CurrentLicense = val.License;
            if (config != null)
            {
                config.LicenseKey = cleanKey;
                config.Save();
            }

            LicenseChanged?.Invoke();
            return (true, "Licença Premium ativada com sucesso! Todas as funcionalidades foram desbloqueadas.");
        }

        public void DeactivateLicense(AppConfig? config = null)
        {
            CurrentLicense = CreateFreeLicense();
            if (config != null)
            {
                config.LicenseKey = string.Empty;
                config.Save();
            }
            LicenseChanged?.Invoke();
        }

        public (bool IsValid, LicenseInfo License) ValidateKey(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return (false, CreateFreeLicense());
            }

            string k = key.Trim().ToUpperInvariant();

            // 1. Chaves Mestre Oficiais de Teste
            if (k == MasterTestKey || k == MasterTestKeySimple)
            {
                return (true, new LicenseInfo
                {
                    LicenseKey = k,
                    Plan = "Premium",
                    LicensedTo = "Administrador / Avaliação Premium",
                    ValidUntil = null, // Vitalícia
                    Message = "Licença de Teste Premium Vitalícia Ativa."
                });
            }

            // 2. Validação Algorítmica de Chaves Criptográficas
            // Formato esperado: ROTINA-PREM-{PARTE1}-{PARTE2}-{CHECKSUM}
            var parts = k.Split('-');
            if (parts.Length >= 4 && (parts[0] == "ROTINA" && (parts[1] == "PREM" || parts[1] == "PREMIUM")))
            {
                string checksum = parts[parts.Length - 1];
                string payload = string.Join("-", parts, 2, parts.Length - 3);
                string expectedChecksum = ComputeChecksum(payload);

                if (string.Equals(checksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
                {
                    return (true, new LicenseInfo
                    {
                        LicenseKey = k,
                        Plan = "Premium",
                        LicensedTo = "Cliente Licenciado (" + payload + ")",
                        ValidUntil = null,
                        Message = "Licença Premium Válida e Desbloqueada."
                    });
                }
            }

            return (false, CreateFreeLicense());
        }

        public static string GenerateKey(string payloadCode = "2026-ADM1")
        {
            string cleanPayload = payloadCode.Replace("-", "").ToUpperInvariant();
            string checksum = ComputeChecksum(cleanPayload);
            return $"ROTINA-PREM-{cleanPayload}-{checksum}";
        }

        private static string ComputeChecksum(string data)
        {
            using var sha256 = SHA256.Create();
            byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes($"{data}:{SecretSalt}"));
            return BitConverter.ToString(bytes).Replace("-", "").Substring(0, 4).ToUpperInvariant();
        }

        public static LicenseInfo CreateFreeLicense()
        {
            return new LicenseInfo
            {
                LicenseKey = string.Empty,
                Plan = "Free",
                LicensedTo = "Utilizador Gratuito (Versão Free)",
                ValidUntil = null,
                Message = "Versão Free: Sessões, Histórico, Diagnóstico e Modo de Depuração bloqueados."
            };
        }

        /// <summary>
        /// Método assíncrono preparado para a futura sincronização com o painel administrativo de licenças.
        /// </summary>
        public async Task<(bool Success, string Message)> SyncWithAdminPanelAsync(string adminApiUrl, string licenseKey, string deviceId)
        {
            try
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
                var requestObj = new
                {
                    Action = "ValidateLicense",
                    LicenseKey = licenseKey,
                    DeviceId = deviceId,
                    Timestamp = DateTime.UtcNow
                };

                var content = new StringContent(JsonSerializer.Serialize(requestObj), Encoding.UTF8, "application/json");
                var response = await client.PostAsync($"{adminApiUrl.TrimEnd('/')}/api/licenses/sync", content);

                if (response.IsSuccessStatusCode)
                {
                    return (true, "Licença validada e sincronizada com sucesso com o painel administrativo.");
                }

                return (false, $"O servidor administrativo respondeu com estado: {response.StatusCode}");
            }
            catch (Exception ex)
            {
                return (false, $"Não foi possível contactar o servidor administrativo: {ex.Message}");
            }
        }
    }
}
