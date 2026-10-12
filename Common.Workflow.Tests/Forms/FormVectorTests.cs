using Common.Workflow.Forms;
using static Common.Workflow.Tests.Forms.FormFixture;

namespace Common.Workflow.Tests.Forms;

/// <summary>
/// Vectores V1..V11 de §12 del SRS CMN-C06, con los mismos datos.
/// </summary>
public class FormVectorTests
{
    [Fact]
    public void V1_visibilidad_converge_en_dos_pasadas()
    {
        var result = Engine.ComputeVisibility(F1, Answers(("q1", AnswerValue.Choice("equipo")), ("q2", AnswerValue.Text("Caminadora 3"))));

        Assert.True(result.Questions["q2"]);
        Assert.True(result.Questions["q3"]);
        Assert.False(result.Sections["s2"]);
        Assert.False(result.Questions["q4"]);
        Assert.True(result.Sections["s1"]);
        Assert.Equal(2, result.Iterations);
        Assert.True(result.Converged);
    }

    [Fact]
    public void V2_pregunta_oculta_con_respuesta_vieja_necesita_tres_pasadas()
    {
        var result = Engine.ComputeVisibility(F1, Answers(("q1", AnswerValue.Choice("acceso")), ("q2", AnswerValue.Text("Caminadora 3"))));

        Assert.False(result.Questions["q2"]);
        Assert.False(result.Questions["q3"]);   // sujeto oculto con not_empty: falso aunque q2 tenga texto
        Assert.True(result.Sections["s2"]);
        Assert.True(result.Questions["q4"]);
        Assert.Equal(3, result.Iterations);
        Assert.True(result.Converged);
    }

    public static TheoryData<bool[], LogicOperator[], bool> AndOverOrCases => new()
    {
        // [A=true, And B=false, Or C=true] -> (A and B) or C
        { [true, false, true], [LogicOperator.And, LogicOperator.And, LogicOperator.Or], true },
        // [A=true, And B=false, Or C=false]
        { [true, false, false], [LogicOperator.And, LogicOperator.And, LogicOperator.Or], false },
        // [A=false, Or B=true, And C=true] -> A or (B and C)
        { [false, true, true], [LogicOperator.And, LogicOperator.Or, LogicOperator.And], true },
    };

    [Theory]
    [MemberData(nameof(AndOverOrCases))]
    public void V3_and_tiene_precedencia_sobre_or(bool[] values, LogicOperator[] logic, bool expected)
    {
        // Cada condicion es "qa equals si" (verdadera) o "qa equals no" (falsa).
        var conditions = values.Select((v, i) => Q("qa", ValueOperator.Equals, v ? "si" : "no", logic[i])).ToArray();
        var form = Form(Question("qa", "text"), Question("qt", "text", VisibleIf(conditions)));

        var result = Engine.ComputeVisibility(form, Answers(("qa", AnswerValue.Text("si"))));

        Assert.Equal(expected, result.Questions["qt"]);
    }

    [Fact]
    public void V4_lista_vacia_es_falso_en_cada_estrategia()
    {
        var form = new FormModel("F", VisibilityRule.Always, [new SectionModel("s1", 1, VisibilityRule.Always)],
            [Question("visible_if", "text", VisibleIf()), Question("hidden_if", "text", HiddenIf())], [],
            [
                new DestinationModel("created_if", new CreationRule(CreationStrategy.CreatedIf, [])),
                new DestinationModel("created_unless", new CreationRule(CreationStrategy.CreatedUnless, [])),
            ]);

        var visibility = Engine.ComputeVisibility(form, FormAnswers.Empty);
        var destinations = Engine.ComputeDestinations(form, FormAnswers.Empty);

        Assert.False(visibility.Questions["visible_if"]);
        Assert.True(visibility.Questions["hidden_if"]);
        Assert.Equal(["created_unless"], destinations);
    }

    [Fact]
    public void V5_sujeto_oculto_cuenta_como_vacio()
    {
        var form = F1 with { Questions = [.. F1.Questions, Question("q5", "text", VisibleIf(Q("q2", ValueOperator.Empty)))] };

        var result = Engine.ComputeVisibility(form, Answers(("q1", AnswerValue.Choice("acceso")), ("q2", AnswerValue.Text("x"))));

        Assert.False(result.Questions["q2"]);
        Assert.True(result.Questions["q5"]);
    }

    [Fact]
    public void V6_validacion_condicional_devuelve_la_condicion_que_falla()
    {
        var condition = Q("q6", ValueOperator.MatchRegex, @"^[^@]+@hospital\.mx$");
        var form = Form(Question("q6", "email", validation: new ValidationRule(ValidationStrategy.ValidIf, [condition])));

        var invalid = Engine.ComputeValidation(form, Answers(("q6", AnswerValue.Text("x@gmail.com"))));
        var valid = Engine.ComputeValidation(form, Answers(("q6", AnswerValue.Text("ana@hospital.mx"))));

        Assert.Equal([new QuestionValidationError("q6", "validation", condition)], invalid.Errors);
        Assert.Same(condition, invalid.Errors[0].FailedCondition);
        Assert.True(valid.IsValid);
    }

    [Theory]
    [InlineData("equipo", "d1")]
    [InlineData("acceso", "d2")]
    public void V7_destinos_segun_la_respuesta(string q1, string expected)
    {
        Assert.Equal([expected], Engine.ComputeDestinations(F1, Answers(("q1", AnswerValue.Choice(q1)))));
    }

    [Fact]
    public void V8_numero_vacio_no_cumple_ninguna_comparacion()
    {
        var form = Form(Question("q7", "number"), Question("q8", "text", VisibleIf(Q("q7", ValueOperator.GreaterThan, "-1"))));

        var result = Engine.ComputeVisibility(form, Answers(("q7", AnswerValue.Number(null))));

        Assert.False(result.Questions["q8"]);
    }

    [Fact]
    public void V9_sin_convergencia_falla_cerrado()
    {
        // q9 sola oscila con periodo 2: tras 100 pasadas (par) ya queda oculta por la oscilacion y la prueba no
        // detectaria quitar el "falla cerrado". q9b va ANTES y las pasadas actualizan en sitio, asi que sigue a q9
        // con una pasada de retraso: al final de la pasada 100 la oscilacion deja q9b visible, y solo el "falla
        // cerrado" la oculta.
        var form = Form(
            Question("q9b", "text", VisibleIf(Q("q9", ValueOperator.Visible))),
            Question("q9", "text", VisibleIf(Q("q9", ValueOperator.NotVisible))));

        var result = Engine.ComputeVisibility(form, FormAnswers.Empty);

        Assert.False(result.Converged);
        Assert.Equal(100, result.Iterations);
        Assert.False(result.Questions["q9"]);
        Assert.False(result.Questions["q9b"]);
    }

    [Fact]
    public void V10_ciclo_de_visibilidad_al_guardar()
    {
        var form = Form(
            Question("q3", "text", VisibleIf(Q("q5", ValueOperator.Visible))),
            Question("q5", "text", VisibleIf(Q("q3", ValueOperator.NotEmpty))));

        var failure = Assert.IsType<DefinitionFailure<FormModel>>(Engine.ValidateDefinition(form));

        var error = Assert.Single(failure.Errors);
        Assert.Equal("forms.visibility_cycle", error.Code);
        Assert.Contains("q3 → q5 → q3", error.Message);
    }

    [Theory]
    [InlineData(ValueOperator.Contains, "a,c", true)]
    [InlineData(ValueOperator.Equals, "a,b", false)]
    [InlineData(ValueOperator.NotMatchRegex, "^z", true)]
    public void V11_multivaluado_por_claves_separadas_por_coma(ValueOperator op, string value, bool expected)
    {
        var form = Form(Question("q10", "multiple_choice"), Question("qt", "text", VisibleIf(Q("q10", op, value))));

        var result = Engine.ComputeVisibility(form, Answers(("q10", AnswerValue.Choices(["a", "b", "c"]))));

        Assert.Equal(expected, result.Questions["qt"]);
    }
}
