using System.Security.Cryptography;
using System.Text;

namespace ImageScanner.ConsoleApp;

/// <summary>
/// A static class that provides methods to securely save and load a password using Windows Data Protection API (DPAPI).
/// </summary>
public static class SecureVault {
    private static readonly string VaultDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ImageScanner");
    private static readonly string KeyFile = Path.Combine(VaultDirectory, "vault.key"); // The file where the encrypted password will be stored

    /// <summary>
    /// Saves the provided password securely by encrypting it and writing it to a file.
    /// </summary>
    /// <param name="password">The password to save.</param>
    public static void SavePassword(string password) {
        if (!Directory.Exists(VaultDirectory)) {
            Directory.CreateDirectory(VaultDirectory);
        }
        byte[] rawBytes = Encoding.UTF8.GetBytes(password); // Convert the password to bytes
        byte[] encryptedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser); // Encrypt the password using DPAPI
        File.WriteAllBytes(KeyFile, encryptedBytes); // Write the encrypted password to the file
    }

    /// <summary>
    /// Loads the password securely by reading the encrypted password from the file and decrypting it.
    /// </summary>
    /// <returns>The decrypted password, or null if the file does not exist or decryption fails.</returns>
    public static string? LoadPassword() {
        // Check if the key file exists; if not, return null
        if (!File.Exists(KeyFile)) {
		    return null;
	    }

        // Attempt to read and decrypt the password from the file
        try {
		    byte[] encryptedBytes = File.ReadAllBytes(KeyFile); // Read the encrypted password from the file
            byte[] rawBytes = ProtectedData.Unprotect(encryptedBytes, null, DataProtectionScope.CurrentUser); // Decrypt the password using DPAPI
            return Encoding.UTF8.GetString(rawBytes); // Convert the decrypted bytes back to a string and return it
        }
	    catch {
		    return null; // Return null if decryption fails (e.g., if the data is corrupted or the user context has changed)
        }
    }

}
