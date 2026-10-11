using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Common.Workflow.Forms;
using static Common.Workflow.Tests.Forms.FormFixture;

namespace Common.Workflow.Tests.Forms;

/// <summary>
/// REQ-CMN-C06-QOS-002: los vectores publicados en <c>Common.Workflow/Forms/vectors.json</c> para el port
/// del front pasan tambien en el servidor, que es la autoridad.
/// </summary>
public class SharedVectorsTests
{
    private static JsonElement Load([CallerFilePath] string here = "")
    {
        var path = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here)!, "..", "..", "Common.Workflow", "Forms", "vectors.json"));
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        return doc.RootElement.Clone();
    }

    public static TheoryData<string> CaseIds()
    {
        var data = new TheoryData<string>();
        foreach (var c in Load().GetProperty("cases").EnumerateArray()) data.Add(c.GetProperty("id").GetString()!);
        return data;
    }

    [Fact]
    public void Cubre_los_once_vectores_del_srs()
    {
        var vectors = Load().GetProperty("cases").EnumerateArray()
            .Select(c => c.GetProperty("id").GetString()!.TrimEnd('a', 'b', 'c'))
            .ToHashSet();
        Assert.Equal(Enumerable.Range(1, 11).Select(i => $"V{i}").ToHashSet(), vectors);
    }

    [Theory]
    [MemberData(nameof(CaseIds))]
    public void El_servidor_cumple_el_vector(string id)
    {
        var root = Load();
        var vector = root.GetProperty("cases").EnumerateArray().Single(c => c.GetProperty("id").GetString() == id);
        var form = root.GetProperty("forms").GetProperty(vector.GetProperty("form").GetString()!).Deserialize<FormModel>()!;
        var answers = FormAnswers.From(vector.GetProperty("answers").EnumerateObject()
            .Select(p => KeyValuePair.Create(p.Name, ToAnswer(p.Value))));
        var expect = vector.GetProperty("expect");

        if (expect.TryGetProperty("visibility", out var visibility))
        {
            var result = Engine.ComputeVisibility(form, answers);
            if (visibility.TryGetProperty("questions", out var qs))
                foreach (var q in qs.EnumerateObject()) Assert.Equal(q.Value.GetBoolean(), result.Questions[q.Name]);
            if (visibility.TryGetProperty("sections", out var ss))
                foreach (var s in ss.EnumerateObject()) Assert.Equal(s.Value.GetBoolean(), result.Sections[s.Name]);
            if (visibility.TryGetProperty("iterations", out var it)) Assert.Equal(it.GetInt32(), result.Iterations);
            if (visibility.TryGetProperty("converged", out var cv)) Assert.Equal(cv.GetBoolean(), result.Converged);
        }
        if (expect.TryGetProperty("destinations", out var destinations))
            Assert.Equal(destinations.EnumerateArray().Select(d => d.GetString()!), Engine.ComputeDestinations(form, answers));
        if (expect.TryGetProperty("validation", out var validation))
        {
            var errors = Engine.ComputeValidation(form, answers).Errors;
            var expected = validation.EnumerateArray().Select(e => (
                e.GetProperty("questionId").GetString()!, e.GetProperty("code").GetString()!,
                e.GetProperty("conditionIndex").ValueKind == JsonValueKind.Null ? -1 : e.GetProperty("conditionIndex").GetInt32()));
            var actual = errors.Select(e => (e.QuestionId, e.Code, e.FailedCondition is null ? -1
                : form.Questions.Single(q => q.Id == e.QuestionId).Validation.Conditions.ToList().IndexOf(e.FailedCondition)));
            Assert.Equal(expected, actual);
        }
        if (expect.TryGetProperty("definitionErrors", out var definitionErrors))
        {
            var failure = Assert.IsType<DefinitionFailure<FormModel>>(Engine.ValidateDefinition(form));
            foreach (var e in definitionErrors.EnumerateArray())
                Assert.Contains(failure.Errors, a => a.Code == e.GetProperty("code").GetString()
                    && a.Message.Contains(e.GetProperty("messageContains").GetString()!, StringComparison.Ordinal));
        }
    }

    private static AnswerValue ToAnswer(JsonElement e)
    {
        var value = e.TryGetProperty("value", out var v) ? v : default;
        var inv = CultureInfo.InvariantCulture;
        return e.GetProperty("kind").GetString() switch
        {
            "Text" => AnswerValue.Text(value.GetString()),
            "Number" => AnswerValue.Number(value.GetDecimal()),
            "Choice" => AnswerValue.Choice(value.GetString()),
            "Choices" => AnswerValue.Choices(value.EnumerateArray().Select(x => x.GetString()!)),
            "Date" => AnswerValue.Date(DateOnly.ParseExact(value.GetString()!, "yyyy-MM-dd", inv)),
            "DateTime" => AnswerValue.DateTime(DateTime.ParseExact(value.GetString()!, "yyyy-MM-dd'T'HH:mm:ss", inv)),
            "Time" => AnswerValue.Time(TimeOnly.ParseExact(value.GetString()!, "HH:mm:ss", inv)),
            "Files" => AnswerValue.Files(value.GetInt32()),
            _ => AnswerValue.None,
        };
    }
}
