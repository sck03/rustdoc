using System;
using System.Security.Cryptography;
using System.Text;

namespace ExportDocManager.DataAccess
{
    public class DatabaseKeyProvider
    {
        private string _key;

        public string Key 
        { 
            get => _key;
            private set => _key = value;
        }

        // Generate a stable key from user password for SQLCipher
        public void DeriveAndSetKey(string password)
        {
            if (string.IsNullOrEmpty(password))
            {
                _key = string.Empty;
                return;
            }

            // Using a fixed salt for the application so the same password 
            // always derives the same database encryption key.
            byte[] salt = Encoding.UTF8.GetBytes("ExportDocManager_SQLCipher_Salt_2026");
            
            // PBKDF2
            using (var pbkdf2 = new Rfc2898DeriveBytes(password, salt, 100000, HashAlgorithmName.SHA256))
            {
                byte[] keyBytes = pbkdf2.GetBytes(32); // 256-bit key
                _key = Convert.ToBase64String(keyBytes);
            }
        }
    }
}
