using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Security
{
    /// <summary>
    /// Protects installation-local secrets with AES-GCM and a random installation key.
    /// The key is read from EXPORTDOCMANAGER_MASTER_KEY when supplied; otherwise it is
    /// created under the active runtime data root Security directory.
    /// </summary>
    public sealed class LocalSecretProtector
    {
        public const string MasterKeyEnvironmentVariable = "EXPORTDOCMANAGER_MASTER_KEY";
        public const string MasterKeyFileName = "local-master-key.bin";

        private const string PayloadPrefix = "edm-aesgcm-v1:";
        private const int KeySize = 32;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private static readonly object KeyFileGate = new();

        private readonly string _securityRoot;
        private readonly Lazy<byte[]> _key;

        public LocalSecretProtector(IAppPathProvider pathProvider)
            : this((pathProvider ?? throw new ArgumentNullException(nameof(pathProvider))).SecurityRoot)
        {
        }

        public LocalSecretProtector(string securityRoot)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(securityRoot);
            _securityRoot = Path.GetFullPath(securityRoot);
            _key = new Lazy<byte[]>(LoadOrCreateKey, LazyThreadSafetyMode.ExecutionAndPublication);
        }

        public string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
            {
                return plainText;
            }

            byte[] nonce = RandomNumberGenerator.GetBytes(NonceSize);
            byte[] plaintext = Encoding.UTF8.GetBytes(plainText);
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[TagSize];
            try
            {
                using (var aes = new AesGcm(_key.Value, TagSize))
                {
                    aes.Encrypt(nonce, plaintext, ciphertext, tag);
                }

                byte[] payload = new byte[NonceSize + TagSize + ciphertext.Length];
                Buffer.BlockCopy(nonce, 0, payload, 0, NonceSize);
                Buffer.BlockCopy(tag, 0, payload, NonceSize, TagSize);
                Buffer.BlockCopy(ciphertext, 0, payload, NonceSize + TagSize, ciphertext.Length);
                return PayloadPrefix + Convert.ToBase64String(payload);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
            }
        }

        public string Unprotect(string protectedText)
        {
            if (string.IsNullOrEmpty(protectedText))
            {
                return protectedText;
            }

            if (!protectedText.StartsWith(PayloadPrefix, StringComparison.Ordinal))
            {
                return null;
            }

            try
            {
                byte[] payload = Convert.FromBase64String(protectedText[PayloadPrefix.Length..]);
                if (payload.Length < NonceSize + TagSize)
                {
                    return null;
                }

                int ciphertextLength = payload.Length - NonceSize - TagSize;
                byte[] plaintext = new byte[ciphertextLength];
                try
                {
                    using (var aes = new AesGcm(_key.Value, TagSize))
                    {
                        aes.Decrypt(
                            payload.AsSpan(0, NonceSize),
                            payload.AsSpan(NonceSize + TagSize, ciphertextLength),
                            payload.AsSpan(NonceSize, TagSize),
                            plaintext);
                    }

                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (Exception ex) when (ex is FormatException or CryptographicException)
            {
                return null;
            }
        }

        private byte[] LoadOrCreateKey()
        {
            string configuredKey = Environment.GetEnvironmentVariable(MasterKeyEnvironmentVariable) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(configuredKey))
            {
                return ParseConfiguredKey(configuredKey);
            }

            Directory.CreateDirectory(_securityRoot);
            string keyPath = Path.Combine(_securityRoot, MasterKeyFileName);
            lock (KeyFileGate)
            {
                if (File.Exists(keyPath))
                {
                    return ReadKeyFileWithRetry(keyPath);
                }

                byte[] key = RandomNumberGenerator.GetBytes(KeySize);
                try
                {
                    using var stream = new FileStream(
                        keyPath,
                        FileMode.CreateNew,
                        FileAccess.Write,
                        FileShare.None,
                        4096,
                        FileOptions.WriteThrough);
                    stream.Write(key);
                    stream.Flush(flushToDisk: true);
                    RestrictKeyFilePermissions(keyPath);
                    return key;
                }
                catch (IOException) when (File.Exists(keyPath))
                {
                    CryptographicOperations.ZeroMemory(key);
                    return ReadKeyFileWithRetry(keyPath);
                }
            }
        }

        private static byte[] ParseConfiguredKey(string configuredKey)
        {
            string value = configuredKey.Trim();
            byte[] key;
            try
            {
                key = value.Length == KeySize * 2 && value.All(Uri.IsHexDigit)
                    ? Convert.FromHexString(value)
                    : Convert.FromBase64String(value);
            }
            catch (FormatException ex)
            {
                throw new InvalidOperationException(
                    $"{MasterKeyEnvironmentVariable} 必须是 32 字节密钥的 Base64 或 64 位十六进制文本。",
                    ex);
            }

            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidOperationException(
                    $"{MasterKeyEnvironmentVariable} 解码后必须恰好为 {KeySize} 字节。");
            }

            return key;
        }

        private static byte[] ReadKeyFile(string path)
        {
            byte[] key = File.ReadAllBytes(path);
            if (key.Length != KeySize)
            {
                CryptographicOperations.ZeroMemory(key);
                throw new InvalidDataException($"本机密钥文件长度无效：{path}");
            }

            return key;
        }

        private static byte[] ReadKeyFileWithRetry(string path)
        {
            const int maximumAttempts = 10;
            for (int attempt = 1; attempt < maximumAttempts; attempt++)
            {
                try
                {
                    return ReadKeyFile(path);
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException)
                {
                    Thread.Sleep(20);
                }
            }

            return ReadKeyFile(path);
        }

        private static void RestrictKeyFilePermissions(string path)
        {
            if (OperatingSystem.IsWindows())
            {
                return;
            }

            try
            {
                File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException)
            {
                // The containing runtime directory may already be protected by the deployment.
            }
        }
    }
}
