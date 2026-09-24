namespace Common.PostgreSql
{
    /// <summary>
    /// Opciones de <see cref="SchemaMigrationHostedService"/>.
    /// </summary>
    public sealed class SchemaMigrationOptions
    {
        /// <summary>
        /// Carpeta de los scripts <c>.sql</c>, relativa al directorio de salida o, si ahi no existe, al
        /// directorio actual. Por defecto <c>Services/Schema Migration/Tables</c>.
        /// </summary>
        public string ScriptsRelativePath { get; set; } = Path.Combine("Services", "Schema Migration", "Tables");
    }
}
