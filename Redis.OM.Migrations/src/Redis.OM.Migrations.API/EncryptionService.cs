using System.Security.Cryptography;
using Microsoft.Extensions.Options;

namespace Redis.OM.Migrations.API;

public interface IEncryptionService
{
    Task<byte[]> EncryptAsync(byte[] bytes, out byte[] iv, out byte[] tag);
    Task<byte[]> EncryptAsPackageAsync(byte[] bytes, CancellationToken token);
    Task<byte[]> DecryptAsync(byte[] bytes, byte[] iv, byte[] tag);
    Task<byte[]> DecryptPackageAsync(byte[] bytes, CancellationToken token);
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
    /// Unlike the <see cref="EncryptAsync"/> method, this encryption technique
    /// doesn't output iv and tag through "out" parameters, but encodes them sequentially
    /// to the original set of bytes we want to encrypt.
    /// </summary>
    /// <param name="bytes">The bytes to encrypt</param>
    /// <param name="token">The cancellation token for circuit-breaking the async operations</param>
    /// <returns>Encrypted Package Bytes</returns>
    public async Task<byte[]> EncryptAsPackageAsync(byte[] bytes, CancellationToken token)
    {
        var keyBytes = Convert.FromBase64String(
            settings.Value.EncryptionKey ?? string.Empty);

        var iv = RandomNumberGenerator.GetBytes(12);
        var tag = new byte[16];
        var encryptedBytes = new byte[bytes.Length];
        
        using var aesGcm = new AesGcm(keyBytes, tag.Length);
        aesGcm.Encrypt(
            iv,
            bytes,
            encryptedBytes,
            tag);
        await using var resultStream = new MemoryStream();
        
        await resultStream.WriteAsync(iv.AsMemory(0, iv.Length), token);
        await resultStream.WriteAsync(encryptedBytes.AsMemory(0, encryptedBytes.Length), token);
        await resultStream.WriteAsync(tag.AsMemory(0, tag.Length), token);

        return resultStream.ToArray();
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

    /// <summary>
    /// This function pairs up with <see cref="EncryptAsPackageAsync"/> in the sense
    /// that it will attempt to get the iv and tag for decrypting the encrypted
    /// bytes by reading specific positions and then running the decrypt function
    /// with the extracted variables plus the encrypted set of bytes.
    /// </summary>
    /// <param name="bytes">Stream that contains iv, tag and the encrypted bytes</param>
    /// <param name="token">The cancellation token for circuit-breaking the async operations</param>
    /// <returns>Decrypted bytes</returns>
    public async Task<byte[]> DecryptPackageAsync(byte[] bytes, CancellationToken token)
    {
        var keyBytes = Convert.FromBase64String(
            settings.Value.EncryptionKey ?? string.Empty);
        
        await using var memoryStream = new MemoryStream(bytes);
        
        var iv = new byte[12];
        var tag = new byte[16];
        
        await memoryStream.ReadExactlyAsync(iv.AsMemory(0, iv.Length), token);
        
        memoryStream.Seek(-tag.Length, SeekOrigin.End);
        await memoryStream.ReadExactlyAsync(tag.AsMemory(0, tag.Length), token);
        
        memoryStream.Seek(iv.Length, SeekOrigin.Begin);
        var encryptedBytes = new byte[bytes.Length - iv.Length - tag.Length];
        var readBytes = await memoryStream.ReadAsync(encryptedBytes.AsMemory(0, encryptedBytes.Length), token);
        
        var decryptedBytes = new byte[readBytes];
        using var aesGcm = new AesGcm(keyBytes, tag.Length);
        aesGcm.Decrypt(
            iv,
            encryptedBytes,
            tag,
            decryptedBytes);

        return decryptedBytes;
    }
}