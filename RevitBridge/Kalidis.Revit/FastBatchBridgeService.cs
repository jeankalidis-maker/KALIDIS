using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Executa várias ações simples em um único ciclo do Idling.
/// Formato:
/// {
///   "id": "lote-1",
///   "acao": "lote",
///   "acoes": [
///     { "acao": "copiar", "elementIds": [123], "x": 1000, "y": 0, "z": 0 }
///   ]
/// }
/// </summary>
public static class FastBatchBridgeService
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string ProcessedPath = @"C:\KALIDIS\Bridge\comando.processado.json";

    private static DateTime _lastProcessedWriteUtc = DateTime.MinValue;

    public static bool IsBatchCommand()
    {
        try
        {
            if (!File.Exists(CommandPath)) return false;
            string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            return root.TryGetProperty("acao", out JsonElement acao) &&
                   string.Equals(acao.GetString(), "lote", StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public static void TryProcess(UIApplication uiApp)
    {
        UIDocument? uiDoc = uiApp.ActiveUIDocument;
        if (uiDoc == null || !File.Exists(CommandPath)) return;

        DateTime writeUtc = File.GetLastWriteTimeUtc(CommandPath);
        if (writeUtc <= _lastProcessedWriteUtc) return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        BatchCommand? batch;
        try
        {
            batch = JsonSerializer.Deserialize<BatchCommand>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            WriteResult(new BatchResult(null, false, "JSON de lote inválido.", 0, Array.Empty<long>(), ex.Message, Array.Empty<BatchItemResult>()));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        if (batch == null || !string.Equals(batch.Acao, "lote", StringComparison.OrdinalIgnoreCase)) return;
        if (batch.Acoes == null || batch.Acoes.Count == 0)
        {
            WriteResult(new BatchResult(batch.Id, false, "Lote sem ações.", 0, Array.Empty<long>(), null, Array.Empty<BatchItemResult>()));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        List<BatchItemResult> items = new();
        List<long> createdOrChangedIds = new();
        bool allOk = true;

        foreach (BatchAction action in batch.Acoes)
        {
            BatchItemResult item;
            try
            {
                item = Execute(uiDoc.Document, action);
            }
            catch (Exception ex)
            {
                item = new BatchItemResult(action.Id, action.Acao, false, "Falha ao executar ação do lote.", Array.Empty<long>(), ex.Message);
            }

            items.Add(item);
            createdOrChangedIds.AddRange(item.ElementIds);
            if (!item.Sucesso) allOk = false;
        }

        string message = allOk
            ? $"Lote concluído: {items.Count} ação(ões) executada(s)."
            : $"Lote concluído com falhas: {items.Count(x => x.Sucesso)}/{items.Count} ação(ões) com sucesso.";

        WriteResult(new BatchResult(
            batch.Id,
            allOk,
            message,
            createdOrChangedIds.Count,
            createdOrChangedIds.Take(500).ToArray(),
            null,
            items));

        File.WriteAllText(ProcessedPath, raw, new UTF8Encoding(false));
        _lastProcessedWriteUtc = writeUtc;
    }

    private static BatchItemResult Execute(Document doc, BatchAction action)
    {
        string acao = (action.Acao ?? string.Empty).Trim().ToLowerInvariant();
        return acao switch
        {
            "copiar" => Copy(doc, action),
            "mover" => Move(doc, action),
            "rotacionar" => Rotate(doc, action),
            _ => new BatchItemResult(action.Id, action.Acao, false, $"Ação de lote não suportada: {action.Acao}", Array.Empty<long>(), null)
        };
    }

    private static BatchItemResult Copy(Document doc, BatchAction action)
    {
        List<ElementId> sourceIds = ResolveIds(doc, action.ElementIds);
        if (sourceIds.Count == 0)
            return new BatchItemResult(action.Id, action.Acao, false, "Nenhum elemento válido para copiar.", Array.Empty<long>(), null);

        XYZ delta = new(Mm(action.X), Mm(action.Y), Mm(action.Z));
        List<ElementId> created = new();

        using Transaction tx = new(doc, "KALIDIS - Lote copiar");
        tx.Start();
        foreach (ElementId id in sourceIds)
        {
            try { created.AddRange(ElementTransformUtils.CopyElement(doc, id, delta)); }
            catch { }
        }
        tx.Commit();

        return new BatchItemResult(
            action.Id,
            action.Acao,
            created.Count > 0,
            $"{created.Count} cópia(s) criada(s).",
            created.Select(x => x.Value).ToArray(),
            created.Count > 0 ? null : "O Revit não criou nenhuma cópia.");
    }

    private static BatchItemResult Move(Document doc, BatchAction action)
    {
        List<ElementId> ids = ResolveIds(doc, action.ElementIds);
        XYZ delta = new(Mm(action.X), Mm(action.Y), Mm(action.Z));
        List<long> changed = new();

        using Transaction tx = new(doc, "KALIDIS - Lote mover");
        tx.Start();
        foreach (ElementId id in ids)
        {
            try { ElementTransformUtils.MoveElement(doc, id, delta); changed.Add(id.Value); }
            catch { }
        }
        tx.Commit();

        return new BatchItemResult(action.Id, action.Acao, changed.Count > 0,
            $"{changed.Count} elemento(s) movido(s).", changed.ToArray(), changed.Count > 0 ? null : "Nenhum elemento foi movido.");
    }

    private static BatchItemResult Rotate(Document doc, BatchAction action)
    {
        if (action.Angulo == null)
            return new BatchItemResult(action.Id, action.Acao, false, "Informe 'angulo' em graus.", Array.Empty<long>(), null);

        List<ElementId> ids = ResolveIds(doc, action.ElementIds);
        double radians = action.Angulo.Value * Math.PI / 180.0;
        List<long> changed = new();

        using Transaction tx = new(doc, "KALIDIS - Lote rotacionar");
        tx.Start();
        foreach (ElementId id in ids)
        {
            Element? e = doc.GetElement(id);
            if (e == null) continue;
            try
            {
                XYZ point = GetElementPoint(e);
                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, id, axis, radians);
                changed.Add(id.Value);
            }
            catch { }
        }
        tx.Commit();

        return new BatchItemResult(action.Id, action.Acao, changed.Count > 0,
            $"{changed.Count} elemento(s) rotacionado(s).", changed.ToArray(), changed.Count > 0 ? null : "Nenhum elemento foi rotacionado.");
    }

    private static List<ElementId> ResolveIds(Document doc, List<long>? values)
        => values == null
            ? new List<ElementId>()
            : values.Select(x => new ElementId(x)).Where(x => doc.GetElement(x) != null).ToList();

    private static XYZ GetElementPoint(Element e)
    {
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        BoundingBoxXYZ? bb = e.get_BoundingBox(null);
        return bb == null ? XYZ.Zero : (bb.Min + bb.Max) * 0.5;
    }

    private static double Mm(double? value)
        => UnitUtils.ConvertToInternalUnits(value ?? 0.0, UnitTypeId.Millimeters);

    private static void WriteResult(BatchResult result)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ResultPath)!);
        string json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(ResultPath, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true
    };
}

public sealed class BatchCommand
{
    public string? Id { get; set; }
    public string? Acao { get; set; }
    public List<BatchAction>? Acoes { get; set; }
}

public sealed class BatchAction
{
    public string? Id { get; set; }
    public string? Acao { get; set; }
    public List<long>? ElementIds { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    public double? Angulo { get; set; }
}

public sealed record BatchItemResult(
    string? Id,
    string? Acao,
    bool Sucesso,
    string Mensagem,
    IReadOnlyList<long> ElementIds,
    string? Erro);

public sealed record BatchResult(
    string? Id,
    bool Sucesso,
    string Mensagem,
    int Quantidade,
    IReadOnlyList<long> ElementIds,
    string? Erro,
    IReadOnlyList<BatchItemResult> Dados);
