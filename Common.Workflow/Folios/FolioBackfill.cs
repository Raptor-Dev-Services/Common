namespace Common.Workflow.Folios
{
    /// <summary>
    /// Ayuda para la migracion que adopta folios sobre una tabla con datos: la expresion SQL (PostgreSQL) que
    /// produce el mismo <c>scope_key</c> que <see cref="FolioKey.ToScopeKey"/>, para que el relleno y el
    /// contador usen la misma clave.
    /// </summary>
    /// <remarks>
    /// Los nombres de columna se validan como identificadores (simples o entre comillas dobles, con un
    /// calificador opcional <c>t.columna</c>): nunca se concatena texto arbitrario en el SQL. Los ids se
    /// convierten con <c>::text</c>, asi que el producto debe pasar a <see cref="FolioKey"/> el mismo texto
    /// que PostgreSQL produce (uuid en minusculas, long sin ceros a la izquierda).
    /// </remarks>
    public static class FolioBackfill
    {
        /// <summary>
        /// Expresion SQL del <c>scope_key</c> para un ambito: <c>'global'</c>,
        /// <c>'t:' || tenant_id::text</c> o <c>'t:' || tenant_id::text || '/b:' || branch_id::text</c>,
        /// con <c>|| '/p:' || periodo::text</c> si se da columna de periodo.
        /// </summary>
        /// <param name="scope">Ambito del contador.</param>
        /// <param name="tenantColumn">Columna del tenant.</param>
        /// <param name="branchColumn">Columna de la sucursal.</param>
        /// <param name="periodColumn">Columna (o alias) con el periodo, para contadores que reinician; nula si no.</param>
        /// <returns>La expresion SQL.</returns>
        /// <exception cref="ArgumentException">Si algun nombre de columna no es un identificador valido o el ambito no existe.</exception>
        public static string ScopeKeySql(FolioScope scope, string tenantColumn = "tenant_id",
            string branchColumn = "branch_id", string? periodColumn = null)
        {
            var sql = scope switch
            {
                FolioScope.Global => "'global'",
                FolioScope.Tenant => $"'t:' || {Identifier(tenantColumn, nameof(tenantColumn))}::text",
                FolioScope.Branch => $"'t:' || {Identifier(tenantColumn, nameof(tenantColumn))}::text"
                                     + $" || '/b:' || {Identifier(branchColumn, nameof(branchColumn))}::text",
                _ => throw new ArgumentException($"Ambito desconocido: {scope}.", nameof(scope)),
            };
            if (periodColumn is not null)
                sql += $" || '/p:' || {Identifier(periodColumn, nameof(periodColumn))}::text";
            return sql;
        }

        private static string Identifier(string value, string name)
        {
            if (value is null || value.Split('.') is not { Length: 1 or 2 } parts || !parts.All(IsIdentifierPart))
                throw new ArgumentException($"'{value}' no es un identificador SQL valido.", name);
            return value;
        }

        private static bool IsIdentifierPart(string part)
        {
            if (part.Length >= 3 && part[0] == '"' && part[^1] == '"')
                return !part[1..^1].Contains('"');
            return part.Length > 0
                   && (char.IsAsciiLetter(part[0]) || part[0] == '_')
                   && part.All(c => char.IsAsciiLetterOrDigit(c) || c == '_');
        }
    }
}
