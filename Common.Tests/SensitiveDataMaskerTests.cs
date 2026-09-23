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
}
