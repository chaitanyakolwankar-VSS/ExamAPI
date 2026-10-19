using System.Security.Cryptography;
using System.Text;

namespace ExamAPI.Services.StudentAccess
{
    /// <summary>The random values of student access and their stored forms; the raw values are never saved.</summary>
    public interface IStudentAccessSecrets
    {
        /// <summary>A link token or session id: 32 random bytes, base64url (43 characters).</summary>
        string NewToken();
        /// <summary>SHA-256 of a token (tokens are random, so a plain hash is enough).</summary>
        byte[] Hash(string token);
        /// <summary>A 6-digit code.</summary>
        string NewCode();
        /// <summary>HMAC of a code with a server key, bound to its challenge (6 digits alone would be guessed from a bare hash).</summary>
        byte[] CodeHash(Guid challengeId, string code);
    }

    /// <summary>The code key is derived (HKDF-SHA256) from the server's Jwt:Key, like <see cref="Email.SecretProtector"/>.</summary>
    public sealed class StudentAccessSecrets : IStudentAccessSecrets
    {
        private readonly byte[] _codeKey;

        public StudentAccessSecrets(string serverKey)
        {
            if (string.IsNullOrWhiteSpace(serverKey))
                throw new ArgumentException("A server key is required.", nameof(serverKey));
            _codeKey = HKDF.DeriveKey(HashAlgorithmName.SHA256, Encoding.UTF8.GetBytes(serverKey), 32,
                info: Encoding.UTF8.GetBytes("GradeSphere.StudentOtp.v1"));
        }

        public string NewToken() =>
            Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

        public byte[] Hash(string token) => SHA256.HashData(Encoding.UTF8.GetBytes(token));

        public string NewCode() => RandomNumberGenerator.GetInt32(0, 1_000_000).ToString("D6");

        public byte[] CodeHash(Guid challengeId, string code) =>
            HMACSHA256.HashData(_codeKey, Encoding.UTF8.GetBytes($"{challengeId:N}:{code}"));
    }
}
