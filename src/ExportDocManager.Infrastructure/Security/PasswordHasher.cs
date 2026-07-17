using System;
using System.Security.Cryptography;

namespace ExportDocManager.Services.Security
{
    public static class PasswordHasher
    {
        private const int SaltSize = 16;
        private const int KeySize = 32;
        private const int Iterations = 210000;

        public static string HashPassword(string password)
        {
            using (var algorithm = new Rfc2898DeriveBytes(password, SaltSize, Iterations, HashAlgorithmName.SHA256))
            {
                var key = Convert.ToBase64String(algorithm.GetBytes(KeySize));
                var salt = Convert.ToBase64String(algorithm.Salt);
                return $"{Iterations}.{salt}.{key}";
            }
        }

        public static bool VerifyPassword(string hash, string password)
        {
            if (string.IsNullOrWhiteSpace(hash))
            {
                return false;
            }

            try
            {
                var parts = hash.Split('.', 3);
                if (parts.Length != 3 ||
                    !int.TryParse(parts[0], out var iterations) ||
                    iterations <= 0)
                {
                    return false;
                }

                var salt = Convert.FromBase64String(parts[1]);
                var key = Convert.FromBase64String(parts[2]);
                if (salt.Length != SaltSize || key.Length != KeySize)
                {
                    return false;
                }

                using var algorithm = new Rfc2898DeriveBytes(
                    password ?? string.Empty,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256);
                var keyToCheck = algorithm.GetBytes(KeySize);
                return CryptographicOperations.FixedTimeEquals(keyToCheck, key);
            }
            catch (FormatException)
            {
                return false;
            }
            catch (OverflowException)
            {
                return false;
            }
        }
    }
}
