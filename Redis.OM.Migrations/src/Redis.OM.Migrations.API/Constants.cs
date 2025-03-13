namespace Redis.OM.Migrations.API;

public static class Constants
{
    public static readonly Type[] Indexes = [typeof(User)];
    public const string EncryptedFileName = "dump.rdb.crypt";
    public const string DecryptedFileName = "dump.rdb.dcrypt";
}