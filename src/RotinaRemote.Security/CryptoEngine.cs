using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace RotinaRemote.Security
{
    public class CryptoEngine
    {
        public static (byte[] PrivateKey, byte[] PublicKey) GenerateKeyPair()
        {
            using var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
            var privateKey = ecdh.ExportECPrivateKey();
            var publicKey = ecdh.ExportSubjectPublicKeyInfo();
            return (privateKey, publicKey);
        }

        public static byte[] DeriveSharedSecret(byte[] myPrivateKey, byte[] peerPublicKey)
        {
            using var myEcdh = ECDiffieHellman.Create();
            myEcdh.ImportECPrivateKey(myPrivateKey, out _);

            using var peerEcdh = ECDiffieHellman.Create();
            peerEcdh.ImportSubjectPublicKeyInfo(peerPublicKey, out _);

            return myEcdh.DeriveKeyMaterial(peerEcdh.PublicKey);
        }

        public static byte[] EncryptAesGcm(byte[] plaintext, byte[] key, out byte[] nonce, out byte[] tag)
        {
            nonce = new byte[12]; // 96-bit nonce
            tag = new byte[16];   // 128-bit auth tag
            RandomNumberGenerator.Fill(nonce);

            var ciphertext = new byte[plaintext.Length];
            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            return ciphertext;
        }

        public static byte[] DecryptAesGcm(byte[] ciphertext, byte[] key, byte[] nonce, byte[] tag)
        {
            var plaintext = new byte[ciphertext.Length];
            using var aesGcm = new AesGcm(key, 16);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintext);
            return plaintext;
        }

        public static string ComputeSha256(byte[] data)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(data);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }

        public static string ComputeFileSha256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hash = sha256.ComputeHash(stream);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
        }
    }
}
