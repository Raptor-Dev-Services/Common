using Common.Results;
using Common.Workflow;

namespace Common.Workflow.Tests;

public class DefinitionFailureTests
{
    [Fact]
    public void Lleva_todos_los_errores_y_es_fallo_de_validacion()
    {
        Result<int> result = new DefinitionFailure<int>(
        [
            new DefinitionError("calendar.segment_range", "Segments[0]", "fin antes del inicio"),
            new DefinitionError("calendar.no_working_time", "Segments", "sin horario"),
        ]);

        var failure = Assert.IsType<DefinitionFailure<int>>(result);
        Assert.IsAssignableFrom<IValidationFailure>(result);
        Assert.Equal(2, failure.Errors.Count);
        Assert.Contains("Segments[0]: calendar.segment_range", failure.Message);
    }

    [Fact]
    public void Sin_errores_es_error_del_programador()
    {
        Assert.Throws<ArgumentException>(() => new DefinitionFailure<int>([]));
    }
}
