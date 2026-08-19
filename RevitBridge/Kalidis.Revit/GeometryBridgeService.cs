using System.Text;
using System.Text.Json;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Roteia consultas geométricas ricas do comando.json para o GeometrySnapshotService.
/// Mantém a leitura do modelo no contexto seguro do evento Idling do Revit.
/// </summary>
public static class GeometryBridgeService
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private static DateTime _lastWriteUtc = DateTime.MinValue;

    private static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        "snapshot_elemento",
        "snapshot_ambiente",
        "analisar_proximidade",
        "detectar_aberturas_sem_cuba"
    };

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static void TryProcess(UIApplication uiApp)
    {
        if (!File.Exists(CommandPath)) return;
        DateTime write = File.GetLastWriteTimeUtc(CommandPath);
        if (write <= _lastWriteUtc) return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        MaxCommand? command;
        try { command = JsonSerializer.Deserialize<MaxCommand>(raw, JsonOptions); }
        catch { _lastWriteUtc = write; return; }

        if (command?.Acao == null || !Actions.Contains(command.Acao))
        {
            _lastWriteUtc = write;
            return;
        }

        MaxResult result;
        try
        {
            UIDocument? uiDoc = uiApp.ActiveUIDocument;
            if (uiDoc == null)
                result = new MaxResult(command.Id, false, "Nenhum documento Revit ativo.", 0, Array.Empty<long>(), null, null);
            else
                result = Execute(uiDoc, command);
        }
        catch (Exception ex)
        {
            result = new MaxResult(command.Id, false, $"Falha em {command.Acao}.", 0, Array.Empty<long>(), ex.Message, null);
        }

        File.WriteAllText(ResultPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        _lastWriteUtc = write;
    }

    private static MaxResult Execute(UIDocument uiDoc, MaxCommand c)
    {
        string action = c.Acao!.Trim().ToLowerInvariant();
        return action switch
        {
            "snapshot_elemento" => GeometrySnapshotService.SnapshotElement(uiDoc.Document, c),
            "snapshot_ambiente" => GeometrySnapshotService.SnapshotRoom(uiDoc.Document, c),
            "analisar_proximidade" => GeometrySnapshotService.AnalyzeProximity(uiDoc.Document, c),
            "detectar_aberturas_sem_cuba" => GeometrySnapshotService.DetectOpeningsWithoutSink(uiDoc.Document, c),
            _ => new MaxResult(c.Id, false, "Ação geométrica não suportada.", 0, Array.Empty<long>(), null, null)
        };
    }
}
