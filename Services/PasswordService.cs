using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace FattureViewer.Services
{
    public class PasswordService
    {
        private static string PasswordFilePath =>
            AppProfileService.GetPasswordFilePath();

        // Use a static entropy for slight additional obfuscation (optional)
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("FattureViewer_SecureEntropy_2026");

        public static bool IsPasswordSet()
        {
            return File.Exists(PasswordFilePath);
        }

        public static void SetPassword(string plainPassword)
        {
            if (string.IsNullOrEmpty(plainPassword))
                throw new ArgumentException("La password non può essere vuota.");

            byte[] plainBytes = Encoding.UTF8.GetBytes(plainPassword);
            
            // Encrypt using DPAPI - scope set to CurrentUser so only the current Windows user can decrypt it
            byte[] encryptedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);

            Directory.CreateDirectory(Path.GetDirectoryName(PasswordFilePath)!);
            File.WriteAllBytes(PasswordFilePath, encryptedBytes);
        }

        public static bool VerifyPassword(string inputPassword)
        {
            if (!IsPasswordSet()) return false;
            if (string.IsNullOrEmpty(inputPassword)) return false;

            try
            {
                byte[] encryptedBytes = File.ReadAllBytes(PasswordFilePath);
                byte[] plainBytes = ProtectedData.Unprotect(encryptedBytes, Entropy, DataProtectionScope.CurrentUser);
                
                string savedPassword = Encoding.UTF8.GetString(plainBytes);
                return inputPassword == savedPassword;
            }
            catch (CryptographicException)
            {
                // Decryption failed (e.g. wrong user, corrupted file)
                return false;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }
}
