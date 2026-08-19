using System.Globalization;
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
            WriteResult(Fail(null, "JSON inválido", ex.Message));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        if (command == null || string.IsNullOrWhiteSpace(command.Acao))
        {
            WriteResult(Fail(command?.Id, "Comando sem ação."));
            _lastProcessedWriteUtc = writeUtc;
            return;
        }

        BridgeResult result;
        try
        {
            result = Execute(uiApp, uiDoc, command);
        }
        catch (Exception ex)
        {
            result = Fail(command.Id, $"Falha ao executar '{command.Acao}'.", ex.Message);
        }

        WriteResult(result);
        File.WriteAllText(ProcessedPath, raw, new UTF8Encoding(false));
        _lastProcessedWriteUtc = writeUtc;
    }

    private static BridgeResult Execute(UIApplication uiApp, UIDocument uiDoc, BridgeCommand command)
    {
        string action = command.Acao!.Trim().ToLowerInvariant();

        return action switch
        {
            "selecionar" => Select(uiDoc, command),
            "listar" => List(uiDoc.Document, command),
            "info" => Info(uiDoc.Document, command),
            "inventario" => Inventory(uiDoc.Document, command),
            "alterar_parametro" => SetParameter(uiDoc.Document, command),
            "mover" => Move(uiDoc.Document, command),
            "rotacionar" => Rotate(uiDoc.Document, command),
            "copiar" => Copy(uiDoc.Document, command),
            "copiar_entre_projetos" => CopyBetweenProjects(uiApp, uiDoc.Document, command),
            "excluir" => Delete(uiDoc.Document, command),
            "trocar_tipo" => ChangeType(uiDoc.Document, command),
            "fixar" => SetPinned(uiDoc.Document, command, true),
            "desfixar" => SetPinned(uiDoc.Document, command, false),
            "ocultar_vista" => HideInView(uiDoc, command),
            "mostrar_vista" => UnhideInView(uiDoc, command),
            "isolar_temporario" => IsolateTemporary(uiDoc, command),
            "reset_isolamento" => ResetTemporaryIsolation(uiDoc, command),
            "regenerar" => Regenerate(uiDoc.Document, command),
            "salvar" => Save(uiDoc.Document, command),
            _ => Fail(command.Id, $"Ação ainda não suportada: {command.Acao}")
        };
    }

    private static BridgeResult Select(UIDocument uiDoc, BridgeCommand command)
    {
        List<Element> matches = ResolveElements(uiDoc.Document, command);
        List<ElementId> ids = matches.Select(e => e.Id).ToList();
        uiDoc.Selection.SetElementIds(ids);
        if (ids.Count > 0)
        {
            try { uiDoc.ShowElements(ids); } catch { }
        }
        return Ok(command.Id, ids.Count == 0 ? "Nenhum elemento encontrado." : $"{ids.Count} elemento(s) selecionado(s).", ids);
    }

    private static BridgeResult List(Document doc, BridgeCommand command)
    {
        List<Element> matches = ResolveElements(doc, command);
        return Ok(command.Id, $"{matches.Count} elemento(s) encontrado(s).", matches.Select(e => e.Id));
    }

    private static BridgeResult Info(Document doc, BridgeCommand command)
    {
        List<Element> matches = ResolveElements(doc, command).Take(100).ToList();
        var details = matches.Select(e => new ElementInfo(
            e.Id.Value,
            e.Category?.Name,
            SafeName(e),
            e.GetType().Name,
            e.GetTypeId().Value,
            e.Pinned,
            e is FamilyInstance fi ? fi.Symbol?.FamilyName : null,
            e is FamilyInstance fi2 ? fi2.Symbol?.Name : null)).ToArray();

        return new BridgeResult(command.Id, true, $"Informações de {details.Length} elemento(s).", details.Length,
            details.Select(x => x.ElementId).ToArray(), null, details);
    }

    private static BridgeResult Inventory(Document doc, BridgeCommand command)
    {
        InventoryResult inv = InventoryService.Generate(doc);
        return Ok(command.Id,
            $"Inventário gerado: {inv.ElementsWithCategory} elementos, {inv.CategoryCount} categorias, {inv.FamilyInstances} instâncias de famílias.",
            Array.Empty<ElementId>());
    }

    private static BridgeResult SetParameter(Document doc, BridgeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.Parametro)) return Fail(command.Id, "Informe 'parametro'.");
        if (command.Valor == null) return Fail(command.Id, "Informe 'valor'.");

        List<Element> targets = ResolveElements(doc, command);
        int changed = 0;
        using Transaction tx = new(doc, "KALIDIS - Alterar parâmetro");
        tx.Start();
        foreach (Element e in targets)
        {
            Parameter? p = e.LookupParameter(command.Parametro);
            if (p == null || p.IsReadOnly) continue;
            if (TrySetParameter(p, command.Valor, command.Unidade)) changed++;
        }
        tx.Commit();
        return Ok(command.Id, $"Parâmetro '{command.Parametro}' alterado em {changed} elemento(s).", targets.Select(e => e.Id));
    }

    private static bool TrySetParameter(Parameter p, string value, string? unit)
    {
        switch (p.StorageType)
        {
            case StorageType.String:
                return p.Set(value);
            case StorageType.Integer:
                if (int.TryParse(value, NumberStyles.Any, CultureInfo.InvariantCulture, out int i)) return p.Set(i);
                if (bool.TryParse(value, out bool b)) return p.Set(b ? 1 : 0);
                return false;
            case StorageType.Double:
                if (!double.TryParse(value.Replace(',', '.'), NumberStyles.Any, CultureInfo.InvariantCulture, out double d)) return false;
                if (string.Equals(unit, "mm", StringComparison.OrdinalIgnoreCase))
                    d = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.Millimeters);
                else if (string.Equals(unit, "m", StringComparison.OrdinalIgnoreCase))
                    d = UnitUtils.ConvertToInternalUnits(d, UnitTypeId.Meters);
                return p.Set(d);
            case StorageType.ElementId:
                if (long.TryParse(value, out long id)) return p.Set(new ElementId(id));
                return false;
            default:
                return false;
        }
    }

    private static BridgeResult Move(Document doc, BridgeCommand command)
    {
        List<Element> targets = ResolveElements(doc, command);
        double x = Mm(command.X), y = Mm(command.Y), z = Mm(command.Z);
        XYZ delta = new(x, y, z);
        using Transaction tx = new(doc, "KALIDIS - Mover");
        tx.Start();
        int changed = 0;
        foreach (Element e in targets)
        {
            try { ElementTransformUtils.MoveElement(doc, e.Id, delta); changed++; } catch { }
        }
        tx.Commit();
        return Ok(command.Id, $"{changed} elemento(s) movido(s) por X={command.X ?? 0} mm, Y={command.Y ?? 0} mm, Z={command.Z ?? 0} mm.", targets.Select(e => e.Id));
    }

    private static BridgeResult Rotate(Document doc, BridgeCommand command)
    {
        if (command.Angulo == null) return Fail(command.Id, "Informe 'angulo' em graus.");
        List<Element> targets = ResolveElements(doc, command);
        double radians = command.Angulo.Value * Math.PI / 180.0;
        int changed = 0;
        using Transaction tx = new(doc, "KALIDIS - Rotacionar");
        tx.Start();
        foreach (Element e in targets)
        {
            try
            {
                XYZ point = GetElementPoint(e);
                Line axis = Line.CreateBound(point, point + XYZ.BasisZ);
                ElementTransformUtils.RotateElement(doc, e.Id, axis, radians);
                changed++;
            }
            catch { }
        }
        tx.Commit();
        return Ok(command.Id, $"{changed} elemento(s) rotacionado(s) em {command.Angulo}°.", targets.Select(e => e.Id));
    }

    private static BridgeResult Copy(Document doc, BridgeCommand command)
    {
        List<Element> targets = ResolveElements(doc, command);
        XYZ delta = new(Mm(command.X), Mm(command.Y), Mm(command.Z));
        List<ElementId> created = new();
        using Transaction tx = new(doc, "KALIDIS - Copiar");
        tx.Start();
        foreach (Element e in targets)
        {
            try { created.AddRange(ElementTransformUtils.CopyElement(doc, e.Id, delta)); } catch { }
        }
        tx.Commit();
        return Ok(command.Id, $"{created.Count} cópia(s) criada(s).", created);
    }

    private static BridgeResult CopyBetweenProjects(UIApplication uiApp, Document targetDoc, BridgeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ArquivoOrigem)) return Fail(command.Id, "Informe 'arquivoOrigem'.");
        if (!File.Exists(command.ArquivoOrigem)) return Fail(command.Id, "Arquivo de origem não encontrado.");
        if (string.IsNullOrWhiteSpace(command.Busca)) return Fail(command.Id, "Informe 'busca' para localizar elementos no projeto de origem.");

        Document? sourceDoc = null;
        try
        {
            sourceDoc = uiApp.Application.OpenDocumentFile(command.ArquivoOrigem);
            List<ElementId> sourceIds = FindElements(sourceDoc, command.Busca).Select(e => e.Id).ToList();
            if (sourceIds.Count == 0) return Ok(command.Id, "Nenhum elemento encontrado no arquivo de origem.", Array.Empty<ElementId>());

            ICollection<ElementId> copied;
            using Transaction tx = new(targetDoc, "KALIDIS - Copiar entre projetos");
            tx.Start();
            copied = ElementTransformUtils.CopyElements(sourceDoc, sourceIds, targetDoc, Transform.Identity, new CopyPasteOptions());
            tx.Commit();
            return Ok(command.Id, $"{copied.Count} elemento(s) copiado(s) de outro projeto.", copied);
        }
        finally
        {
            if (sourceDoc != null && sourceDoc.IsValidObject)
            {
                try { sourceDoc.Close(false); } catch { }
            }
        }
    }

    private static BridgeResult Delete(Document doc, BridgeCommand command)
    {
        List<Element> targets = ResolveElements(doc, command);
        List<ElementId> ids = targets.Select(e => e.Id).ToList();
        ICollection<ElementId> deleted;
        using Transaction tx = new(doc, "KALIDIS - Excluir");
        tx.Start();
        deleted = ids.Count == 0 ? Array.Empty<ElementId>() : doc.Delete(ids);
        tx.Commit();
        return Ok(command.Id, $"{deleted.Count} elemento(s) removido(s), incluindo dependências do Revit quando aplicável.", deleted);
    }

    private static BridgeResult ChangeType(Document doc, BridgeCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.NovoTipo)) return Fail(command.Id, "Informe 'novoTipo'.");
        List<Element> targets = ResolveElements(doc, command);
        ElementType? targetType = new FilteredElementCollector(doc)
            .WhereElementIsElementType()
            .Cast<ElementType>()
            .FirstOrDefault(t => Contains(t.Name, command.NovoTipo!.ToLowerInvariant()) ||
                                 (t is FamilySymbol fs && Contains(fs.FamilyName, command.NovoTipo!.ToLowerInvariant())));
        if (targetType == null) return Fail(command.Id, $"Tipo não encontrado: {command.NovoTipo}");

        int changed = 0;
        using Transaction tx = new(doc, "KALIDIS - Trocar tipo");
        tx.Start();
        foreach (Element e in targets)
        {
            try { e.ChangeTypeId(targetType.Id); changed++; } catch { }
        }
        tx.Commit();
        return Ok(command.Id, $"Tipo alterado em {changed} elemento(s) para '{targetType.Name}'.", targets.Select(e => e.Id));
    }

    private static BridgeResult SetPinned(Document doc, BridgeCommand command, bool pinned)
    {
        List<Element> targets = ResolveElements(doc, command);
        int changed = 0;
        using Transaction tx = new(doc, pinned ? "KALIDIS - Fixar" : "KALIDIS - Desfixar");
        tx.Start();
        foreach (Element e in targets)
        {
            try { e.Pinned = pinned; changed++; } catch { }
        }
        tx.Commit();
        return Ok(command.Id, $"{changed} elemento(s) {(pinned ? "fixado(s)" : "desfixado(s)")}.", targets.Select(e => e.Id));
    }

    private static BridgeResult HideInView(UIDocument uiDoc, BridgeCommand command)
    {
        List<ElementId> ids = ResolveElements(uiDoc.Document, command).Select(e => e.Id).ToList();
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Ocultar na vista");
        tx.Start();
        if (ids.Count > 0) uiDoc.ActiveView.HideElements(ids);
        tx.Commit();
        return Ok(command.Id, $"{ids.Count} elemento(s) ocultado(s) na vista ativa.", ids);
    }

    private static BridgeResult UnhideInView(UIDocument uiDoc, BridgeCommand command)
    {
        List<ElementId> ids = ResolveElements(uiDoc.Document, command).Select(e => e.Id).ToList();
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Mostrar na vista");
        tx.Start();
        if (ids.Count > 0) uiDoc.ActiveView.UnhideElements(ids);
        tx.Commit();
        return Ok(command.Id, $"{ids.Count} elemento(s) exibido(s) na vista ativa.", ids);
    }

    private static BridgeResult IsolateTemporary(UIDocument uiDoc, BridgeCommand command)
    {
        List<ElementId> ids = ResolveElements(uiDoc.Document, command).Select(e => e.Id).ToList();
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Isolar temporariamente");
        tx.Start();
        if (ids.Count > 0) uiDoc.ActiveView.IsolateElementsTemporary(ids);
        tx.Commit();
        return Ok(command.Id, $"{ids.Count} elemento(s) isolado(s) temporariamente.", ids);
    }

    private static BridgeResult ResetTemporaryIsolation(UIDocument uiDoc, BridgeCommand command)
    {
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Reset isolamento");
        tx.Start();
        uiDoc.ActiveView.DisableTemporaryViewMode(TemporaryViewMode.TemporaryHideIsolate);
        tx.Commit();
        return Ok(command.Id, "Isolamento temporário da vista desativado.", Array.Empty<ElementId>());
    }

    private static BridgeResult Regenerate(Document doc, BridgeCommand command)
    {
        using Transaction tx = new(doc, "KALIDIS - Regenerar");
        tx.Start();
        doc.Regenerate();
        tx.Commit();
        return Ok(command.Id, "Documento regenerado.", Array.Empty<ElementId>());
    }

    private static BridgeResult Save(Document doc, BridgeCommand command)
    {
        if (string.IsNullOrWhiteSpace(doc.PathName)) return Fail(command.Id, "O documento ainda não possui caminho de salvamento.");
        doc.Save();
        return Ok(command.Id, "Documento salvo.", Array.Empty<ElementId>());
    }

    private static List<Element> ResolveElements(Document doc, BridgeCommand command)
    {
        if (command.ElementIds is { Count: > 0 })
        {
            return command.ElementIds
                .Select(id => doc.GetElement(new ElementId(id)))
                .Where(e => e != null)
                .Cast<Element>()
                .ToList();
        }

        string term = (command.Busca ?? string.Empty).Trim();
        if (term.Length == 0) return new List<Element>();
        return FindElements(doc, term);
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

    private static XYZ GetElementPoint(Element e)
    {
        if (e.Location is LocationPoint lp) return lp.Point;
        if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
        BoundingBoxXYZ? bb = e.get_BoundingBox(null);
        if (bb != null) return (bb.Min + bb.Max) * 0.5;
        return XYZ.Zero;
    }

    private static double Mm(double? value)
        => UnitUtils.ConvertToInternalUnits(value ?? 0.0, UnitTypeId.Millimeters);

    private static string SafeName(Element e)
    {
        try { return e.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static bool Contains(string? value, string needle)
        => !string.IsNullOrWhiteSpace(value) && value.ToLowerInvariant().Contains(needle);

    private static BridgeResult Ok(string? id, string message, IEnumerable<ElementId> ids)
    {
        long[] values = ids.Take(500).Select(x => x.Value).ToArray();
        return new BridgeResult(id, true, message, values.Length, values, null, null);
    }

    private static BridgeResult Ok(string? id, string message, IEnumerable<long> ids)
    {
        long[] values = ids.Take(500).ToArray();
        return new BridgeResult(id, true, message, values.Length, values, null, null);
    }

    private static BridgeResult Fail(string? id, string message, string? error = null)
        => new(id, false, message, 0, Array.Empty<long>(), error, null);

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

public sealed class BridgeCommand
{
    public string? Id { get; set; }
    public string? Acao { get; set; }
    public string? Busca { get; set; }
    public List<long>? ElementIds { get; set; }
    public string? Parametro { get; set; }
    public string? Valor { get; set; }
    public string? Unidade { get; set; }
    public double? X { get; set; }
    public double? Y { get; set; }
    public double? Z { get; set; }
    public double? Angulo { get; set; }
    public string? NovoTipo { get; set; }
    public string? ArquivoOrigem { get; set; }
}

public sealed record ElementInfo(long ElementId, string? Categoria, string Nome, string Classe, long TipoId, bool Fixado, string? Familia, string? Tipo);
public sealed record BridgeResult(string? Id, bool Sucesso, string Mensagem, int Quantidade, IReadOnlyList<long> ElementIds, string? Erro, object? Dados);
