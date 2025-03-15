namespace Redis.OM.Migrations.API;

public static class Constants
{
    public static readonly Type[] Indexes = [typeof(User)];
    public const string EncryptedFileName = "dump.rdb.crypt";
    public const string DecryptedFileName = "dump.rdb.dcrypt";
    // 4 KB Chunks
    // 12 Bytes = IV
    // 16 Bytes = Tag
    // 4096 Bytes = Actual Payload
    // When file size is way below that, we need to readjust the calculations 
    // to whatever size we are dealing with
    public const int ChunkSize = 4096;
    public const int IvSize = 12;
    public const int TagSize = 16;
}