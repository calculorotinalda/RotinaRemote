using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;

namespace RotinaRemote.Security
{
    [SupportedOSPlatform("windows")]
    public class DeviceIdentity
    {
        private static readonly string IdentityFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "identity.dat");

        public string RawId { get; private set; } = string.Empty;

        public string FormattedId
        {
            get
            {
                if (RawId.Length == 9)
                {
                    return $"{RawId.Substring(0, 3)} {RawId.Substring(3, 3)} {RawId.Substring(6, 3)}";
                }
                return RawId;
            }
        }

        public static DeviceIdentity LoadOrCreate()
        {
            var identity = new DeviceIdentity();
            identity.Initialize();
            return identity;
        }

        private void Initialize()
        {
            try
            {
                if (File.Exists(IdentityFilePath))
                {
                    var encryptedBytes = File.ReadAllBytes(IdentityFilePath);
                    var decryptedBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser);
                    var savedId = Encoding.UTF8.GetString(decryptedBytes).Trim();
                    if (savedId.Length == 9 && long.TryParse(savedId, out _))
                    {
                        RawId = savedId;
                        return;
                    }
                }
            }
            catch
            {
                // Fallback para geração se falhar a leitura
            }

            // Gerar novo ID único baseado em GUID e Hash local
            RawId = GenerateUniqueId();
            SaveIdentity();
        }

        public void Regenerate()
        {
            RawId = GenerateUniqueId();
            SaveIdentity();
        }

        private void SaveIdentity()
        {
            try
            {
                var bytes = Encoding.UTF8.GetBytes(RawId);
                var encryptedBytes = ProtectedData.Protect(bytes, null, DataProtectionScope.CurrentUser);
                File.WriteAllBytes(IdentityFilePath, encryptedBytes);
            }
            catch
            {
                // Ignorar erro ao guardar se o ambiente for restrito
            }
        }

        private static string GenerateUniqueId()
        {
            var machineSeed = $"{Environment.MachineName}-{Environment.UserName}-{Environment.ProcessorCount}-{Environment.OSVersion}";
            using var sha = SHA256.Create();
            var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(machineSeed + Guid.NewGuid().ToString()));
            
            // Extrair valor numérico de 9 dígitos (ex: 100000000 a 999999999)
            var number = BitConverter.ToUInt32(hash, 0) % 900_000_000 + 100_000_000;
            return number.ToString();
        }
    }
}
