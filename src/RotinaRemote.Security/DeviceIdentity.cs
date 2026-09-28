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
        public string RawId { get; private set; } = string.Empty;

        private static string GetIdentityFilePath(bool forWriting = false)
        {
            string localFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "identity.dat");
            if (File.Exists(localFile))
            {
                return localFile;
            }

            try
            {
                string localAppData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "RotinaRemote");
                string appDataFile = Path.Combine(localAppData, "identity.dat");
                if (File.Exists(appDataFile))
                {
                    return appDataFile;
                }

                if (forWriting)
                {
                    if (!Directory.Exists(localAppData))
                    {
                        Directory.CreateDirectory(localAppData);
                    }
                    return appDataFile;
                }
            }
            catch
            {
                // Fallback para pasta local se falhar acesso a AppData
            }

            return localFile;
        }

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
                var filePath = GetIdentityFilePath(forWriting: false);
                if (File.Exists(filePath))
                {
                    var encryptedBytes = File.ReadAllBytes(filePath);
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
                var filePath = GetIdentityFilePath(forWriting: true);
                File.WriteAllBytes(filePath, encryptedBytes);
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
