using System.Reflection;
using System.Reflection.Emit;
using Common.Workflow;

namespace Common.Workflow.Tests;

/// <summary>
/// ADR-0009: Common.Workflow es calculo puro. Solo depende de Common.Contracts y nunca lee el reloj:
/// el "ahora" entra siempre como parametro.
/// </summary>
public class ArchitectureTests
{
    private static readonly Assembly Workflow = typeof(DefinitionError).Assembly;

    [Fact]
    public void Solo_depende_de_Common_Contracts_y_del_runtime()
    {
        var ajenas = Workflow.GetReferencedAssemblies()
            .Select(a => a.Name!)
            .Where(n => n != "Common.Contracts" && n != "netstandard"
                        && !n.StartsWith("System", StringComparison.Ordinal)
                        && !n.StartsWith("Microsoft.Win32", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(ajenas);
    }

    [Fact]
    public void Ningun_metodo_lee_el_reloj()
    {
        var prohibidos = new HashSet<(Type, string)>
        {
            (typeof(DateTime), "get_UtcNow"), (typeof(DateTime), "get_Now"), (typeof(DateTime), "get_Today"),
            (typeof(DateTimeOffset), "get_UtcNow"), (typeof(DateTimeOffset), "get_Now"),
            (typeof(TimeProvider), "get_System"),
        };

        var violaciones = new List<string>();
        foreach (var type in Workflow.GetTypes())
        {
            const BindingFlags all = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance
                                     | BindingFlags.Static | BindingFlags.DeclaredOnly;
            var methods = type.GetMethods(all).Cast<MethodBase>().Concat(type.GetConstructors(all));
            foreach (var method in methods)
            {
                foreach (var called in CalledMethods(method))
                {
                    if (called.DeclaringType is { } t && prohibidos.Contains((t, called.Name)))
                        violaciones.Add($"{type.FullName}.{method.Name} -> {t.Name}.{called.Name}");
                }
            }
        }

        Assert.Empty(violaciones);
    }

    private static readonly Dictionary<short, OpCode> OpCodesByValue = typeof(OpCodes)
        .GetFields(BindingFlags.Public | BindingFlags.Static)
        .Select(f => (OpCode)f.GetValue(null)!)
        .ToDictionary(o => o.Value);

    // Recorre el IL del metodo y devuelve cada metodo invocado (call, callvirt, newobj, ldftn).
    private static IEnumerable<MethodBase> CalledMethods(MethodBase method)
    {
        var il = method.GetMethodBody()?.GetILAsByteArray();
        if (il is null) yield break;

        var i = 0;
        while (i < il.Length)
        {
            short value = il[i] == 0xFE ? (short)(0xFE00 | il[i + 1]) : il[i];
            var op = OpCodesByValue[value];
            i += op.Size;

            if (op.OperandType == OperandType.InlineMethod)
            {
                var token = BitConverter.ToInt32(il, i);
                MethodBase? called = null;
                try
                {
                    called = method.Module.ResolveMethod(token,
                        method.DeclaringType?.IsGenericType == true ? method.DeclaringType.GetGenericArguments() : null,
                        method.IsGenericMethod ? method.GetGenericArguments() : null);
                }
                catch (ArgumentException) { }
                if (called is not null) yield return called;
            }

            i += op.OperandType switch
            {
                OperandType.InlineNone => 0,
                OperandType.ShortInlineBrTarget or OperandType.ShortInlineI or OperandType.ShortInlineVar => 1,
                OperandType.InlineVar => 2,
                OperandType.InlineI8 or OperandType.InlineR => 8,
                OperandType.InlineSwitch => 4 + 4 * BitConverter.ToInt32(il, i),
                _ => 4,
            };
        }
    }
}
