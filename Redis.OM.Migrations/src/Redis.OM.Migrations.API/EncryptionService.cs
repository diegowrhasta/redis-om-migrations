using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Redis.OM.Migrations.API;

public interface IEncryptionService
{
    Task<byte[]> EncryptAsync(byte[] bytes, out byte[] iv, out byte[] tag);
    Task<byte[]> DecryptAsync(byte[] bytes, byte[] iv, byte[] tag);
}

public class EncryptionService(IOptions<ApiSettings> settings) : IEncryptionService
{
    /// <summary>
    /// General usage function that works with primitives (byte arrays)
    /// The resulting byte array will contain both the iv and the tag plus
    /// the actual information that was encrypted.
    /// </summary>
    /// <param name="bytes">The bytes of the plain data that will be encrypted</param>
    /// <param name="iv">A variable that the consumer will keep a reference to when the IV has been generated</param>
    /// <param name="tag">A variable that the consumer will keep a reference to when the Tag has been generated</param>
    /// <returns>It returns a byte array that represents now ciphered data</returns>
    public Task<byte[]> EncryptAsync(byte[] bytes, out byte[] iv, out byte[] tag)
    {
        var keyBytes = Convert.FromBase64String(
            settings.Value.EncryptionKey ?? string.Empty);
        iv = RandomNumberGenerator.GetBytes(12);
        tag = new byte[16];
        var encryptedBytes = new byte[bytes.Length];
        
        using var aesGcm = new AesGcm(keyBytes, tag.Length);
        aesGcm.Encrypt(
            iv,
            bytes,
            encryptedBytes,
            tag);

        return Task.FromResult(encryptedBytes);
    }

    /// <summary>
    /// General usage function that works with primitives (byte arrays) it assumes
    /// that under an AES-GCM algorithm encrypted piece of data, we feed the iv and
    /// tag that were also part of said encryption process.
    /// </summary>
    /// <param name="bytes">The bytes of the ciphered data that will be decrypted</param>
    /// <param name="iv">Every encrypted piece of bytes needs an IV paired up with it</param>
    /// <param name="tag">Every encrypted piece of bytes needs a tag paired up with it</param>
    /// <returns>It returns the decrypted byte array of the data that was ciphered</returns>
    public Task<byte[]> DecryptAsync(byte[] bytes, byte[] iv, byte[] tag)
    {
        var keyBytes = Convert.FromBase64String(
            settings.Value.EncryptionKey ?? string.Empty);
        
        using var aesGcm = new AesGcm(keyBytes, tag.Length);
        var decryptedBytes = new byte[bytes.Length];
        aesGcm.Decrypt(
            iv,
            bytes,
            tag,
            decryptedBytes);

        return Task.FromResult(decryptedBytes);
    }
}