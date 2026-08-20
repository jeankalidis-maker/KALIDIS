using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Lote encadeado robusto. Uma etapa pode salvar IDs e as próximas reutilizá-los.
/// Inclui TransactionGroup atômico, preflight de documento/elementos, proteção de
/// grupos/worksharing, tratamento de Pinned e validações pós-operação.
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
        PropertyNameCaseInsensitive = true,
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
            commandId = GetString(root, "id");

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
        RevitExecutionSafety.SafetyResult docCheck = RevitExecutionSafety.CheckDocument(doc, mutation: true);
        if (!docCheck.Success) return Fail(commandId, docCheck.Error ?? "Documento não editável.");

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
                    "validar_editabilidade" => ValidateEditability(doc, action, vars, stepId),
                    "descrever_elementos" => DescribeElements(doc, action, vars, stepId),
                    _ => new StepResult(stepId, name, false, $"Ação encadeada não suportada: {name}", Array.Empty<long>(), null, null)
                };
            }
            catch (Exception ex)
            {
                step = new StepResult(stepId, name, false, "Falha na etapa.", Array.Empty<long>(), ex.Message, null);
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
            return StepResult.Fail(id, "copiar", "Nenhum elemento válido para copiar.");

        RevitExecutionSafety.SafetyResult safety = RevitExecutionSafety.CheckElements(doc, source, allowGrouped: true);
        if (!safety.Success) return StepResult.Fail(id, "copiar", safety.Error ?? "Preflight falhou.");

        XYZ delta = new(Mm(GetDouble(action, "x")), Mm(GetDouble(action, "y")), Mm(GetDouble(action, "z")));
        List<ElementId> created = new();

        using Transaction tx = new(doc, "KALIDIS - Encadeado copiar");
        tx.Start();
        foreach (ElementId sourceId in source)
            created.AddRange(ElementTransformUtils.CopyElement(doc, sourceId, delta));
        doc.Regenerate();
        tx.Commit();

        object[] post = created.Select(x => RevitExecutionSafety.DescribeElement(doc, x)).ToArray();
        return new StepResult(id, "copiar", created.Count > 0,
            $"{created.Count} cópia(s) criada(s).", created.Select(x => x.Value).ToArray(),
            created.Count > 0 ? null : "O Revit não criou cópias.", new { post });
    }

    private static StepResult Move(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        if (ids.Count == 0) return StepResult.Fail(id, "mover", "Nenhum elemento válido para mover.");

        bool allowGrouped = GetBool(action, "permitirElementoEmGrupo", false);
        bool autoUnpin = GetBool(action, "desafixarAutomaticamente", true);
        RevitExecutionSafety.SafetyResult safety = RevitExecutionSafety.CheckElements(doc, ids, allowGrouped);
        if (!safety.Success) return StepResult.Fail(id, "mover", safety.Error ?? "Preflight falhou.");

        XYZ delta = new(Mm(GetDouble(action, "x")), Mm(GetDouble(action, "y")), Mm(GetDouble(action, "z")));
        List<(Element element, bool repin)> prepared = new();

        using Transaction tx = new(doc, "KALIDIS - Encadeado mover");
        tx.Start();
        foreach (ElementId elementId in ids)
        {
            Element element = doc.GetElement(elementId)!;
            if (!RevitExecutionSafety.TryPrepareForTransform(element, autoUnpin, out bool repin, out string? error))
                throw new InvalidOperationException(error);
            prepared.Add((element, repin));
            ElementTransformUtils.MoveElement(doc, elementId, delta);
        }
        doc.Regenerate();
        foreach ((Element element, bool repin) in prepared)
            RevitExecutionSafety.RestorePinned(element, repin);
        tx.Commit();

        object[] post = ids.Select(x => RevitExecutionSafety.DescribeElement(doc, x)).ToArray();
        return new StepResult(id, "mover", true, $"{ids.Count} elemento(s) movido(s).",
            ids.Select(x => x.Value).ToArray(), null,
            new { deltaMm = new { x = GetDouble(action, "x"), y = GetDouble(action, "y"), z = GetDouble(action, "z") }, post });
    }

    private static StepResult Rotate(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        if (ids.Count == 0) return StepResult.Fail(id, "rotacionar", "Nenhum elemento válido para rotacionar.");
        if (!TryGetDouble(action, "angulo", out double angle))
            return StepResult.Fail(id, "rotacionar", "Informe angulo em graus.");

        bool allowGrouped = GetBool(action, "permitirElementoEmGrupo", false);
        bool autoUnpin = GetBool(action, "desafixarAutomaticamente", true);
        RevitExecutionSafety.SafetyResult safety = RevitExecutionSafety.CheckElements(doc, ids, allowGrouped);
        if (!safety.Success) return StepResult.Fail(id, "rotacionar", safety.Error ?? "Preflight falhou.");

        double radians = angle * Math.PI / 180.0;
        List<(Element element, bool repin)> prepared = new();

        using Transaction tx = new(doc, "KALIDIS - Encadeado rotacionar");
        tx.Start();
        foreach (ElementId elementId in ids)
        {
            Element element = doc.GetElement(elementId)!;
            if (!RevitExecutionSafety.TryPrepareForTransform(element, autoUnpin, out bool repin, out string? error))
                throw new InvalidOperationException(error);
            prepared.Add((element, repin));

            XYZ p = ElementPoint(element);
            Line axis = Line.CreateBound(p, p + XYZ.BasisZ);
            ElementTransformUtils.RotateElement(doc, elementId, axis, radians);
        }
        doc.Regenerate();
        foreach ((Element element, bool repin) in prepared)
            RevitExecutionSafety.RestorePinned(element, repin);
        tx.Commit();

        object[] post = ids.Select(x => RevitExecutionSafety.DescribeElement(doc, x)).ToArray();
        return new StepResult(id, "rotacionar", true, $"{ids.Count} elemento(s) rotacionado(s).",
            ids.Select(x => x.Value).ToArray(), null, new { angulo = angle, post });
    }

    private static StepResult ValidateCount(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        long expected = GetLong(action, "quantidadeEsperada");
        bool ok = ids.Count == expected;
        return new StepResult(id, "validar_quantidade", ok,
            ok ? $"Quantidade validada: {ids.Count}." : $"Esperado {expected}, encontrado {ids.Count}.",
            ids.Select(x => x.Value).ToArray(), ok ? null : "Validação de quantidade falhou.",
            new { esperado = expected, encontrado = ids.Count });
    }

    private static StepResult ValidateEditability(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        bool allowGrouped = GetBool(action, "permitirElementoEmGrupo", false);
        RevitExecutionSafety.SafetyResult safety = RevitExecutionSafety.CheckElements(doc, ids, allowGrouped);
        return new StepResult(id, "validar_editabilidade", safety.Success,
            safety.Success ? $"{ids.Count} elemento(s) validados para edição." : "Validação de editabilidade falhou.",
            ids.Select(x => x.Value).ToArray(), safety.Error,
            new { elementos = ids.Select(x => RevitExecutionSafety.DescribeElement(doc, x)).ToArray() });
    }

    private static StepResult DescribeElements(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars, string? id)
    {
        List<ElementId> ids = ResolveIds(doc, action, vars);
        return new StepResult(id, "descrever_elementos", true, $"{ids.Count} elemento(s) descrito(s).",
            ids.Select(x => x.Value).ToArray(), null,
            new { elementos = ids.Select(x => RevitExecutionSafety.DescribeElement(doc, x)).ToArray() });
    }

    private static List<ElementId> ResolveIds(Document doc, JsonElement action, Dictionary<string, List<ElementId>> vars)
    {
        string? use = GetString(action, "usarIdsDe");
        if (!string.IsNullOrWhiteSpace(use) && vars.TryGetValue(use, out List<ElementId>? saved))
            return saved.Where(x => doc.GetElement(x) != null).Distinct().ToList();

        if (!action.TryGetProperty("elementIds", out JsonElement idsEl) || idsEl.ValueKind != JsonValueKind.Array)
            return new List<ElementId>();

        return idsEl.EnumerateArray()
            .Where(x => x.TryGetInt64(out _))
            .Select(x => new ElementId(x.GetInt64()))
            .Where(x => doc.GetElement(x) != null)
            .Distinct()
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

    private static bool GetBool(JsonElement e, string name, bool defaultValue)
        => e.TryGetProperty(name, out JsonElement p) && (p.ValueKind == JsonValueKind.True || p.ValueKind == JsonValueKind.False)
            ? p.GetBoolean()
            : defaultValue;

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

    private sealed record StepResult(
        string? Id,
        string Acao,
        bool Sucesso,
        string Mensagem,
        long[] ElementIds,
        string? Erro,
        object? Dados)
    {
        public static StepResult Fail(string? id, string action, string error)
            => new(id, action, false, error, Array.Empty<long>(), error, null);
    }
}
