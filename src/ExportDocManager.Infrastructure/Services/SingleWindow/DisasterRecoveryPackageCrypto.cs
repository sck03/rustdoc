using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace ExportDocManager.Services.SingleWindow
{
    internal static class DisasterRecoveryPackageCrypto
    {
        private static readonly byte[] Magic = "EDMDRP01"u8.ToArray();
        private static readonly JsonSerializerOptions JsonOptions = JsonSerializerOptions.Web;
        private const int SaltSize = 16;
        private const int NoncePrefixSize = 8;
        private const int NonceSize = 12;
        private const int TagSize = 16;
        private const int KeySize = 32;
        private const int ChunkSize = 1024 * 1024;
        private const int Pbkdf2Iterations = 600_000;
        private const int MaximumHeaderBytes = 16 * 1024;
        internal const long MaximumPlaintextBytes = 4L * 1024L * 1024L * 1024L + 16L * 1024L * 1024L;

        public static async Task EncryptAsync(
            string plaintextPath,
            string packagePath,
            string password,
            CancellationToken cancellationToken = default)
        {
            ValidatePassword(password);
            var plaintextInfo = new FileInfo(plaintextPath);
            if (!plaintextInfo.Exists || plaintextInfo.Length <= 0 || plaintextInfo.Length > MaximumPlaintextBytes)
            {
                throw new InvalidDataException("待加密恢复数据大小无效。");
            }

            byte[] salt = RandomNumberGenerator.GetBytes(SaltSize);
            byte[] noncePrefix = RandomNumberGenerator.GetBytes(NoncePrefixSize);
            byte[] key = DeriveKey(password, salt, Pbkdf2Iterations);
            byte[] plaintext = new byte[ChunkSize];
            byte[] ciphertext = new byte[ChunkSize];
            byte[] tag = new byte[TagSize];
            byte[] nonce = new byte[NonceSize];
            try
            {
                var header = new PackageHeader(
                    SchemaVersion: 1,
                    Algorithm: "AES-256-GCM-CHUNKED",
                    Kdf: "PBKDF2-SHA256",
                    Pbkdf2Iterations,
                    Convert.ToBase64String(salt),
                    Convert.ToBase64String(noncePrefix),
                    ChunkSize,
                    plaintextInfo.Length,
                    DateTimeOffset.UtcNow);
                byte[] headerBytes = JsonSerializer.SerializeToUtf8Bytes(header, JsonOptions);
                byte[] headerHash = SHA256.HashData(headerBytes);

                await using var input = new FileStream(
                    plaintextPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.Read,
                    ChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                await using var output = new FileStream(
                    packagePath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    ChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
                await output.WriteAsync(Magic, cancellationToken).ConfigureAwait(false);
                await WriteInt32Async(output, headerBytes.Length, cancellationToken).ConfigureAwait(false);
                await output.WriteAsync(headerBytes, cancellationToken).ConfigureAwait(false);

                using var aes = new AesGcm(key, TagSize);
                uint chunkIndex = 0;
                long remaining = plaintextInfo.Length;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int requested = (int)Math.Min(ChunkSize, remaining);
                    await input.ReadExactlyAsync(plaintext.AsMemory(0, requested), cancellationToken)
                        .ConfigureAwait(false);
                    BuildNonce(noncePrefix, chunkIndex, nonce);
                    byte[] associatedData = BuildAssociatedData(headerHash, chunkIndex, requested);
                    aes.Encrypt(
                        nonce,
                        plaintext.AsSpan(0, requested),
                        ciphertext.AsSpan(0, requested),
                        tag,
                        associatedData);
                    await WriteInt32Async(output, requested, cancellationToken).ConfigureAwait(false);
                    await output.WriteAsync(ciphertext.AsMemory(0, requested), cancellationToken)
                        .ConfigureAwait(false);
                    await output.WriteAsync(tag, cancellationToken).ConfigureAwait(false);
                    CryptographicOperations.ZeroMemory(associatedData);
                    remaining -= requested;
                    chunkIndex = checked(chunkIndex + 1);
                }

                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(noncePrefix);
            }
        }

        public static async Task DecryptAsync(
            string packagePath,
            string plaintextPath,
            string password,
            CancellationToken cancellationToken = default)
        {
            ValidatePassword(password);
            await using var input = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                ChunkSize,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] magic = new byte[Magic.Length];
            await input.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(magic, Magic))
            {
                throw new InvalidDataException("不是受支持的灾难恢复包。");
            }

            int headerLength = await ReadInt32Async(input, cancellationToken).ConfigureAwait(false);
            if (headerLength <= 0 || headerLength > MaximumHeaderBytes)
            {
                throw new InvalidDataException("灾难恢复包头长度无效。");
            }
            byte[] headerBytes = new byte[headerLength];
            await input.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            PackageHeader header;
            try
            {
                header = JsonSerializer.Deserialize<PackageHeader>(headerBytes, JsonOptions)
                    ?? throw new InvalidDataException("灾难恢复包头为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("灾难恢复包头格式无效。", ex);
            }
            ValidateHeader(header);

            byte[] salt;
            byte[] noncePrefix;
            try
            {
                salt = Convert.FromBase64String(header.Salt);
                noncePrefix = Convert.FromBase64String(header.NoncePrefix);
            }
            catch (FormatException ex)
            {
                throw new InvalidDataException("灾难恢复包加密参数无效。", ex);
            }
            if (salt.Length != SaltSize || noncePrefix.Length != NoncePrefixSize)
            {
                throw new InvalidDataException("灾难恢复包加密参数长度无效。");
            }

            byte[] key = DeriveKey(password, salt, header.Iterations);
            byte[] ciphertext = new byte[header.ChunkSize];
            byte[] plaintext = new byte[header.ChunkSize];
            byte[] tag = new byte[TagSize];
            byte[] nonce = new byte[NonceSize];
            byte[] headerHash = SHA256.HashData(headerBytes);
            try
            {
                await using var output = new FileStream(
                    plaintextPath,
                    FileMode.CreateNew,
                    FileAccess.Write,
                    FileShare.None,
                    header.ChunkSize,
                    FileOptions.Asynchronous | FileOptions.SequentialScan | FileOptions.WriteThrough);
                using var aes = new AesGcm(key, TagSize);
                uint chunkIndex = 0;
                long remaining = header.PlaintextLength;
                while (remaining > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    int chunkLength = await ReadInt32Async(input, cancellationToken).ConfigureAwait(false);
                    int expectedLength = (int)Math.Min(header.ChunkSize, remaining);
                    if (chunkLength != expectedLength)
                    {
                        throw new InvalidDataException("灾难恢复包分块长度无效。");
                    }
                    await input.ReadExactlyAsync(ciphertext.AsMemory(0, chunkLength), cancellationToken)
                        .ConfigureAwait(false);
                    await input.ReadExactlyAsync(tag, cancellationToken).ConfigureAwait(false);
                    BuildNonce(noncePrefix, chunkIndex, nonce);
                    byte[] associatedData = BuildAssociatedData(headerHash, chunkIndex, chunkLength);
                    try
                    {
                        aes.Decrypt(
                            nonce,
                            ciphertext.AsSpan(0, chunkLength),
                            tag,
                            plaintext.AsSpan(0, chunkLength),
                            associatedData);
                    }
                    catch (AuthenticationTagMismatchException ex)
                    {
                        throw new InvalidDataException("灾难恢复包密码错误或包已损坏。", ex);
                    }
                    finally
                    {
                        CryptographicOperations.ZeroMemory(associatedData);
                    }
                    await output.WriteAsync(plaintext.AsMemory(0, chunkLength), cancellationToken)
                        .ConfigureAwait(false);
                    remaining -= chunkLength;
                    chunkIndex = checked(chunkIndex + 1);
                }
                if (input.Position != input.Length)
                {
                    throw new InvalidDataException("灾难恢复包尾部包含未认证数据。");
                }
                await output.FlushAsync(cancellationToken).ConfigureAwait(false);
                output.Flush(flushToDisk: true);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                CryptographicOperations.ZeroMemory(ciphertext);
                CryptographicOperations.ZeroMemory(plaintext);
                CryptographicOperations.ZeroMemory(tag);
                CryptographicOperations.ZeroMemory(nonce);
                CryptographicOperations.ZeroMemory(salt);
                CryptographicOperations.ZeroMemory(noncePrefix);
                CryptographicOperations.ZeroMemory(headerHash);
                CryptographicOperations.ZeroMemory(headerBytes);
            }
        }

        internal static async Task<long> ReadDeclaredPlaintextLengthAsync(
            string packagePath,
            CancellationToken cancellationToken = default)
        {
            await using var input = new FileStream(
                packagePath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            byte[] magic = new byte[Magic.Length];
            await input.ReadExactlyAsync(magic, cancellationToken).ConfigureAwait(false);
            if (!CryptographicOperations.FixedTimeEquals(magic, Magic))
            {
                throw new InvalidDataException("不是受支持的灾难恢复包。");
            }

            int headerLength = await ReadInt32Async(input, cancellationToken).ConfigureAwait(false);
            if (headerLength <= 0 || headerLength > MaximumHeaderBytes)
            {
                throw new InvalidDataException("灾难恢复包头长度无效。");
            }

            byte[] headerBytes = new byte[headerLength];
            await input.ReadExactlyAsync(headerBytes, cancellationToken).ConfigureAwait(false);
            PackageHeader header;
            try
            {
                header = JsonSerializer.Deserialize<PackageHeader>(headerBytes, JsonOptions)
                    ?? throw new InvalidDataException("灾难恢复包头为空。");
            }
            catch (JsonException ex)
            {
                throw new InvalidDataException("灾难恢复包头格式无效。", ex);
            }
            ValidateHeader(header);
            return header.PlaintextLength;
        }

        internal static void ValidatePassword(string password)
        {
            if (string.IsNullOrWhiteSpace(password) || password.Length < 12 || password.Length > 128)
            {
                throw new ArgumentException("灾难恢复包密码长度必须为 12 至 128 个字符。", nameof(password));
            }
            if (!password.Any(char.IsUpper) ||
                !password.Any(char.IsLower) ||
                !password.Any(char.IsDigit) ||
                !password.Any(ch => !char.IsLetterOrDigit(ch)))
            {
                throw new ArgumentException("灾难恢复包密码必须同时包含大写字母、小写字母、数字和符号。", nameof(password));
            }
        }

        private static byte[] DeriveKey(string password, byte[] salt, int iterations) =>
            Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, KeySize);

        private static void ValidateHeader(PackageHeader header)
        {
            if (header.SchemaVersion != 1 ||
                !string.Equals(header.Algorithm, "AES-256-GCM-CHUNKED", StringComparison.Ordinal) ||
                !string.Equals(header.Kdf, "PBKDF2-SHA256", StringComparison.Ordinal) ||
                header.Iterations < 300_000 ||
                header.Iterations > 2_000_000 ||
                header.ChunkSize != ChunkSize ||
                header.PlaintextLength <= 0 ||
                header.PlaintextLength > MaximumPlaintextBytes)
            {
                throw new InvalidDataException("灾难恢复包版本或加密参数不受支持。");
            }
        }

        private static void BuildNonce(byte[] prefix, uint chunkIndex, Span<byte> destination)
        {
            prefix.CopyTo(destination);
            BinaryPrimitives.WriteUInt32BigEndian(destination[NoncePrefixSize..], chunkIndex);
        }

        private static byte[] BuildAssociatedData(byte[] headerHash, uint chunkIndex, int chunkLength)
        {
            byte[] associatedData = new byte[headerHash.Length + 8];
            headerHash.CopyTo(associatedData, 0);
            BinaryPrimitives.WriteUInt32BigEndian(associatedData.AsSpan(headerHash.Length, 4), chunkIndex);
            BinaryPrimitives.WriteInt32BigEndian(associatedData.AsSpan(headerHash.Length + 4, 4), chunkLength);
            return associatedData;
        }

        private static async Task WriteInt32Async(
            Stream stream,
            int value,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(buffer, value);
            await stream.WriteAsync(buffer, cancellationToken).ConfigureAwait(false);
        }

        private static async Task<int> ReadInt32Async(
            Stream stream,
            CancellationToken cancellationToken)
        {
            byte[] buffer = new byte[4];
            try
            {
                await stream.ReadExactlyAsync(buffer, cancellationToken).ConfigureAwait(false);
            }
            catch (EndOfStreamException ex)
            {
                throw new InvalidDataException("灾难恢复包被截断。", ex);
            }
            return BinaryPrimitives.ReadInt32LittleEndian(buffer);
        }

        private sealed record PackageHeader(
            int SchemaVersion,
            string Algorithm,
            string Kdf,
            int Iterations,
            string Salt,
            string NoncePrefix,
            int ChunkSize,
            long PlaintextLength,
            DateTimeOffset CreatedAtUtc);
    }
}
