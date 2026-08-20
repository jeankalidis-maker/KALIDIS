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

    /// <summary>
    /// Roteamento leve: apenas identifica se o comando atual pertence à camada Max.
    /// Não executa nenhuma operação no modelo.
    /// </summary>
    public static bool IsCommand()
    {
        try
        {
            if (!File.Exists(CommandPath)) return false;
            string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
            MaxCommand? c = JsonSerializer.Deserialize<MaxCommand>(raw, JsonOptions);
            return c?.Acao != null && Actions.Contains(c.Acao);
        }
        catch
        {
            return false;
        }
    }

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
        var commands = RevitNativeCommandService.ListAvailable();
        return Ok(c.Id, $"{commands.Count} comandos nativos disponíveis.", null, commands);
    }

    private static MaxResult PostNative(UIApplication uiApp, MaxCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Comando)) return Fail(c.Id, "Informe 'comando'.");
        bool ok = RevitNativeCommandService.TryPost(uiApp, c.Comando, out string message);
        return ok ? Ok(c.Id, message) : Fail(c.Id, message);
    }

    private static MaxResult Mirror(Document doc, MaxCommand c)
    {
        if (c.ElementIds == null || c.ElementIds.Count == 0) return Fail(c.Id, "elementIds obrigatório.");
        XYZ a = Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), b = Mm(c.X2 ?? 1000, c.Y2 ?? 0, c.Z2 ?? 0);
        Plane plane = Plane.CreateByNormalAndOrigin((b - a).CrossProduct(XYZ.BasisZ).Normalize(), a);
        using Transaction t = new(doc, "KALIDIS - Espelhar");
        t.Start();
        var ids = ElementTransformUtils.MirrorElements(doc, c.ElementIds.Select(id => new ElementId(id)).ToList(), plane, c.Copiar ?? true);
        t.Commit();
        return Ok(c.Id, "Espelhamento concluído.", ids?.Select(x => x.Value).ToArray());
    }

    private static MaxResult SetViewScale(UIDocument uiDoc, MaxCommand c)
    {
        int scale = c.Escala ?? 100;
        using Transaction t = new(uiDoc.Document, "KALIDIS - Escala da vista");
        t.Start(); uiDoc.ActiveView.Scale = scale; t.Commit();
        return Ok(c.Id, $"Escala definida para 1:{scale}.", new[] { uiDoc.ActiveView.Id.Value });
    }

    private static MaxResult CreateLevel(Document doc, MaxCommand c)
    {
        double z = UnitUtils.ConvertToInternalUnits(c.Z ?? 0, UnitTypeId.Millimeters);
        using Transaction t = new(doc, "KALIDIS - Criar nível"); t.Start();
        Level level = Level.Create(doc, z); if (!string.IsNullOrWhiteSpace(c.Nome)) level.Name = c.Nome; t.Commit();
        return Ok(c.Id, "Nível criado.", new[] { level.Id.Value });
    }

    private static MaxResult CreateGrid(Document doc, MaxCommand c)
    {
        XYZ a = Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), b = Mm(c.X2 ?? 10000, c.Y2 ?? 0, c.Z2 ?? 0);
        using Transaction t = new(doc, "KALIDIS - Criar eixo"); t.Start(); Grid g = Grid.Create(doc, Line.CreateBound(a, b)); if (!string.IsNullOrWhiteSpace(c.Nome)) g.Name = c.Nome; t.Commit();
        return Ok(c.Id, "Eixo criado.", new[] { g.Id.Value });
    }

    private static MaxResult CreateWall(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        XYZ a = Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), b = Mm(c.X2 ?? 3000, c.Y2 ?? 0, c.Z2 ?? 0);
        double h = UnitUtils.ConvertToInternalUnits(c.Altura ?? 3000, UnitTypeId.Millimeters);
        using Transaction t = new(doc, "KALIDIS - Criar parede"); t.Start(); Wall w = Wall.Create(doc, Line.CreateBound(a, b), level.Id, false); Parameter? ph = w.get_Parameter(BuiltInParameter.WALL_USER_HEIGHT_PARAM); if (ph?.IsReadOnly == false) ph.Set(h); t.Commit();
        return Ok(c.Id, "Parede criada.", new[] { w.Id.Value });
    }

    private static MaxResult CreateFloor(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        CurveLoop loop = RectLoop(c); FloorType ft = new FilteredElementCollector(doc).OfClass(typeof(FloorType)).Cast<FloorType>().First();
        using Transaction t = new(doc, "KALIDIS - Criar piso"); t.Start(); Floor f = Floor.Create(doc, new List<CurveLoop> { loop }, ft.Id, level.Id); t.Commit();
        return Ok(c.Id, "Piso criado.", new[] { f.Id.Value });
    }

    private static MaxResult CreateCeiling(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        CeilingType ct = new FilteredElementCollector(doc).OfClass(typeof(CeilingType)).Cast<CeilingType>().First();
        using Transaction t = new(doc, "KALIDIS - Criar forro"); t.Start(); Ceiling f = Ceiling.Create(doc, new List<CurveLoop> { RectLoop(c) }, ct.Id, level.Id); t.Commit();
        return Ok(c.Id, "Forro criado.", new[] { f.Id.Value });
    }

    private static MaxResult CreateRoom(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        using Transaction t = new(doc, "KALIDIS - Criar ambiente"); t.Start(); var room = doc.Create.NewRoom(level, new UV(ToFt(c.X ?? 0), ToFt(c.Y ?? 0))); if (!string.IsNullOrWhiteSpace(c.Nome)) room.Name = c.Nome; if (!string.IsNullOrWhiteSpace(c.Numero)) room.Number = c.Numero; t.Commit();
        return Ok(c.Id, "Ambiente criado.", new[] { room.Id.Value });
    }

    private static MaxResult LoadFamily(Document doc, MaxCommand c)
    {
        if (string.IsNullOrWhiteSpace(c.Arquivo) || !File.Exists(c.Arquivo)) return Fail(c.Id, "Arquivo de família não encontrado.");
        using Transaction t = new(doc, "KALIDIS - Carregar família"); t.Start(); bool ok = doc.LoadFamily(c.Arquivo, out Family? family); t.Commit();
        return ok && family != null ? Ok(c.Id, "Família carregada.", new[] { family.Id.Value }) : Fail(c.Id, "Falha ao carregar família.");
    }

    private static MaxResult InsertFamily(Document doc, MaxCommand c)
    {
        FamilySymbol? s = FindSymbol(doc, c.Familia, c.Tipo); if (s == null) return Fail(c.Id, "Família/tipo não encontrado.");
        Level? level = FindLevel(doc, c.Nivel) ?? new FilteredElementCollector(doc).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(); if (level == null) return Fail(c.Id, "Nível não encontrado.");
        using Transaction t = new(doc, "KALIDIS - Inserir família"); t.Start(); if (!s.IsActive) s.Activate(); var fi = doc.Create.NewFamilyInstance(Mm(c.X ?? 0, c.Y ?? 0, c.Z ?? 0), s, level, StructuralType.NonStructural); t.Commit();
        return Ok(c.Id, "Família inserida.", new[] { fi.Id.Value });
    }

    private static MaxResult CreateMaterial(Document doc, MaxCommand c)
    {
        string name = string.IsNullOrWhiteSpace(c.Nome) ? "KALIDIS Material" : c.Nome;
        using Transaction t = new(doc, "KALIDIS - Criar material"); t.Start(); ElementId id = Material.Create(doc, name); Material m = (Material)doc.GetElement(id); if (c.R.HasValue && c.G.HasValue && c.B.HasValue) m.Color = new Color((byte)c.R.Value, (byte)c.G.Value, (byte)c.B.Value); t.Commit();
        return Ok(c.Id, "Material criado.", new[] { id.Value });
    }

    private static MaxResult AssignMaterial(Document doc, MaxCommand c)
    {
        if (c.ElementIds == null || c.ElementIds.Count == 0 || string.IsNullOrWhiteSpace(c.Material)) return Fail(c.Id, "elementIds e material são obrigatórios.");
        Material? m = new FilteredElementCollector(doc).OfClass(typeof(Material)).Cast<Material>().FirstOrDefault(x => x.Name.Equals(c.Material, StringComparison.OrdinalIgnoreCase)); if (m == null) return Fail(c.Id, "Material não encontrado.");
        int changed = 0; using Transaction t = new(doc, "KALIDIS - Atribuir material"); t.Start(); foreach (long id in c.ElementIds) { Element? e = doc.GetElement(new ElementId(id)); Parameter? p = e?.get_Parameter(BuiltInParameter.MATERIAL_ID_PARAM); if (p?.IsReadOnly == false) { p.Set(m.Id); changed++; } } t.Commit();
        return Ok(c.Id, $"Material atribuído a {changed} elemento(s).", c.ElementIds.ToArray());
    }

    private static MaxResult Create3DView(Document doc, MaxCommand c)
    {
        ViewFamilyType? vft = new FilteredElementCollector(doc).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().FirstOrDefault(x => x.ViewFamily == ViewFamily.ThreeDimensional); if (vft == null) return Fail(c.Id, "Tipo de vista 3D não encontrado.");
        using Transaction t = new(doc, "KALIDIS - Criar vista 3D"); t.Start(); View3D v = View3D.CreateIsometric(doc, vft.Id); if (!string.IsNullOrWhiteSpace(c.Nome)) v.Name = c.Nome; t.Commit(); return Ok(c.Id, "Vista 3D criada.", new[] { v.Id.Value });
    }

    private static MaxResult DuplicateView(UIDocument uiDoc, MaxCommand c)
    {
        using Transaction t = new(uiDoc.Document, "KALIDIS - Duplicar vista"); t.Start(); ElementId id = uiDoc.ActiveView.Duplicate(ViewDuplicateOption.WithDetailing); View v = (View)uiDoc.Document.GetElement(id); if (!string.IsNullOrWhiteSpace(c.Nome)) v.Name = c.Nome; t.Commit(); return Ok(c.Id, "Vista duplicada.", new[] { id.Value });
    }

    private static MaxResult CreateSheet(Document doc, MaxCommand c)
    {
        FamilySymbol? tb = new FilteredElementCollector(doc).OfCategory(BuiltInCategory.OST_TitleBlocks).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault();
        using Transaction t = new(doc, "KALIDIS - Criar folha"); t.Start(); ViewSheet s = ViewSheet.Create(doc, tb?.Id ?? ElementId.InvalidElementId); if (!string.IsNullOrWhiteSpace(c.Nome)) s.Name = c.Nome; if (!string.IsNullOrWhiteSpace(c.Numero)) s.SheetNumber = c.Numero; t.Commit(); return Ok(c.Id, "Folha criada.", new[] { s.Id.Value });
    }

    private static MaxResult CreatePipe(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); PipingSystemType? sys = new FilteredElementCollector(doc).OfClass(typeof(PipingSystemType)).Cast<PipingSystemType>().FirstOrDefault(); PipeType? type = new FilteredElementCollector(doc).OfClass(typeof(PipeType)).Cast<PipeType>().FirstOrDefault(); if (level == null || sys == null || type == null) return Fail(c.Id, "Configuração de tubulação incompleta.");
        using Transaction t = new(doc, "KALIDIS - Criar tubo"); t.Start(); Pipe p = Pipe.Create(doc, sys.Id, type.Id, level.Id, Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), Mm(c.X2 ?? 1000, c.Y2 ?? 0, c.Z2 ?? 0)); SetDiameter(p, c.Diametro); t.Commit(); return Ok(c.Id, "Tubo criado.", new[] { p.Id.Value });
    }

    private static MaxResult CreateDuct(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); MechanicalSystemType? sys = new FilteredElementCollector(doc).OfClass(typeof(MechanicalSystemType)).Cast<MechanicalSystemType>().FirstOrDefault(); DuctType? type = new FilteredElementCollector(doc).OfClass(typeof(DuctType)).Cast<DuctType>().FirstOrDefault(); if (level == null || sys == null || type == null) return Fail(c.Id, "Configuração de duto incompleta.");
        using Transaction t = new(doc, "KALIDIS - Criar duto"); t.Start(); Duct d = Duct.Create(doc, sys.Id, type.Id, level.Id, Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), Mm(c.X2 ?? 1000, c.Y2 ?? 0, c.Z2 ?? 0)); t.Commit(); return Ok(c.Id, "Duto criado.", new[] { d.Id.Value });
    }

    private static MaxResult CreateConduit(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); ConduitType? type = new FilteredElementCollector(doc).OfClass(typeof(ConduitType)).Cast<ConduitType>().FirstOrDefault(); if (level == null || type == null) return Fail(c.Id, "Configuração de eletroduto incompleta.");
        using Transaction t = new(doc, "KALIDIS - Criar eletroduto"); t.Start(); Conduit d = Conduit.Create(doc, type.Id, Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), Mm(c.X2 ?? 1000, c.Y2 ?? 0, c.Z2 ?? 0), level.Id); SetDiameter(d, c.Diametro); t.Commit(); return Ok(c.Id, "Eletroduto criado.", new[] { d.Id.Value });
    }

    private static MaxResult CreateCableTray(Document doc, MaxCommand c)
    {
        Level? level = FindLevel(doc, c.Nivel); CableTrayType? type = new FilteredElementCollector(doc).OfClass(typeof(CableTrayType)).Cast<CableTrayType>().FirstOrDefault(); if (level == null || type == null) return Fail(c.Id, "Configuração de bandeja incompleta.");
        using Transaction t = new(doc, "KALIDIS - Criar bandeja"); t.Start(); CableTray d = CableTray.Create(doc, type.Id, Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), Mm(c.X2 ?? 1000, c.Y2 ?? 0, c.Z2 ?? 0), level.Id); t.Commit(); return Ok(c.Id, "Bandeja criada.", new[] { d.Id.Value });
    }

    private static void SetDiameter(Element e, double? mm) { if (!mm.HasValue) return; Parameter? p = e.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM) ?? e.get_Parameter(BuiltInParameter.RBS_CONDUIT_DIAMETER_PARAM); if (p?.IsReadOnly == false) p.Set(ToFt(mm.Value)); }
    private static FamilySymbol? FindSymbol(Document d, string? family, string? type) => new FilteredElementCollector(d).OfClass(typeof(FamilySymbol)).Cast<FamilySymbol>().FirstOrDefault(s => (string.IsNullOrWhiteSpace(family) || s.FamilyName.Contains(family, StringComparison.OrdinalIgnoreCase)) && (string.IsNullOrWhiteSpace(type) || s.Name.Contains(type, StringComparison.OrdinalIgnoreCase)));
    private static Level? FindLevel(Document d, string? name) => new FilteredElementCollector(d).OfClass(typeof(Level)).Cast<Level>().FirstOrDefault(l => string.IsNullOrWhiteSpace(name) || l.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    private static CurveLoop RectLoop(MaxCommand c) { XYZ a = Mm(c.X1 ?? 0, c.Y1 ?? 0, c.Z1 ?? 0), b = Mm(c.X2 ?? 3000, c.Y2 ?? 3000, c.Z1 ?? 0); XYZ p1 = a, p2 = new(b.X, a.Y, a.Z), p3 = b, p4 = new(a.X, b.Y, a.Z); CurveLoop loop = new(); loop.Append(Line.CreateBound(p1,p2)); loop.Append(Line.CreateBound(p2,p3)); loop.Append(Line.CreateBound(p3,p4)); loop.Append(Line.CreateBound(p4,p1)); return loop; }
    private static XYZ Mm(double x, double y, double z) => new(ToFt(x), ToFt(y), ToFt(z));
    private static double ToFt(double mm) => UnitUtils.ConvertToInternalUnits(mm, UnitTypeId.Millimeters);
    private static MaxResult Ok(string? id, string m, long[]? ids = null, object? d = null) => new(id ?? Guid.NewGuid().ToString("N"), true, m, ids ?? Array.Empty<long>(), null, d);
    private static MaxResult Fail(string? id, string m, string? e = null) => new(id ?? Guid.NewGuid().ToString("N"), false, m, Array.Empty<long>(), e, null);

    private sealed record MaxCommand(string? Id, string? Acao, string? Comando, List<long>? ElementIds, double? X, double? Y, double? Z, double? X1, double? Y1, double? Z1, double? X2, double? Y2, double? Z2, double? Altura, double? Diametro, int? Escala, bool? Copiar, string? Nome, string? Numero, string? Nivel, string? Familia, string? Tipo, string? Arquivo, string? Material, int? R, int? G, int? B);
    private sealed record MaxResult(string Id, bool Sucesso, string Mensagem, long[] ElementIds, string? Erro, object? Dados);
}
