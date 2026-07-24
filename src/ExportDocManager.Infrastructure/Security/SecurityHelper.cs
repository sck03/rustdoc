using System;
using System.Security.Cryptography;
using System.Text;
using ExportDocManager.Services.Infrastructure;

namespace ExportDocManager.Services.Security
{
    public static class SecurityHelper
    {
        private static readonly object ProtectorGate = new();
        private static LocalSecretProtector _protector =
            new(new RuntimeAppPathProvider());

        public static void ConfigurePathProvider(IAppPathProvider pathProvider)
        {
            ArgumentNullException.ThrowIfNull(pathProvider);
            lock (ProtectorGate)
            {
                _protector = new LocalSecretProtector(pathProvider);
            }
        }

        public static string Encrypt(string plainText)
        {
            return Volatile.Read(ref _protector).Protect(plainText);
        }

        public static string Decrypt(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return cipherText;

            return Volatile.Read(ref _protector).Unprotect(cipherText);
        }

        public static string ComputeHash(string input)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
                StringBuilder builder = new StringBuilder();
                for (int i = 0; i < bytes.Length; i++)
                {
                    builder.Append(bytes[i].ToString("x2"));
                }
                return builder.ToString();
            }
        }
    }
}
