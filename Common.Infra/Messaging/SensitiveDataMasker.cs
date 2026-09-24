using System.Collections;
using System.Reflection;

namespace Common.Messaging
{
    /// <summary>
    /// Tapa los campos sensibles antes de que un objeto llegue al log.
    ///
    /// POR QUE EXISTE: `InteractorPipeline` registraba cada peticion y cada respuesta
    /// destructuradas con `LogInformation("{@Request}", request)`. Eso escribia en claro,
    /// entre otras cosas:
    ///
    ///     {"Email": "...", "Password": "...", "$type": "LoginRequest"}
    ///     {"AccessToken": "eyJ...", "RefreshToken": "...", ...}
    ///
    /// y el nivel configurado en produccion es `Information`, asi que llegaba a consola
    /// --que el runtime del contenedor captura-- y a Seq. Medido sobre una hora de uso de
    /// desarrollo: 5 contrasenas y 12 JWT completos.
    ///
    /// COMO DECIDE QUE TAPAR: por NOMBRE de propiedad, con coincidencia parcial y sin
    /// distinguir mayusculas. La coincidencia es parcial a proposito: `Password`,
    /// `NewPassword`, `PasswordHash` y `ConfirmPassword` caen todas con un solo termino.
    ///
    /// LO QUE NO HACE, y hay que saberlo: esto es una lista NEGRA. Un campo sensible con un
    /// nombre que no este en la lista se registra en claro. La lista se amplia cuando aparece
    /// un caso nuevo, y por eso `Terminos` es publica: un producto puede sumar los suyos.
    /// La alternativa --lista blanca de lo que si se registra-- rompe el valor de depuracion
    /// que este log tiene hoy, y ese cambio merece su propia decision.
    /// </summary>
    public static class SensitiveDataMasker
    {
        public const string Tapado = "***";

        /// <summary>
        /// Fragmentos de nombre que marcan un valor como sensible. Coincidencia PARCIAL e
        /// insensible a mayusculas: "password" tapa tambien "NewPassword" y "PasswordHash".
        /// </summary>
        public static readonly IReadOnlyList<string> Terminos = new[]
        {
            "password", "contrasena", "contrasena",
            "token",          // AccessToken, RefreshToken, ResetToken, IdToken
            "secret",         // ClientSecret, WebhookSecret, SigningKey via "secret"
            "apikey", "api_key",
            "authorization",
            "cardnumber", "card_number", "cvv", "cvc",
            "privatekey", "private_key",
            "connectionstring",
            "otp", "totp",
        };

        // Un tope de profundidad, no por rendimiento sino porque un grafo con ciclos
        // (una entidad con su padre, que a su vez la contiene) haria desbordar la pila
        // dentro del propio logger -- el peor sitio posible para reventar.
        private const int ProfundidadMaxima = 4;

        /// <summary>
        /// Sufijos que DESARMAN la coincidencia: describen el token, no lo contienen.
        ///
        /// Sin esto, `AccessTokenExpiresAt` caia por contener "token" y se tapaba una fecha
        /// de caducidad -- que no es secreta y es justo el dato con el que se depura un
        /// token vencido. Se vio en el log real tras aplicar el enmascarado.
        ///
        /// La lista es de SUFIJOS y no de nombres completos para que cubra la familia:
        /// AccessTokenExpiresAt, RefreshTokenExpiresAt, TokenExpiresOn.
        /// </summary>
        private static readonly string[] SufijosDescriptivos =
        {
            "expiresat", "expireson", "expiry", "expiresin",
            "type", "length", "count", "issuedat",
        };

        public static bool EsSensible(string nombre)
        {
            if (SufijosDescriptivos.Any(s => nombre.EndsWith(s, StringComparison.OrdinalIgnoreCase)))
                return false;
            return Terminos.Any(t => nombre.Contains(t, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Devuelve una vista del objeto apta para el log: los campos sensibles con
        /// <see cref="Tapado"/> y el resto tal cual. Nunca lanza: un fallo aqui no debe
        /// tumbar la peticion que se estaba registrando.
        /// </summary>
        public static object? Enmascarar(object? valor) => Enmascarar(valor, 0);

        private static object? Enmascarar(object? valor, int profundidad)
        {
            if (valor is null) return null;
            if (profundidad >= ProfundidadMaxima) return valor.GetType().Name;

            var tipo = valor.GetType();

            // Los escalares y todo lo que ya se imprime bien se deja intacto: el riesgo no
            // esta en el VALOR sino en el NOMBRE con el que viaja, y eso lo decide el padre.
            if (tipo.IsPrimitive || valor is string || valor is decimal || valor is DateTime
                || valor is DateTimeOffset || valor is DateOnly || valor is TimeOnly
                || valor is Guid || valor is Enum)
            {
                return valor;
            }

            // Un diccionario se tapa por CLAVE, igual que un objeto por nombre de propiedad.
            // Sin esto caia en la rama de coleccion y cada entrada salia como {Key, Value}:
            // la clave "Password" no es sensible como nombre de propiedad ("Key"), y el valor
            // se escribia en claro. Es la forma normal de pasar parametros a Dapper.
            if (valor is IDictionary diccionario)
            {
                return EnmascararEntradas(Entradas(diccionario), profundidad);
            }

            if (valor is IEnumerable<KeyValuePair<string, object?>> pares)   // ExpandoObject
            {
                return EnmascararEntradas(pares.Select(p => (p.Key, p.Value)), profundidad);
            }

            if (valor is IEnumerable enumerable)
            {
                var lista = new List<object?>();
                foreach (var item in enumerable)
                {
                    lista.Add(Enmascarar(item, profundidad + 1));
                    if (lista.Count >= 50) { lista.Add("..."); break; }
                }
                return lista;
            }

            var salida = new Dictionary<string, object?> { ["$type"] = tipo.Name };
            foreach (var p in tipo.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (p.GetIndexParameters().Length > 0) continue;

                if (EsSensible(p.Name))
                {
                    // Se tapa SIN leer el valor: leerlo y descartarlo deja el secreto en
                    // memoria y en cualquier volcado, y ademas un getter con efectos
                    // secundarios no tiene por que ejecutarse para escribir un log.
                    salida[p.Name] = Tapado;
                    continue;
                }

                try   { salida[p.Name] = Enmascarar(p.GetValue(valor), profundidad + 1); }
                catch { salida[p.Name] = "<no legible>"; }
            }
            return salida;
        }

        // Por el enumerador de IDictionary y no con Cast<DictionaryEntry>(): un Dictionary<,>
        // se enumera como KeyValuePair aunque se le trate como IDictionary, y el Cast lanza.
        private static IEnumerable<(string Clave, object? Valor)> Entradas(IDictionary diccionario)
        {
            var e = diccionario.GetEnumerator();
            while (e.MoveNext())
            {
                yield return (Convert.ToString(e.Key) ?? string.Empty, e.Value);
            }
        }

        private static Dictionary<string, object?> EnmascararEntradas(
            IEnumerable<(string Clave, object? Valor)> entradas, int profundidad)
        {
            var salida = new Dictionary<string, object?>();
            foreach (var (clave, valor) in entradas)
            {
                if (salida.Count >= 50) { salida["..."] = "..."; break; }
                salida[clave] = EsSensible(clave) ? Tapado : Enmascarar(valor, profundidad + 1);
            }
            return salida;
        }
    }
}
