using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Electrical;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Camada avançada do KALIDIS. Complementa o BridgeService principal com
/// criação arquitetônica/MEP, famílias, vistas, materiais e gateway para
/// todos os comandos nativos disponíveis em PostableCommand.
/// </summary>
public static class MaxBridgeService
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private static DateTime _lastWriteUtc = DateTime.MinValue;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly HashSet<string> Actions = new(StringComparer.OrdinalIgnoreCase)
    {
        "listar_comandos_revit", "comando_revit",
        "espelhar", "definir_escala_vista", "criar_nivel", "criar_eixo",
        "criar_parede", "criar_piso", "criar_forro", "criar_ambiente",
        "carregar_familia", "inserir_familia", "criar_material", "atribuir_material",
        "criar_vista_3d", "duplicar_vista", "criar_folha",
        "criar_tubo", "criar_duto", "criar_eletroduto", "criar_bandeja"
    };

    public static void TryProcess(UIApplication uiApp)
    {
        if (!File.Exists(CommandPath)) return;
        DateTime write = File.GetLastWriteTimeUtc(CommandPath);
        if (write <= _lastWriteUtc) return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        MaxCommand? c;
        try { c = JsonSerializer.Deserialize<MaxCommand>(raw, JsonOptions); }
        catch { _lastWriteUtc = write; return; }

        if (c?.Acao == null || !Actions.Contains(c.Acao))
        {
            _lastWriteUtc = write;
            return;
        }

        MaxResult result;
        try { result = Execute(uiApp, c); }
        catch (Exception ex) { result = Fail(c.Id, $"Falha em {c.Acao}", ex.Message); }

        File.WriteAllText(ResultPath, JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine, new UTF8Encoding(false));
        _lastWriteUtc = write;
    }

    private static MaxResult Execute(UIApplication uiApp, MaxCommand c)
    {
        UIDocument? uiDoc = uiApp.ActiveUIDocument;
        string a = c.Acao!.Trim().ToLowerInvariant();

        if (a == "listar_comandos_revit") return ListNative(c);
        if (a == "comando_revit") return PostNative(uiApp, c);
        if (uiDoc == null) return Fail(c.Id, "Nenhum documento Revit ativo.");

        Document doc = uiDoc.Document;
        return a switch
        {
            "espelhar" => Mirror(doc, c),
            "definir_escala_vista" => SetViewScale(uiDoc, c),
            "criar_nivel" => CreateLevel(doc, c),
            "criar_eixo" => CreateGrid(doc, c),
            "criar_parede" => CreateWall(doc, c),
            "criar_piso" => CreateFloor(doc, c),
            "criar_forro" => CreateCeiling(doc, c),
            "criar_ambiente" => CreateRoom(doc, c),
            "carregar_familia" => LoadFamily(doc, c),
            "inserir_familia" => InsertFamily(doc, c),
            "criar_material" => CreateMaterial(doc, c),
            "atribuir_material" => AssignMaterial(doc, c),
            "criar_vista_3d" => Create3DView(doc, c),
            "duplicar_vista" => DuplicateView(uiDoc, c),
            "criar_folha" => CreateSheet(doc, c),
            "criar_tubo" => CreatePipe(doc, c),
            "criar_duto" => CreateDuct(doc, c),
            "criar_eletroduto" => CreateConduit(doc, c),
            "criar_bandeja" => CreateCableTray(doc, c),
            _ => Fail(c.Id, "Ação avançada não suportada.")
        };
    }

    private static MaxResult ListNative(MaxCommand c)
    {
        string filter = c.Busca ?? string.Empty;
        string[] names = Enum.GetNames(typeof(PostableCommand))
            .Where(n => filter.Length == 0 || n.Contains(filter, StringComparison.OrdinalIgnoreCase))
            .OrderBy(n => n).ToArray();
        return Ok(c.Id, $"{names.Length} comandos nativos disponíveis no enum PostableCommand.", names.Length, Array.Empty<long>(), names);
    }

    private static MaxResult PostNative(UIApplication uiApp, MaxCommand c)
    {
        string name = (c.Comando ?? c.Busca ?? string.Empty).Trim();
        if (!Enum.TryParse<PostableCommand>(name, true, out var pc)) return Fail(c.Id, $"PostableCommand desconhecido: {name}");
        RevitCommandId id = RevitCommandId.LookupPostableCommandId(pc);
        if (id == null) return Fail(c.Id, $"Sem RevitCommandId para {pc}.");
        if (!uiApp.CanPostCommand(id)) return Fail(c.Id, $"{pc} existe, mas não pode ser postado no contexto atual.");
        uiApp.PostCommand(id);
        return Ok(c.Id, $"Comando nativo {pc} enviado ao Revit.", 1);
    }

    private static MaxResult Mirror(Document doc, MaxCommand c)
    {
        var ids = ResolveIds(doc, c); if (ids.Count == 0) return Fail(c.Id, "Nenhum elemento para espelhar.");
        XYZ origin = P(c.X, c.Y, c.Z);
        XYZ normal = new(c.Nx ?? 1, c.Ny ?? 0, c.Nz ?? 0);
        if (normal.GetLength() < 1e-9) normal = XYZ.BasisX;
        Plane plane = Plane.CreateByNormalAndOrigin(normal.Normalize(), origin);
        using Transaction tx = new(doc, "KALIDIS - Espelhar"); tx.Start();
        ElementTransformUtils.MirrorElements(doc, ids, plane, c.Copiar ?? true); tx.Commit();
        return Ok(c.Id, $"{ids.Count} elemento(s) espelhado(s).", ids.Count, ids.Select(x => x.Value));
    }

    private static MaxResult SetViewScale(UIDocument uiDoc, MaxCommand c)
    {
        if (c.Escala == null || c.Escala < 1) return Fail(c.Id, "Informe 'escala' positiva.");
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Escala da vista"); tx.Start();
        uiDoc.ActiveView.Scale = c.Escala.Value; tx.Commit();
        return Ok(c.Id, $"Escala da vista definida para 1:{c.Escala}.", 1, new[] { uiDoc.ActiveView.Id.Value });
    }

    private static MaxResult CreateLevel(Document doc, MaxCommand c)
    {
        if (c.Elevacao == null) return Fail(c.Id, "Informe 'elevacao' em mm.");
        using Transaction tx = new(doc, "KALIDIS - Criar nível"); tx.Start();
        Level l = Level.Create(doc, Mm(c.Elevacao)); if (!string.IsNullOrWhiteSpace(c.Nome)) l.Name = c.Nome; tx.Commit();
        return Ok(c.Id, $"Nível criado: {l.Name}.", 1, new[] { l.Id.Value });
    }

    private static MaxResult CreateGrid(Document doc, MaxCommand c)
    {
        XYZ p1 = P(c.X, c.Y, c.Z); XYZ p2 = P(c.X2, c.Y2, c.Z2);
        if (p1.DistanceTo(p2) < 1e-9) return Fail(c.Id, "Informe dois pontos diferentes.");
        using Transaction tx = new(doc, "KALIDIS - Criar eixo"); tx.Start();
        Grid g = Grid.Create(doc, Line.CreateBound(p1, p2)); if (!string.IsNullOrWhiteSpace(c.Nome)) g.Name = c.Nome; tx.Commit();
        return Ok(c.Id, $"Eixo criado: {g.Name}.", 1, new[] { g.Id.Value });
    }

    private static MaxResult CreateWall(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        XYZ p1 = P(c.X, c.Y, c.Z); XYZ p2 = P(c.X2, c.Y2, c.Z2);
        using Transaction tx = new(doc, "KALIDIS - Criar parede"); tx.Start();
        Wall wall = Wall.Create(doc, Line.CreateBound(p1, p2), level.Id, c.Estrutural ?? false); tx.Commit();
        return Ok(c.Id, "Parede criada.", 1, new[] { wall.Id.Value });
    }

    private static MaxResult CreateFloor(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        FloorType? ft = FindType<FloorType>(doc, c.Tipo); if (ft == null) return Fail(c.Id, "Tipo de piso não encontrado.");
        CurveLoop? loop = Polygon(c.Pontos); if (loop == null) return Fail(c.Id, "Informe 'pontos' com pelo menos 3 pontos [x,y,z] em mm.");
        using Transaction tx = new(doc, "KALIDIS - Criar piso"); tx.Start();
        Floor floor = Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, level.Id); tx.Commit();
        return Ok(c.Id, "Piso criado.", 1, new[] { floor.Id.Value });
    }

    private static MaxResult CreateCeiling(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        CeilingType? ct = FindType<CeilingType>(doc, c.Tipo); if (ct == null) return Fail(c.Id, "Tipo de forro não encontrado.");
        CurveLoop? loop = Polygon(c.Pontos); if (loop == null) return Fail(c.Id, "Informe 'pontos' com pelo menos 3 pontos.");
        using Transaction tx = new(doc, "KALIDIS - Criar forro"); tx.Start();
        Ceiling ceiling = Ceiling.Create(doc, new List<CurveLoop> { loop }, ct.Id, level.Id); tx.Commit();
        return Ok(c.Id, "Forro criado.", 1, new[] { ceiling.Id.Value });
    }

    private static MaxResult CreateRoom(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        using Transaction tx = new(doc, "KALIDIS - Criar ambiente"); tx.Start();
        var room = doc.Create.NewRoom(level, new UV(Mm(c.X), Mm(c.Y))); if (!string.IsNullOrWhiteSpace(c.Nome)) room.Name = c.Nome; tx.Commit();
        return Ok(c.Id, $"Ambiente criado: {room.Name}.", 1, new[] { room.Id.Value });
    }

    private static MaxResult LoadFamily(Document doc, MaxCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Arquivo) || !File.Exists(c.Arquivo)) return Fail(c.Id, "Arquivo .rfa não encontrado.");
        using Transaction tx = new(doc, "KALIDIS - Carregar família"); tx.Start();
        bool loaded = doc.LoadFamily(c.Arquivo, out Family family); tx.Commit();
        return loaded ? Ok(c.Id, $"Família carregada: {family.Name}.", 1, new[] { family.Id.Value }) : Fail(c.Id, "Revit não carregou a família.");
    }

    private static MaxResult InsertFamily(Document doc, MaxCommand c)
    {
        FamilySymbol? fs = new FilteredElementCollector(doc).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(x => (string.IsNullOrWhiteSpace(c.Familia) || x.FamilyName.Contains(c.Familia, StringComparison.OrdinalIgnoreCase)) &&
                                 (string.IsNullOrWhiteSpace(c.Tipo) || x.Name.Contains(c.Tipo, StringComparison.OrdinalIgnoreCase)));
        if (fs == null) return Fail(c.Id, "Símbolo de família não encontrado.");
        using Transaction tx = new(doc, "KALIDIS - Inserir família"); tx.Start();
        if (!fs.IsActive) { fs.Activate(); doc.Regenerate(); }
        FamilyInstance fi = doc.Create.NewFamilyInstance(P(c.X, c.Y, c.Z), fs, StructuralType.NonStructural); tx.Commit();
        return Ok(c.Id, $"Família inserida: {fs.FamilyName} : {fs.Name}.", 1, new[] { fi.Id.Value });
    }

    private static MaxResult CreateMaterial(Document doc, MaxCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Nome)) return Fail(c.Id, "Informe 'nome'.");
        using Transaction tx = new(doc, "KALIDIS - Criar material"); tx.Start();
        ElementId id = Material.Create(doc, c.Nome); tx.Commit();
        return Ok(c.Id, $"Material criado: {c.Nome}.", 1, new[] { id.Value });
    }

    private static MaxResult AssignMaterial(Document doc, MaxCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Material)) return Fail(c.Id, "Informe 'material'.");
        Material? mat = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>()
            .FirstOrDefault(m => m.Name.Contains(c.Material, StringComparison.OrdinalIgnoreCase));
        if (mat == null) return Fail(c.Id, "Material não encontrado.");
        var elems = ResolveElements(doc, c); int changed = 0;
        using Transaction tx = new(doc, "KALIDIS - Atribuir material"); tx.Start();
        foreach (Element e in elems)
        {
            foreach (Parameter p in e.Parameters)
            {
                if (p.IsReadOnly || p.StorageType != StorageType.ElementId) continue;
                if (!p.Definition.Name.Contains("material", StringComparison.OrdinalIgnoreCase)) continue;
                try { if (p.Set(mat.Id)) { changed++; break; } } catch { }
            }
        }
        tx.Commit();
        return Ok(c.Id, $"Material '{mat.Name}' atribuído em {changed} elemento(s).", changed, elems.Select(e => e.Id.Value));
    }

    private static MaxResult Create3DView(Document doc, MaxCommand c)
    {
        ViewFamilyType? vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional);
        if (vft == null) return Fail(c.Id, "ViewFamilyType 3D não encontrado.");
        using Transaction tx = new(doc, "KALIDIS - Criar vista 3D"); tx.Start();
        View3D v = View3D.CreateIsometric(doc, vft.Id); if (!string.IsNullOrWhiteSpace(c.Nome)) v.Name = c.Nome; tx.Commit();
        return Ok(c.Id, $"Vista 3D criada: {v.Name}.", 1, new[] { v.Id.Value });
    }

    private static MaxResult DuplicateView(UIDocument uiDoc, MaxCommand c)
    {
        ViewDuplicateOption option = (c.Opcao ?? "duplicate").ToLowerInvariant() switch
        {
            "detalhamento" or "withdetailing" => ViewDuplicateOption.WithDetailing,
            "dependente" or "asdependent" => ViewDuplicateOption.AsDependent,
            _ => ViewDuplicateOption.Duplicate
        };
        using Transaction tx = new(uiDoc.Document, "KALIDIS - Duplicar vista"); tx.Start();
        ElementId id = uiDoc.ActiveView.Duplicate(option); View? v = uiDoc.Document.GetElement(id) as View; if (v != null && !string.IsNullOrWhiteSpace(c.Nome)) v.Name = c.Nome; tx.Commit();
        return Ok(c.Id, "Vista duplicada.", 1, new[] { id.Value });
    }

    private static MaxResult CreateSheet(Document doc, MaxCommand c)
    {
        FamilySymbol? tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>()
            .FirstOrDefault(x => string.IsNullOrWhiteSpace(c.Tipo) || x.Name.Contains(c.Tipo, StringComparison.OrdinalIgnoreCase) || x.FamilyName.Contains(c.Tipo, StringComparison.OrdinalIgnoreCase));
        using Transaction tx = new(doc, "KALIDIS - Criar folha"); tx.Start();
        ViewSheet sheet = ViewSheet.Create(doc, tb?.Id ?? ElementId.InvalidElementId); if (!string.IsNullOrWhiteSpace(c.Nome)) sheet.Name = c.Nome; if (!string.IsNullOrWhiteSpace(c.Numero)) sheet.SheetNumber = c.Numero; tx.Commit();
        return Ok(c.Id, $"Folha criada: {sheet.SheetNumber} - {sheet.Name}.", 1, new[] { sheet.Id.Value });
    }

    private static MaxResult CreatePipe(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); PipeType? type = FindType<PipeType>(doc, c.Tipo); PipingSystemType? sys = FindType<PipingSystemType>(doc, c.Sistema);
        if (level == null || type == null || sys == null) return Fail(c.Id, "Informe nível, tipo de tubo e sistema válidos.");
        using Transaction tx = new(doc, "KALIDIS - Criar tubo"); tx.Start(); Pipe p = Pipe.Create(doc, sys.Id, type.Id, level.Id, P(c.X,c.Y,c.Z), P(c.X2,c.Y2,c.Z2)); tx.Commit();
        return Ok(c.Id, "Tubo criado.", 1, new[] { p.Id.Value });
    }

    private static MaxResult CreateDuct(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); DuctType? type = FindType<DuctType>(doc, c.Tipo); MechanicalSystemType? sys = FindType<MechanicalSystemType>(doc, c.Sistema);
        if (level == null || type == null || sys == null) return Fail(c.Id, "Informe nível, tipo de duto e sistema válidos.");
        using Transaction tx = new(doc, "KALIDIS - Criar duto"); tx.Start(); Duct d = Duct.Create(doc, sys.Id, type.Id, level.Id, P(c.X,c.Y,c.Z), P(c.X2,c.Y2,c.Z2)); tx.Commit();
        return Ok(c.Id, "Duto criado.", 1, new[] { d.Id.Value });
    }

    private static MaxResult CreateConduit(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); ConduitType? type = FindType<ConduitType>(doc, c.Tipo); if (level == null || type == null) return Fail(c.Id, "Informe nível e tipo de eletroduto válidos.");
        using Transaction tx = new(doc, "KALIDIS - Criar eletroduto"); tx.Start(); Conduit x = Conduit.Create(doc, type.Id, P(c.X,c.Y,c.Z), P(c.X2,c.Y2,c.Z2), level.Id); tx.Commit();
        return Ok(c.Id, "Eletroduto criado.", 1, new[] { x.Id.Value });
    }

    private static MaxResult CreateCableTray(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); CableTrayType? type = FindType<CableTrayType>(doc, c.Tipo); if (level == null || type == null) return Fail(c.Id, "Informe nível e tipo de bandeja válidos.");
        using Transaction tx = new(doc, "KALIDIS - Criar bandeja"); tx.Start(); CableTray x = CableTray.Create(doc, type.Id, P(c.X,c.Y,c.Z), P(c.X2,c.Y2,c.Z2), level.Id); tx.Commit();
        return Ok(c.Id, "Bandeja criada.", 1, new[] { x.Id.Value });
    }

    private static List<Element> ResolveElements(Document doc, MaxCommand c)
    {
        if (c.ElementIds != null && c.ElementIds.Count > 0) return c.ElementIds.Select(x => doc.GetElement(new ElementId(x))).Where(x => x != null).Cast<Element>().ToList();
        if (string.IsNullOrWhiteSpace(c.Busca)) return new List<Element>();
        string n = c.Busca.ToLowerInvariant();
        return new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements().Where(e =>
            Contains(e.Category?.Name,n) || Contains(SafeName(e),n) ||
            (e is FamilyInstance fi && (Contains(fi.Symbol?.FamilyName,n) || Contains(fi.Symbol?.Name,n))) ||
            e.Parameters.Cast<Parameter>().Any(p => ParamContains(p,n))).ToList();
    }

    private static List<ElementId> ResolveIds(Document doc, MaxCommand c) => ResolveElements(doc,c).Select(e=>e.Id).ToList();
    private static bool ParamContains(Parameter p,string n) { try { return p.HasValue && Contains(p.AsValueString() ?? p.AsString(),n); } catch { return false; } }
    private static bool Contains(string? s,string n) => !string.IsNullOrWhiteSpace(s) && s.Contains(n,StringComparison.OrdinalIgnoreCase);
    private static string SafeName(Element e) { try { return e.Name ?? string.Empty; } catch { return string.Empty; } }

    private static Level? FindLevel(Document doc,string? name) => new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(x => string.IsNullOrWhiteSpace(name) || x.Name.Contains(name,StringComparison.OrdinalIgnoreCase));
    private static T? FindType<T>(Document doc,string? name) where T : ElementType => new FilteredElementCollector(doc).OfClass(typeof(T)).Cast<T>().FirstOrDefault(x => string.IsNullOrWhiteSpace(name) || x.Name.Contains(name,StringComparison.OrdinalIgnoreCase));

    private static CurveLoop? Polygon(List<List<double>>? points)
    {
        if (points == null || points.Count < 3) return null; var xyz = points.Select(a => new XYZ(Mm(a.ElementAtOrDefault(0)),Mm(a.ElementAtOrDefault(1)),Mm(a.ElementAtOrDefault(2)))).ToList();
        CurveLoop loop = new(); for (int i=0;i<xyz.Count;i++) loop.Append(Line.CreateBound(xyz[i],xyz[(i+1)%xyz.Count])); return loop;
    }
    private static XYZ P(double? x,double? y,double? z) => new(Mm(x),Mm(y),Mm(z));
    private static double Mm(double? v) => UnitUtils.ConvertToInternalUnits(v ?? 0, UnitTypeId.Millimeters);

    private static MaxResult Ok(string? id,string msg,int qty,IEnumerable<long>? ids=null,object? data=null) => new(id,true,msg,qty,ids?.Take(500).ToArray() ?? Array.Empty<long>(),null,data);
    private static MaxResult Fail(string? id,string msg,string? err=null) => new(id,false,msg,0,Array.Empty<long>(),err,null);
}

public sealed record MaxCommand(
    string? Id, string? Acao, string? Busca, List<long>? ElementIds,
    string? Comando, string? Nome, string? Numero, string? Tipo, string? Familia,
    string? Nivel, string? Sistema, string? Arquivo, string? Material, string? Opcao,
    double? X, double? Y, double? Z, double? X2, double? Y2, double? Z2,
    double? Nx, double? Ny, double? Nz, double? Elevacao, int? Escala,
    bool? Copiar, bool? Estrutural, List<List<double>>? Pontos);

public sealed record MaxResult(string? Id,bool Sucesso,string Mensagem,int Quantidade,IReadOnlyList<long> ElementIds,string? Erro,object? Dados);
