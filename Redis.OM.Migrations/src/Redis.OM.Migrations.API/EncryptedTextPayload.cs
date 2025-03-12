namespace Redis.OM.Migrations.API;

public record EncryptedTextPayload(string Base64EncodedEncryptedText, string Iv, string Tag);
public record EncryptedTextPackagePayload(string Base64EncryptedPackage);