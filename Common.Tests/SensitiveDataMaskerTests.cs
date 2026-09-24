using Xunit;
using Common.Messaging;

namespace Common.Tests;

/// <summary>
/// El enmascarado de datos sensibles antes del log.
///
/// Existe por un defecto medido en produccion-potencial: `InteractorPipeline` registraba
/// cada peticion y respuesta destructuradas con nivel Information, y el nivel configurado
/// en produccion ES Information. En una hora de uso de desarrollo el log acumulo 5
/// contrasenas en claro y 12 JWT completos.
///
/// Las dos primeras pruebas son las que importan: cubren exactamente las dos formas que se
/// vieron en el log real.
/// </summary>
public sealed class SensitiveDataMaskerTests
{
    private sealed record LoginRequest(string Email, string Password);
    private sealed record LoginResponse(string AccessToken, string RefreshToken, string FullName);

    [Fact]
    public void La_contrasena_de_un_login_no_llega_al_log()
    {
        // La forma exacta que aparecia en el log:
        //   {"Email": "siembra@local.test", "Password": "<en claro>", "$type": "LoginRequest"}
        var enmascarado = SensitiveDataMasker.Enmascarar(
            new LoginRequest("ana@ejemplo.test", "SuperSecreta.2026!"));

        var d = Assert.IsType<Dictionary<string, object?>>(enmascarado);
        Assert.Equal(SensitiveDataMasker.Tapado, d["Password"]);
        // El correo SI se conserva: es lo que hace util el log para depurar, y no es
        // un secreto. Tapar todo equivaldria a no registrar nada.
        Assert.Equal("ana@ejemplo.test", d["Email"]);
    }

    [Fact]
    public void Los_dos_tokens_de_la_respuesta_no_llegan_al_log()
    {
        var enmascarado = SensitiveDataMasker.Enmascarar(
            new LoginResponse("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.carga", "qFbdwMTBpMwS", "Ana Duena"));

        var d = Assert.IsType<Dictionary<string, object?>>(enmascarado);
        Assert.Equal(SensitiveDataMasker.Tapado, d["AccessToken"]);
        Assert.Equal(SensitiveDataMasker.Tapado, d["RefreshToken"]);
        Assert.Equal("Ana Duena", d["FullName"]);
    }

    private sealed record VariantesDeNombre(
        string NewPassword, string PasswordHash, string ConfirmPassword,
        string ClientSecret, string ApiKey, string CardNumber, string Cvv);

    [Fact]
    public void La_coincidencia_es_parcial_para_cubrir_las_variantes()
    {
        // Es lo que hace que un termino cubra una familia: "password" tapa tambien
        // NewPassword, PasswordHash y ConfirmPassword. Con coincidencia exacta habria que
        // enumerarlas todas y la siguiente variante se escaparia.
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(
                new VariantesDeNombre("a", "b", "c", "d", "e", "f", "g")));

        foreach (var campo in new[] { "NewPassword", "PasswordHash", "ConfirmPassword",
                                      "ClientSecret", "ApiKey", "CardNumber", "Cvv" })
        {
            Assert.Equal(SensitiveDataMasker.Tapado, d[campo]);
        }
    }

    [Fact]
    public void No_distingue_mayusculas()
    {
        Assert.True(SensitiveDataMasker.EsSensible("PASSWORD"));
        Assert.True(SensitiveDataMasker.EsSensible("accessToken"));
        Assert.True(SensitiveDataMasker.EsSensible("Api_Key"));
        Assert.False(SensitiveDataMasker.EsSensible("Email"));
        Assert.False(SensitiveDataMasker.EsSensible("FullName"));
    }

    private sealed record Anidado(string Nombre, LoginRequest Credenciales);

    [Fact]
    public void Tapa_tambien_dentro_de_un_objeto_anidado()
    {
        // El caso que se escapa si solo se miran las propiedades del primer nivel.
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(
                new Anidado("alta", new LoginRequest("x@y.z", "secreta"))));

        var dentro = Assert.IsType<Dictionary<string, object?>>(d["Credenciales"]);
        Assert.Equal(SensitiveDataMasker.Tapado, dentro["Password"]);
    }

    [Fact]
    public void Tapa_dentro_de_una_coleccion()
    {
        var d = Assert.IsType<List<object?>>(
            SensitiveDataMasker.Enmascarar(new[]
            {
                new LoginRequest("a@b.c", "uno"),
                new LoginRequest("d@e.f", "dos"),
            }));

        foreach (var item in d)
        {
            var e = Assert.IsType<Dictionary<string, object?>>(item);
            Assert.Equal(SensitiveDataMasker.Tapado, e["Password"]);
        }
    }

    private sealed class ConGetterQueRevienta
    {
        public string Nombre => "visible";
        public string Password => throw new InvalidOperationException("no deberia leerse");
        public string Otro => throw new InvalidOperationException("esta si se intenta");
    }

    [Fact]
    public void Un_getter_sensible_ni_siquiera_se_ejecuta()
    {
        // Tapar leyendo el valor y descartandolo deja el secreto en memoria y en cualquier
        // volcado. Y un getter con efectos secundarios no tiene por que correr solo para
        // escribir un log. Esta prueba lo fija: si alguien "simplifica" a leer-y-tapar,
        // la excepcion de `Password` hace caer el caso.
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(new ConGetterQueRevienta()));

        Assert.Equal(SensitiveDataMasker.Tapado, d["Password"]);
        Assert.Equal("visible", d["Nombre"]);
        // Y un getter NO sensible que revienta no tumba el log: se degrada.
        Assert.Equal("<no legible>", d["Otro"]);
    }

    private sealed class Ciclico
    {
        public string Nombre { get; set; } = "raiz";
        public Ciclico? Padre { get; set; }
    }

    [Fact]
    public void Un_grafo_ciclico_no_desborda_la_pila_dentro_del_logger()
    {
        // Reventar aqui seria el peor sitio: dentro del propio registro, tumbando la
        // peticion que se estaba anotando.
        //
        // OJO AL MEDIR ESTA GUARDA: quitando el tope de profundidad, esta prueba NO falla
        // -- mata el proceso. La corrida entera termina en "Test Run Aborted" con un
        // desbordamiento de pila, sin una sola linea de "Failed". Un barrido de mutacion
        // que busque la palabra "Failed" la lee como si hubiera pasado. Comprobado:
        // subiendo ProfundidadMaxima a 100000, el host de pruebas cae con codigo 1 y el
        // rastro lleno de `SensitiveDataMasker.Enmascarar`.
        var a = new Ciclico { Nombre = "a" };
        var b = new Ciclico { Nombre = "b", Padre = a };
        a.Padre = b;

        var d = Assert.IsType<Dictionary<string, object?>>(SensitiveDataMasker.Enmascarar(a));
        Assert.Equal("a", d["Nombre"]);
    }

    [Fact]
    public void Un_nulo_sigue_siendo_nulo_y_un_escalar_no_se_toca()
    {
        Assert.Null(SensitiveDataMasker.Enmascarar(null));
        Assert.Equal("hola", SensitiveDataMasker.Enmascarar("hola"));
        Assert.Equal(42, SensitiveDataMasker.Enmascarar(42));
    }

    private sealed record ConCaducidad(
        string AccessToken, DateTime AccessTokenExpiresAt,
        string RefreshToken, DateTime RefreshTokenExpiresAt);

    [Fact]
    public void La_caducidad_de_un_token_NO_se_tapa_porque_no_es_secreta()
    {
        // Se vio en el log real tras aplicar el enmascarado: `AccessTokenExpiresAt` caia
        // por contener "token" y se tapaba una fecha. No es un secreto, y es justo el dato
        // con el que se depura un token vencido -- taparlo cuesta y no protege nada.
        var vence = new DateTime(2026, 9, 23, 2, 0, 0, DateTimeKind.Utc);
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(new ConCaducidad("eyJ...", vence, "qFbd...", vence)));

        Assert.Equal(SensitiveDataMasker.Tapado, d["AccessToken"]);
        Assert.Equal(SensitiveDataMasker.Tapado, d["RefreshToken"]);
        // Las dos fechas se conservan.
        Assert.Equal(vence, d["AccessTokenExpiresAt"]);
        Assert.Equal(vence, d["RefreshTokenExpiresAt"]);
    }

    [Fact]
    public void El_sufijo_exento_no_abre_un_hueco_en_el_nombre_a_secas()
    {
        // La exencion es por SUFIJO. Un campo que simplemente se llame "Token" sigue
        // tapado: lo contrario convertiria la exencion en un agujero.
        Assert.True(SensitiveDataMasker.EsSensible("Token"));
        Assert.True(SensitiveDataMasker.EsSensible("AccessToken"));
        Assert.False(SensitiveDataMasker.EsSensible("AccessTokenExpiresAt"));
        Assert.False(SensitiveDataMasker.EsSensible("TokenType"));
    }

    [Fact]
    public void La_exencion_es_por_SUFIJO_y_no_por_contener_el_termino()
    {
        // La diferencia no es teorica: si la exencion se comprobara con Contains en vez de
        // EndsWith, un campo con un termino descriptivo EN MEDIO quedaria exento y se
        // registraria en claro. `TypeOfPassword` lleva "type" dentro y es una contrasena.
        //
        // Se comprobo mutando EndsWith -> Contains: las pruebas seguian verdes sin esta.
        Assert.True(SensitiveDataMasker.EsSensible("TypeOfPassword"));
        Assert.True(SensitiveDataMasker.EsSensible("ExpiresAtSecret"));
        Assert.True(SensitiveDataMasker.EsSensible("CountOfApiKey"));
        // Y las exenciones legitimas siguen funcionando.
        Assert.False(SensitiveDataMasker.EsSensible("AccessTokenExpiresAt"));
    }

    [Fact]
    public void Un_diccionario_se_tapa_por_clave()
    {
        // Antes un diccionario caia en la rama de coleccion y cada entrada salia como
        // {Key: "Password", Value: "<en claro>"}: el nombre de la propiedad era "Value", que
        // no es sensible. Es la forma normal de pasar parametros a Dapper.
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(new Dictionary<string, object?>
            {
                ["Email"] = "ana@ejemplo.test",
                ["PasswordHash"] = "AQAAAAIAAYagAAAAE-hash",
            }));

        Assert.Equal(SensitiveDataMasker.Tapado, d["PasswordHash"]);
        Assert.Equal("ana@ejemplo.test", d["Email"]);
    }

    [Fact]
    public void Un_diccionario_con_valores_tipados_tambien_se_tapa_por_clave()
    {
        // Un Dictionary<string, object?> lo atrapa la rama de pares clave-valor; uno con
        // valores tipados no es IEnumerable<KeyValuePair<string, object?>> y solo lo cubre
        // la rama de IDictionary. Sin esta prueba, quitar esa rama no rompia nada.
        var d = Assert.IsType<Dictionary<string, object?>>(
            SensitiveDataMasker.Enmascarar(new Dictionary<string, string>
            {
                ["Email"] = "ana@ejemplo.test",
                ["ClientSecret"] = "cs_live_123",
            }));

        Assert.Equal(SensitiveDataMasker.Tapado, d["ClientSecret"]);
        Assert.Equal("ana@ejemplo.test", d["Email"]);
    }

    [Fact]
    public void Un_ExpandoObject_se_tapa_por_clave()
    {
        dynamic expando = new System.Dynamic.ExpandoObject();
        expando.ApiKey = "sk_live_123";
        expando.Nombre = "visible";

        var d = Assert.IsType<Dictionary<string, object?>>(SensitiveDataMasker.Enmascarar((object)expando));

        Assert.Equal(SensitiveDataMasker.Tapado, d["ApiKey"]);
        Assert.Equal("visible", d["Nombre"]);
    }
}
