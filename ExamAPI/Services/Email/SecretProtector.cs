using System.Security.Cryptography;
using System.Text;

namespace ExamAPI.Services.Email
{
    /// <summary>Encrypts small secrets (mail account passwords) stored in the database.</summary>
    public interface ISecretProtector
    {
        string Protect(string plain);
        /// <summary>Throws <see cref="CryptographicException"/> when the value was changed or the server key is different.</summary>
        string Unprotect(string protectedValue);
    }

    /// <summary>
    /// AES-256-GCM with a key derived (HKDF-SHA256) from the server's Jwt:Key, which every install already has and
    /// keeps out of the repo: nothing new to configure. Changing Jwt:Key makes stored passwords unreadable; the
    /// platform admin then types them in again. Format: "v1." + base64(nonce | ciphertext | tag).
    /// </summary>
    public sealed class SecretProtector : ISecretProtector
    {
        private const string Version = "v1.";
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private readonly byte[] _key;

        public SecretProtector(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                throw new ArgumentException("A server key is required.", nameof(serverKey));
            _key = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(serverKey), 32,
                info: Encoding.UTF8.GetBytes("GradeSphere.EmailAccountPassword.v1"));
        }

        public string Protect(string plain)
        {
            var data = Encoding.UTF8.GetBytes(plain);
            var output = new byte[NonceSize + data.Length + TagSize];
            var nonce = output.AsSpan(0, NonceSize);
            RandomNumberGenerator.Fill(nonce);
            using var aes = new AesGcm(_key, TagSize);
            aes.Encrypt(nonce, data, output.AsSpan(NonceSize, data.Length), output.AsSpan(NonceSize + data.Length, TagSize));
            return Version + Convert.ToBase64String(output);
        }

        public string Unprotect(string protectedValue)
        {
            if (!protectedValue.StartsWith(Version, StringComparison.Ordinal))
                throw new CryptographicException("Unknown secret format.");
            var input = Convert.FromBase64String(protectedValue[Version.Length..]);
            if (input.Length < NonceSize + TagSize)
                throw new CryptographicException("Secret is too short.");
            var length = input.Length - NonceSize - TagSize;
            var plain = new byte[length];
            using var aes = new AesGcm(_key, TagSize);
            aes.Decrypt(input.AsSpan(0, NonceSize), input.AsSpan(NonceSize, length), input.AsSpan(NonceSize + length, TagSize), plain);
            return Encoding.UTF8.GetString(plain);
        }
    }
}
