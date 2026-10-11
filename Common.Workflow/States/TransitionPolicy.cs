using System.Text.Json;
using System.Text.Json.Serialization;

namespace Common.Workflow.States
{
    /// <summary>
    /// Matriz de permisos: que grants (roles o codigos de permiso, a eleccion del producto) pueden hacer cada
    /// transicion. <b>Deniega por omision</b>: una transicion ausente de la matriz solo la hace el sistema.
    /// </summary>
    /// <param name="GrantsByTransition">
    /// Codigo de transicion -> grants que la conceden. La clave <see cref="AnyTransition"/> concede todas las
    /// transiciones a sus grants (p. ej. <c>platform.superadmin</c>). Comparacion ordinal.
    /// </param>
    /// <remarks>
    /// Se serializa como <c>{"GrantsByTransition":{"close":["supervisor"]}}</c>, para guardarla como <c>jsonb</c>
    /// en la configuracion por tenant.
    /// </remarks>
    [JsonConverter(typeof(TransitionPolicyJsonConverter))]
    public sealed record TransitionPolicy(IReadOnlyDictionary<string, IReadOnlySet<string>> GrantsByTransition)
    {
        /// <summary>Comodin de transicion: sus grants pueden hacer cualquier transicion declarada.</summary>
        public const string AnyTransition = "*";

        /// <summary>
        /// Si alguno de los grants del actor tiene concedida la transicion, directamente o por el comodin.
        /// </summary>
        /// <param name="transitionCode">Codigo de la transicion.</param>
        /// <param name="actorGrants">Grants del actor.</param>
        /// <returns>Verdadero solo si hay concesion explicita.</returns>
        public bool IsGranted(string transitionCode, IReadOnlySet<string> actorGrants)
        {
            ArgumentNullException.ThrowIfNull(transitionCode);
            ArgumentNullException.ThrowIfNull(actorGrants);
            ArgumentNullException.ThrowIfNull(GrantsByTransition);
            return Overlaps(transitionCode, actorGrants) || Overlaps(AnyTransition, actorGrants);
        }

        private bool Overlaps(string transitionCode, IReadOnlySet<string> actorGrants)
        {
            if (!GrantsByTransition.TryGetValue(transitionCode, out var granted) || granted is null)
                return false;
            foreach (var grant in actorGrants)
            {
                if (granted.Contains(grant))
                    return true;
            }
            return false;
        }
    }

    /// <summary>
    /// Lee y escribe <see cref="TransitionPolicy"/>: <c>System.Text.Json</c> no sabe crear un
    /// <see cref="IReadOnlySet{T}"/> por si solo.
    /// </summary>
    internal sealed class TransitionPolicyJsonConverter : JsonConverter<TransitionPolicy>
    {
        private sealed record Shape(Dictionary<string, HashSet<string>>? GrantsByTransition);

        public override TransitionPolicy Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            var shape = JsonSerializer.Deserialize<Shape>(ref reader, options)
                        ?? throw new JsonException("La politica no puede ser null.");
            var grants = (shape.GrantsByTransition ?? []).ToDictionary(
                kv => kv.Key,
                kv => (IReadOnlySet<string>)(kv.Value ?? new HashSet<string>(StringComparer.Ordinal)),
                StringComparer.Ordinal);
            return new TransitionPolicy(grants);
        }

        public override void Write(Utf8JsonWriter writer, TransitionPolicy value, JsonSerializerOptions options)
        {
            var shape = new Shape(value.GrantsByTransition?.ToDictionary(
                kv => kv.Key, kv => kv.Value?.ToHashSet(StringComparer.Ordinal) ?? [], StringComparer.Ordinal));
            JsonSerializer.Serialize(writer, shape, options);
        }
    }
}
