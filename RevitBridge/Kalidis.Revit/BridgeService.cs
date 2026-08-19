using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

public static class BridgeService
{
    private const string BridgeFolder = @"C:\KALIDIS\Bridge";
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string ProcessedPath = @"C:\KALIDIS\Bridge\comando.processado.json";

    private static DateTime _lastProcessedWriteUtc = DateTime.MinValue;

    public static void EnsureFiles()
    {
        Directory.CreateDirectory(BridgeFolder);

        if (!File.Exists(CommandPath))
        {
            File.WriteAllText(
                CommandPath,
                "{\n  \"id\": \"exemplo-1\",\n  \"acao\": \"selecionar\",\n  \"busca\": \"cuba\"\n}\n",
                new UTF8Encoding(false));
        }
    }

    public static void TryProcess(UIApplication uiApp)
    {
        EnsureFiles();

        UIDocument? uiDoc = uiApp.ActiveUIDocument;
        if (uiDoc == null || !File.Exists(CommandPath))
            return;

        DateTime writeUtc = File.GetLastWriteTimeUtc(CommandPath);
        if (writeUtc <= _lastProcessedWriteUtc)
            return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        if (string.IsNullOrWhiteSpace(raw))
            return;

        BridgeCommand? command;
        try
        {
            command = JsonSerializer.Deserialize<BridgeCommand>(raw, JsonOptions);
        }
        catch (Exception ex)
        {
            WriteResult(new BridgeResult(null, false, "JSON inválido", 0, Array.Empty<long>(), ex.Message));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        if (command == null || string.IsNullOrWhiteSpace(command.Acao))
        {
            WriteResult(new BridgeResult(command?.Id, false, "Comando sem ação.", 0, Array.Empty<long>(), null));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        BridgeResult result = command.Acao.Trim().ToLowerInvariant() switch
        {
            "selecionar" => Select(uiDoc, command),
            "listar" => List(uiDoc.Document, command),
            _ => new BridgeResult(command.Id, false, $"Ação ainda não suportada: {command.Acao}", 0, Array.Empty<long>(), null)
        };

        WriteResult(result);
        File.WriteAllText(ProcessedPath, raw, new UTF8Encoding(false));
        _lastProcessedWriteUtc = writeUtc;
    }

    private static BridgeResult Select(UIDocument uiDoc, BridgeCommand command)
    {
        string term = (command.Busca ?? string.Empty).Trim();
        if (term.Length == 0)
            return new BridgeResult(command.Id, false, "Informe 'busca'.", 0, Array.Empty<long>(), null);

        List<Element> matches = FindElements(uiDoc.Document, term);
        List<ElementId> ids = matches.Select(e => e.Id).ToList();

        uiDoc.Selection.SetElementIds(ids);

        if (ids.Count > 0)
        {
            try { uiDoc.ShowElements(ids); } catch { }
        }

        return new BridgeResult(
            command.Id,
            true,
            ids.Count == 0
                ? $"Nenhum elemento encontrado para '{term}'."
                : $"{ids.Count} elemento(s) selecionado(s) para '{term}'.",
            ids.Count,
            ids.Take(200).Select(id => id.Value).ToArray(),
            null);
    }

    private static BridgeResult List(Document doc, BridgeCommand command)
    {
        string term = (command.Busca ?? string.Empty).Trim();
        if (term.Length == 0)
            return new BridgeResult(command.Id, false, "Informe 'busca'.", 0, Array.Empty<long>(), null);

        List<Element> matches = FindElements(doc, term);
        return new BridgeResult(
            command.Id,
            true,
            $"{matches.Count} elemento(s) encontrado(s) para '{term}'.",
            matches.Count,
            matches.Take(200).Select(e => e.Id.Value).ToArray(),
            null);
    }

    private static List<Element> FindElements(Document doc, string term)
    {
        string needle = term.ToLowerInvariant();

        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(e => Matches(e, needle))
            .ToList();
    }

    private static bool Matches(Element e, string needle)
    {
        if (Contains(e.Category?.Name, needle)) return true;
        if (Contains(SafeName(e), needle)) return true;

        if (e is FamilyInstance fi)
        {
            if (Contains(fi.Symbol?.FamilyName, needle)) return true;
            if (Contains(fi.Symbol?.Name, needle)) return true;
        }

        foreach (Parameter p in e.Parameters)
        {
            if (!p.HasValue) continue;
            string? value = null;
            try { value = p.AsValueString() ?? p.AsString(); } catch { }
            if (Contains(value, needle)) return true;
        }

        return false;
    }

    private static string SafeName(Element e)
    {
        try { return e.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) && value.ToLowerInvariant().Contains(needle);

    private static void WriteResult(BridgeResult result)
    {
        Directory.CreateDirectory(BridgeFolder);
        string json = JsonSerializer.Serialize(result, JsonOptions);
        File.WriteAllText(ResultPath, json + Environment.NewLine, new UTF8Encoding(false));
    }

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };
}

public sealed record BridgeCommand(string? Id, string? Acao, string? Busca);
public sealed record BridgeResult(string? Id, bool Sucesso, string Mensagem, int Quantidade, IReadOnlyList<long> ElementIds, string? Erro);
