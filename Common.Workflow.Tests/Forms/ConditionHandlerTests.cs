using Common.Workflow.Forms;
using static Common.Workflow.Tests.Forms.FormFixture;

namespace Common.Workflow.Tests.Forms;

/// <summary>
/// REQ-CMN-C06-008: tabla de verdad de los manejadores integrados, evaluados a traves del motor.
/// </summary>
public class ConditionHandlerTests
{
    private static bool Eval(string type, AnswerValue answer, ValueOperator op, string? value)
    {
        var form = Form(Question("src", type), Question("qt", "text", VisibleIf(Q("src", op, value))));
        return Engine.ComputeVisibility(form, Answers(("src", answer))).Questions["qt"];
    }

    public static TheoryData<string?, ValueOperator, string?, bool> TextCases => new()
    {
        { "Hola Mundo", ValueOperator.Equals, "hola mundo", true },
        { "Hola", ValueOperator.Equals, "hola mundo", false },
        { "Hola", ValueOperator.NotEquals, "HOLA", false },
        { null, ValueOperator.NotEquals, "x", true },
        { "Hola Mundo", ValueOperator.Contains, "MUNDO", true },
        { "Hola", ValueOperator.Contains, "x", false },
        { "Hola", ValueOperator.NotContains, "x", true },
        { "ÁRBOL", ValueOperator.Equals, "árbol", true },                 // minusculas invariantes, no solo ASCII
        { "año", ValueOperator.LengthGreaterThan, "2", true },
        { "año", ValueOperator.LengthGreaterThanOrEquals, "3", true },
        { "año", ValueOperator.LengthLessThan, "3", false },
        { "año", ValueOperator.LengthLessThanOrEquals, "3", true },
        { "é", ValueOperator.LengthLessThanOrEquals, "1", true },    // e + acento combinante = 1 caracter de texto
        { "abc", ValueOperator.LengthGreaterThan, "dos", false },
        { "abc-123", ValueOperator.MatchRegex, @"^\w+-\d+$", true },
        { "abc", ValueOperator.MatchRegex, @"^\d+$", false },
        { "abc", ValueOperator.NotMatchRegex, @"^\d+$", true },
        { "abc", ValueOperator.MatchRegex, "([a-", false },               // regex invalida sin validar: falla cerrado
        { "abc", ValueOperator.NotMatchRegex, "([a-", false },
        { "", ValueOperator.Empty, null, true },
        { null, ValueOperator.Empty, null, true },
        { " ", ValueOperator.NotEmpty, null, true },
    };

    [Theory]
    [MemberData(nameof(TextCases))]
    public void Texto(string? answer, ValueOperator op, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("text", AnswerValue.Text(answer), op, value));
    }

    [Theory]
    [InlineData("long_text")]
    [InlineData("email")]
    public void Los_otros_tipos_de_texto_usan_el_mismo_manejador(string type)
    {
        Assert.True(Eval(type, AnswerValue.Text("A"), ValueOperator.Equals, "a"));
    }

    [Theory]
    [InlineData(5, ValueOperator.Equals, "5.0", true)]
    [InlineData(5, ValueOperator.NotEquals, "5", false)]
    [InlineData(5, ValueOperator.GreaterThan, "4.9", true)]
    [InlineData(5, ValueOperator.GreaterThanOrEquals, "5", true)]
    [InlineData(5, ValueOperator.LessThan, "5", false)]
    [InlineData(5, ValueOperator.LessThanOrEquals, "5", true)]
    [InlineData(null, ValueOperator.LessThan, "100", false)]
    [InlineData(null, ValueOperator.NotEquals, "1", false)]
    [InlineData(null, ValueOperator.Empty, null, true)]
    [InlineData(0, ValueOperator.NotEmpty, null, true)]
    public void Numero(int? answer, ValueOperator op, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("number", AnswerValue.Number(answer), op, value));
    }

    [Theory]
    [InlineData(ValueOperator.Equals, "2026-11-16", true)]
    [InlineData(ValueOperator.GreaterThan, "2026-11-15", true)]
    [InlineData(ValueOperator.LessThan, "2026-11-15", false)]
    [InlineData(ValueOperator.LessThanOrEquals, "2026-11-16", true)]
    [InlineData(ValueOperator.Equals, "16/11/2026", false)]
    public void Fecha(ValueOperator op, string value, bool expected)
    {
        Assert.Equal(expected, Eval("date", AnswerValue.Date(new DateOnly(2026, 11, 16)), op, value));
    }

    [Theory]
    [InlineData(ValueOperator.Equals, "2026-11-16T09:30", true)]
    [InlineData(ValueOperator.GreaterThanOrEquals, "2026-11-16 09:30:00", true)]
    [InlineData(ValueOperator.LessThan, "2026-11-16T09:31", true)]
    [InlineData(ValueOperator.NotEquals, "2026-11-16T09:30", false)]
    public void Fecha_y_hora_de_pared(ValueOperator op, string value, bool expected)
    {
        var answer = AnswerValue.DateTime(new DateTime(2026, 11, 16, 9, 30, 0, DateTimeKind.Utc));   // el Kind se descarta
        Assert.Equal(expected, Eval("datetime", answer, op, value));
    }

    [Theory]
    [InlineData(ValueOperator.Equals, "09:30", true)]
    [InlineData(ValueOperator.GreaterThan, "09:29:59", true)]
    [InlineData(ValueOperator.LessThan, "09:00", false)]
    public void Hora(ValueOperator op, string value, bool expected)
    {
        Assert.Equal(expected, Eval("time", AnswerValue.Time(new TimeOnly(9, 30)), op, value));
    }

    [Theory]
    [InlineData("date")]
    [InlineData("datetime")]
    [InlineData("time")]
    public void Fecha_vacia_no_cumple_comparaciones_pero_si_empty(string type)
    {
        Assert.False(Eval(type, AnswerValue.None, ValueOperator.LessThan, type == "time" ? "23:59" : "2099-01-01T00:00"));
        Assert.True(Eval(type, AnswerValue.None, ValueOperator.Empty, null));
    }

    [Theory]
    [InlineData("equipo", ValueOperator.Equals, "EQUIPO", true)]
    [InlineData("equipo", ValueOperator.NotEquals, "acceso", true)]
    [InlineData("equipo", ValueOperator.Contains, "acceso, equipo", true)]
    [InlineData("equipo", ValueOperator.Contains, "acceso", false)]
    [InlineData("equipo", ValueOperator.NotContains, "acceso", true)]
    [InlineData(null, ValueOperator.Contains, "acceso", false)]
    [InlineData(null, ValueOperator.NotContains, "acceso", true)]
    [InlineData(null, ValueOperator.Empty, null, true)]
    [InlineData("", ValueOperator.Empty, null, true)]
    [InlineData("equipo", ValueOperator.NotEmpty, null, true)]
    public void Opcion_unica_por_clave(string? key, ValueOperator op, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("single_choice", AnswerValue.Choice(key), op, value));
    }

    public static TheoryData<string[], ValueOperator, string?, bool> MultipleCases => new()
    {
        { ["a", "b", "c"], ValueOperator.Equals, "c, b,a", true },          // conjunto sin orden
        { ["a", "b", "c"], ValueOperator.NotEquals, "a,b", true },
        { ["a", "b", "c"], ValueOperator.Contains, "a,d", false },
        { ["a", "b", "c"], ValueOperator.NotContains, "a,d", true },
        { ["a", "", "b"], ValueOperator.Equals, "a,b", true },              // se descartan claves vacias
        { ["a", "b"], ValueOperator.MatchRegex, "^[ab]$", true },           // todas coinciden
        { ["a", "z"], ValueOperator.MatchRegex, "^[ab]$", false },
        { ["a", "z"], ValueOperator.NotMatchRegex, "^z", false },           // ninguna debe coincidir
        { [], ValueOperator.MatchRegex, ".*", false },
        { [], ValueOperator.NotMatchRegex, ".*", true },
        { [], ValueOperator.Empty, null, true },
        { [""], ValueOperator.Empty, null, true },
        { ["a"], ValueOperator.NotEmpty, null, true },
        { ["a"], ValueOperator.MatchRegex, "([a-", false },
    };

    [Theory]
    [MemberData(nameof(MultipleCases))]
    public void Opcion_multiple_como_conjunto(string[] keys, ValueOperator op, string? value, bool expected)
    {
        Assert.Equal(expected, Eval("multiple_choice", AnswerValue.Choices(keys), op, value));
    }

    [Theory]
    [InlineData(0, ValueOperator.Empty, true)]
    [InlineData(2, ValueOperator.Empty, false)]
    [InlineData(2, ValueOperator.NotEmpty, true)]
    public void Archivos(int count, ValueOperator op, bool expected)
    {
        Assert.Equal(expected, Eval("file", AnswerValue.Files(count), op, null));
    }

    [Theory]
    [InlineData("file", ValueOperator.Equals)]
    [InlineData("single_choice", ValueOperator.GreaterThan)]
    [InlineData("tipo_desconocido", ValueOperator.Equals)]
    public void Operador_no_soportado_sin_validar_es_falso(string type, ValueOperator op)
    {
        Assert.False(Eval(type, AnswerValue.Text("x"), op, "x"));
    }

    [Fact]
    public void Un_producto_registra_su_propio_manejador()
    {
        var engine = new FormConditionEngine(ConditionHandlerRegistry.WithBuiltIns().Register("asset", new AssetHandler()));
        var form = Form(Question("activo", "asset"), Question("qt", "text", VisibleIf(Q("activo", ValueOperator.Equals, "Printer"))));

        Assert.True(engine.ComputeVisibility(form, Answers(("activo", AnswerValue.Text("Printer:42")))).Questions["qt"]);
        Assert.False(engine.ComputeVisibility(form, Answers(("activo", AnswerValue.Text("Monitor:7")))).Questions["qt"]);
        Assert.IsType<DefinitionFailure<FormModel>>(engine.ValidateDefinition(
            Form(Question("activo", "asset"), Question("qt", "text", VisibleIf(Q("activo", ValueOperator.Equals, "Silla"))))));
    }

    /// <summary>Manejador "is_type" de ejemplo: compara el tipo antes de los dos puntos.</summary>
    private sealed class AssetHandler : IConditionHandler
    {
        public IReadOnlySet<ValueOperator> SupportedOperators { get; } = new HashSet<ValueOperator> { ValueOperator.Equals };

        public bool IsEmpty(AnswerValue answer) => answer.IsEmpty;

        public bool Apply(AnswerValue answer, ValueOperator op, string? value) =>
            (answer.AsText ?? string.Empty).Split(':')[0] == value;

        public DefinitionError? ValidateValue(ValueOperator op, string? value) =>
            value is "Printer" or "Monitor" ? null : new DefinitionError("asset.unknown_type", "", $"Tipo '{value}' desconocido.");
    }

    [Fact]
    public void Valores_de_respuesta()
    {
        Assert.Equal(AnswerValue.None, AnswerValue.Text(null));
        Assert.Equal(AnswerValue.None, AnswerValue.Choice(null));
        Assert.Equal(AnswerValue.None, AnswerValue.Date(null));
        Assert.Equal(AnswerValue.None, AnswerValue.DateTime(null));
        Assert.Equal(AnswerValue.None, AnswerValue.Time(null));
        Assert.Equal(AnswerValue.Choices(["a", "b"]), AnswerValue.Choices(["a", "", "b"]));
        Assert.Equal(AnswerValue.Choices(["a"]).GetHashCode(), AnswerValue.Choices(["a"]).GetHashCode());
        Assert.Equal(["k"], AnswerValue.Choice("k").Keys);
        Assert.Equal(DateTimeKind.Unspecified, AnswerValue.DateTime(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)).AsDateTime!.Value.Kind);
        Assert.Equal(3, AnswerValue.Files(3).FileCount);
        Assert.Throws<ArgumentOutOfRangeException>(() => AnswerValue.Files(-1));
        Assert.Throws<ArgumentNullException>(() => AnswerValue.Choices(null!));
        Assert.Throws<ArgumentNullException>(() => FormAnswers.From(null!));
    }
}
