namespace Common.PostgreSql
{
    public sealed class SchemaMigrationOptions
    {
        public string ScriptsRelativePath { get; set; } = Path.Combine("Services", "Schema Migration", "Tables");
    }
}
