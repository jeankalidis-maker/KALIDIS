using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Lote encadeado para operações Revit simples. Cada etapa pode salvar os IDs
/// produzidos e etapas seguintes podem reutilizá-los por nome.
///
/// Exemplo:
/// {
///   "id":"exemplo",
///   "acao":"lote_encadeado",
///   "atomico":true,
///   "acoes":[
///     {"id":"copiar","acao":"copiar","elementIds":[123],"x":1000,"salvarComo":"novos"},
///     {"id":"girar","acao":"rotacionar","usarIdsDe":"novos","angulo":90},
///     {"id":"mover","acao":"mover","usarIdsDe":"novos","y":500},
///     {"id":"validar","acao":"validar_quantidade","usarIdsDe":"novos","quantidadeEsperada":1}
///   ]
/// }
/// </summary>
public static class SmartBatchBridgeService
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string Action = "lote_encadeado";
    private static DateTime _lastProcessedWriteUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };

    public static bool IsCommand()
    {
        try
        {
            if (!File.Exists(CommandPath)) return false;
            using JsonDocument json = JsonDocument.Parse(File.ReadAllText(CommandPath, Encoding.UTF8));
            return json.RootElement.TryGetProperty("acao", out JsonElement a) &&
                   string.Equals(a.GetString(), Action, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void TryProcess(UIApplication uiApp)
    {
        if (!File.Exists(CommandPath)) return;
        DateTime writeUtc = File.GetLastWriteTimeUtc(CommandPath);
        if (writeUtc <= _lastProcessedWriteUtc) return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        string? commandId = null;
        object result;

        try
        {
            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            commandId = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;

            if (!root.TryGetProperty("acao", out JsonElement acaoEl) ||
                !string.Equals(acaoEl.GetString(), Action, StringComparison.OrdinalIgnoreCase))
            {
                _lastProcessedWriteUtc = writeUtc;
                return;
            }

            UIDocument? uiDoc = uiApp.ActiveUIDocument;
            result = uiDoc == null
                ? Fail(commandId, "Nenhum documento Revit ativo.")
                : Execute(uiDoc.Document, root, commandId);
        }
        catch (Exception ex)
        {
            result = Fail(commandId, "Falha ao executar lote encadeado.", ex.Message);
        }

        File.WriteAllText(ResultPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        _lastProcessedWriteUtc = writeUtc;
    }

    private static object Execute(Document doc, JsonElement root, string? commandId)
    {
        if (!root.TryGetProperty("acoes", out JsonElement actionsEl) || actionsEl.ValueKind != JsonValueKind.Array)
            return Fail(commandId, "Lote encadeado sem ações.");

        bool atomic = !root.TryGetProperty("atomico", out JsonElement atomicEl) || atomicEl.ValueKind != JsonValueKind.False;
        Dictionary<string, List<ElementId>> vars = new(StringComparer.OrdinalIgnoreCase);
        List<StepResult> steps = new();
        List<long> changed = new();
        bool failed = false;
        string? failureMessage = null;

        using TransactionGroup group = new(doc, "KALIDIS - Lote encadeado");
        group.Start();

        foreach (JsonElement action in actionsEl.EnumerateArray())
        {
            string? stepId = GetString(action, "id");
            string name = (GetString(action, "acao") ?? string.Empty).Trim().ToLowerInvariant();
            bool required = !action.TryGetProperty("obrigatoria", out JsonElement reqEl) || reqEl.ValueKind != JsonValueKind.False;

            StepResult step;
            try
            {
                step = name switch
                {
                    "copiar" => Copy(doc, action, vars, stepId),
                    "mover" => Move(doc, action, vars, stepId),
                    "rotacionar" => Rotate(doc, action, vars, stepId),
                    "validar_quantidade" => ValidateCount(doc, action, vars, stepId),
                    _ => new StepResult(stepId, name, false, $"Ação encadeada não suportada: {name}", Array.Empty<long>(), null)
                };
            }
            catch (Exception ex)
            {
                step = new StepResult(stepId, name, false, "Falha na etapa.", Array.Empty<long>(), ex.Message);
            }

            steps.Add(step);
            changed.AddRange(step.ElementIds);

            string? saveAs = GetString(action, "salvarComo");
            if (step.Sucesso && !string.IsNullOrWhiteSpace(saveAs))
                vars[saveAs] = step.ElementIds.Select(x => new ElementId(x)).ToList();

            if (!step.Sucesso && required)
            {
                failed = true;
                failureMessage = $"Etapa obrigatória falhou: {stepId ?? name}.";
                if (atomic) break;
            }
        }

        if (failed && atomic)
        {
            group.RollBack();
            return new
            {
                id = commandId,
                sucesso = false,
                mensagem = failureMessage + " Lote revertido integralmente.",
                quantidade = 0,
                elementIds = Array.Empty<long>(),
                erro = (string?)null,
                dados = new { atomico = true, revertido = true, etapas = steps }
            };
        }

        group.Assimilate();
        return new
        {
            id = commandId,
            sucesso = !failed,
            mensagem = failed
                ? failureMessage + " Alterações anteriores foram mantidas porque atomico=false."
                : $"Lote encadeado concluído: {steps.Count} etapa(s).",
            quantidade = changed.Distinct().Count(),
            elementIds = changed.Distinct().Take(500).ToArray(),
            erro = (string?)null,
            dados = new
            {
                atomico = atomic,
                revertido = false,
                variaveis = vars.ToDictionary(k => k.Key, v => v.Value.Select(x => x.Value).ToArray()),
                etapas = steps
            }
        };
    }

    private static StepResult Copy(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> source = ResolveIds(doc, action, vars);
        if (source.Count == 0)
            return new StepResult(id, "copiar", false, "Nenhum elemento válido para copiar.", Array.Empty<long>(), null);

        XYZ delta = new(Mm(GetDouble(action, "x")), Mm(GetDouble(action, "y")), Mm(GetDouble(action, "z")));
        List<ElementId> created = new();

        using Transaction tx = new(doc, "KALIDIS - Encadeado copiar");
        tx.Start();
        foreach (ElementId sourceId in source)
            created.AddRange(ElementTransformUtils.CopyElement(doc, sourceId, delta));
        tx.Commit();

        return new StepResult(id, "copiar", created.Count > 0,
            $"{created.Count} cópia(s) criada(s).", created.Select(x => x.Value).ToArray(),
            created.Count > 0 ? null : "O Revit não criou cópias.");
    }

    private static StepResult Move(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        if (ids.Count == 0)
            return new StepResult(id, "mover", false, "Nenhum elemento válido para mover.", Array.Empty<long>(), null);

        XYZ delta = new(Mm(GetDouble(action, "x")), Mm(GetDouble(action, "y")), Mm(GetDouble(action, "z")));
        using Transaction tx = new(doc, "KALIDIS - Encadeado mover");
        tx.Start();
        foreach (ElementId elementId in ids)
            ElementTransformUtils.MoveElement(doc, elementId, delta);
        doc.Regenerate();
        tx.Commit();

        return new StepResult(id, "mover", true, $"{ids.Count} elemento(s) movido(s).", ids.Select(x => x.Value).ToArray(), null);
    }

    private static StepResult Rotate(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        if (ids.Count == 0)
            return new StepResult(id, "rotacionar", false, "Nenhum elemento válido para rotacionar.", Array.Empty<long>(), null);
        if (!TryGetDouble(action, "angulo", out double angle))
            return new StepResult(id, "rotacionar", false, "Informe angulo em graus.", Array.Empty<long>(), null);

        double radians = angle * Math.PI / 180.0;
        using Transaction tx = new(doc, "KALIDIS - Encadeado rotacionar");
        tx.Start();
        foreach (ElementId elementId in ids)
        {
            Element? element = doc.GetElement(elementId);
            if (element == null) continue;
            XYZ p = ElementPoint(element);
            Line axis = Line.CreateBound(p, p + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, elementId, axis, radians);
        }
        doc.Regenerate();
        tx.Commit();

        return new StepResult(id, "rotacionar", true, $"{ids.Count} elemento(s) rotacionado(s).", ids.Select(x => x.Value).ToArray(), null);
    }

    private static StepResult ValidateCount(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        long expected = GetLong(action, "quantidadeEsperada");
        bool ok = ids.Count == expected;
        return new StepResult(id, "validar_quantidade", ok,
            ok ? $"Quantidade validada: {ids.Count}." : $"Esperado {expected}, encontrado {ids.Count}.",
            ids.Select(x => x.Value).ToArray(), ok ? null : "Validação de quantidade falhou.");
    }

    private static List<ElementId> ResolveIds(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars)
    {
        string? use = GetString(action, "usarIdsDe");
        if (!string.IsNullOrWhiteSpace(use) && vars.TryGetValue(use, out List<ElementId>? saved))
            return saved.Where(x => doc.GetElement(x) != null).ToList();

        if (!action.TryGetProperty("elementIds", out JsonElement idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            return new List<ElementId>();

        return idsEl.EnumerateArray()
            .Where(x => x.TryGetInt64(out _))
            .Select(x => new ElementId(x.GetInt64()))
            .Where(x => doc.GetElement(x) != null)
            .ToList();
    }

    private static XYZ ElementPoint(Element e)
    {
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        BoundingBoxXYZ? bb = e.get_BoundingBox(null);
        return bb == null ? XYZ.Zero : (bb.Min + bb.Max) * 0.5;
    }

    private static string? GetString(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement p) && p.ValueKind == JsonValueKind.String ? p.GetString() : null;

    private static long GetLong(JsonElement e, string name)
        => e.TryGetProperty(name, out JsonElement p) && p.TryGetInt64(out long v) ? v : 0;

    private static double GetDouble(JsonElement e, string name)
        => TryGetDouble(e, name, out double v) ? v : 0.0;

    private static bool TryGetDouble(JsonElement e, string name, out double value)
    {
        value = 0;
        return e.TryGetProperty(name, out JsonElement p) && p.TryGetDouble(out value);
    }

    private static double Mm(double value)
        => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);

    private static object Fail(string? id, string message, string? error = null)
        => new { id, sucesso = false, mensagem = message, quantidade = 0, elementIds = Array.Empty<long>(), erro = error, dados = (object?)null };

    private sealed record StepResult(string? Id, string Acao, bool Sucesso, string Mensagem, long[] ElementIds, string? Erro);
}
