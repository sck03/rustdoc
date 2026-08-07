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
            var salt = RandomNumberGenerator.GetBytes(SaltSize);
            var key = Rfc2898DeriveBytes.Pbkdf2(
                password,
                salt,
                Iterations,
                HashAlgorithmName.SHA256,
                KeySize);
            return $"{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(key)}";
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

                var keyToCheck = Rfc2898DeriveBytes.Pbkdf2(
                    password ?? string.Empty,
                    salt,
                    iterations,
                    HashAlgorithmName.SHA256,
                    KeySize);
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
