using System;
using System.Text;
using RotinaRemote.Core.Models;
using RotinaRemote.Security;
using Xunit;

namespace RotinaRemote.UnitTests
{
    public class SecurityTests
    {
        [Fact]
        public void DeviceId_ValidRaw_ShouldFormatCorrectly()
        {
            var deviceId = new DeviceId("482731905");
            Assert.Equal("482 731 905", deviceId.Formatted);
        }

        [Fact]
        public void DeviceId_InvalidLength_ShouldThrowException()
        {
            Assert.Throws<ArgumentException>(() => new DeviceId("12345"));
        }

        [Fact]
        public void CryptoEngine_AesGcmEncryptDecrypt_ShouldMatchOriginal()
        {
            var key = new byte[32];
            new Random().NextBytes(key);

            var plaintext = Encoding.UTF8.GetBytes("Segredo Altamente Confidencial");

            var ciphertext = CryptoEngine.EncryptAesGcm(plaintext, key, out var nonce, out var tag);
            Assert.NotNull(ciphertext);

            var decrypted = CryptoEngine.DecryptAesGcm(ciphertext, key, nonce, tag);
            Assert.Equal(plaintext, decrypted);
        }
    }
}
