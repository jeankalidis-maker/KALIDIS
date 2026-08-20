using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using System.Globalization;
using System.Text;

namespace Kalidis.Revit;

/// <summary>
/// Leitura geométrica rica para dar ao KALIDIS uma "visão" estruturada do modelo.
/// Não altera o RVT. Todas as coordenadas e dimensões retornadas são em milímetros.
/// </summary>
public static class GeometrySnapshotService
{
    private const int MaxRoomElements = 220;
    private const int MaxParametersPerElement = 50;
    private const int MaxFacesPerElement = 24;

    public static MaxResult SnapshotElement(Document doc, MaxCommand c)
    {
        List<Element> elements = Resolve(doc, c).Take(40).ToList();
        if (elements.Count == 0)
            return Fail(c.Id, "Nenhum elemento encontrado para snapshot.");

        var data = elements.Select(e => BuildElementSnapshot(doc, e, true)).ToArray();
        return Ok(c.Id, $"Snapshot geométrico de {data.Length} elemento(s).", data.Length,
            elements.Select(e => e.Id.Value), data);
    }

    public static MaxResult SnapshotRoom(Document doc, MaxCommand c)
    {
        Room? room = FindRoom(doc, c.Busca);
        if (room == null)
            return Fail(c.Id, "Ambiente não encontrado. Use 'busca' com o nome, número ou ElementId do ambiente.");

        var elements = ElementsInAndNearRoom(doc, room)
            .Where(e => e.Id != room.Id)
            .Take(MaxRoomElements)
            .ToList();

        var roomData = new
        {
            elementId = room.Id.Value,
            nome = room.Name,
            numero = room.Number,
            nivel = doc.GetElement(room.LevelId)?.Name,
            areaM2 = InternalAreaToM2(room.Area),
            volumeM3 = room.Volume > 0 ? InternalVolumeToM3(room.Volume) : (double?)null,
            bbox = BBox(room.get_BoundingBox(null)),
            limites = RoomBoundary(room)
        };

        var elementData = elements.Select(e => BuildElementSnapshot(doc, e, true)).ToArray();

        var byCategory = elements
            .GroupBy(e => e.Category?.Name ?? "Sem categoria")
            .OrderByDescending(g => g.Count())
            .ToDictionary(g => g.Key, g => g.Count());

        var data = new
        {
            ambiente = roomData,
            quantidadeElementos = elements.Count,
            categorias = byCategory,
            elementos = elementData
        };

        return Ok(c.Id, $"Snapshot completo do ambiente '{room.Name}' com {elements.Count} elemento(s).",
            elements.Count, elements.Select(e => e.Id.Value), data);
    }

    public static MaxResult AnalyzeProximity(Document doc, MaxCommand c)
    {
        List<Element> targets = Resolve(doc, c).Take(20).ToList();
        if (targets.Count == 0)
            return Fail(c.Id, "Nenhum elemento-alvo encontrado.");

        double radiusMm = c.X is > 0 ? c.X.Value : 1000.0;
        var all = new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(e => e.Category != null)
            .ToList();

        var result = new List<object>();
        foreach (Element target in targets)
        {
            XYZ? p = RepresentativePoint(target);
            if (p == null) continue;

            var nearby = all
                .Where(e => e.Id != target.Id)
                .Select(e => new { element = e, point = RepresentativePoint(e) })
                .Where(x => x.point != null)
                .Select(x => new
                {
                    elementId = x.element.Id.Value,
                    categoria = x.element.Category?.Name,
                    nome = SafeName(x.element),
                    familia = x.element is FamilyInstance fi ? fi.Symbol?.FamilyName : null,
                    tipo = x.element is FamilyInstance fi2 ? fi2.Symbol?.Name : doc.GetElement(x.element.GetTypeId())?.Name,
                    distanciaMm = ToMm(p.DistanceTo(x.point!))
                })
                .Where(x => x.distanciaMm <= radiusMm)
                .OrderBy(x => x.distanciaMm)
                .Take(60)
                .ToArray();

            result.Add(new
            {
                alvo = BuildElementSnapshot(doc, target, false),
                raioMm = radiusMm,
                proximos = nearby
            });
        }

        return Ok(c.Id, $"Análise de proximidade concluída para {result.Count} elemento(s).", result.Count,
            targets.Select(e => e.Id.Value), result);
    }

    public static MaxResult DetectOpeningsWithoutSink(Document doc, MaxCommand c)
    {
        Room? room = FindRoom(doc, c.Busca);
        if (room == null)
            return Fail(c.Id, "Ambiente não encontrado. Informe nome, número ou ElementId em 'busca'.");

        double toleranceMm = c.X is > 0 ? c.X.Value : 220.0;
        List<Element> roomElements = ElementsInAndNearRoom(doc, room).ToList();

        var sinks = roomElements
            .Where(IsSinkLike)
            .Select(e => new { element = e, point = RepresentativePoint(e) })
            .Where(x => x.point != null)
            .Select(x => new
            {
                elementId = x.element.Id.Value,
                nome = SafeName(x.element),
                familia = x.element is FamilyInstance fi ? fi.Symbol?.FamilyName : null,
                tipo = x.element is FamilyInstance fi2 ? fi2.Symbol?.Name : null,
                ponto = Point(x.point!)
            })
            .ToList();

        var openingCandidates = new List<OpeningCandidate>();
        foreach (Element e in roomElements)
        {
            foreach (var opening in ExtractHorizontalInnerLoops(e))
            {
                openingCandidates.Add(new OpeningCandidate(
                    e.Id.Value,
                    e.Category?.Name,
                    SafeName(e),
                    e is FamilyInstance fi ? fi.Symbol?.FamilyName : null,
                    opening.Center,
                    opening.ElevationMm,
                    opening.PerimeterMm));
            }
        }

        var openings = openingCandidates
            .Select(o =>
            {
                var nearest = sinks
                    .Select(s => new
                    {
                        sink = s,
                        distance = Distance2D(o.Center.X, o.Center.Y,
                            Convert.ToDouble(s.ponto!.GetType().GetProperty("x")!.GetValue(s.ponto)),
                            Convert.ToDouble(s.ponto!.GetType().GetProperty("y")!.GetValue(s.ponto)))
                    })
                    .OrderBy(x => x.distance)
                    .FirstOrDefault();

                bool occupied = nearest != null && nearest.distance <= toleranceMm;
                return new
                {
                    elementoBancadaId = o.ElementId,
                    categoria = o.Category,
                    nome = o.Name,
                    familia = o.Family,
                    centro = new { x = Math.Round(o.Center.X, 2), y = Math.Round(o.Center.Y, 2), z = Math.Round(o.ElevationMm, 2) },
                    perimetroMm = Math.Round(o.PerimeterMm, 1),
                    ocupada = occupied,
                    cubaMaisProximaId = nearest?.sink.elementId,
                    distanciaCubaMm = nearest == null ? (double?)null : Math.Round(nearest.distance, 1)
                };
            })
            .OrderBy(x => x.elementoBancadaId)
            .ThenBy(x => x.centro.x)
            .ThenBy(x => x.centro.y)
            .ToArray();

        var empty = openings.Where(x => !x.ocupada).ToArray();
        var data = new
        {
            ambiente = new { room.Id.Value, room.Name, room.Number },
            toleranciaOcupacaoMm = toleranceMm,
            cubasDetectadas = sinks,
            aberturasDetectadas = openings,
            aberturasSemCuba = empty
        };

        return Ok(c.Id,
            $"Detectadas {openings.Length} abertura(s), sendo {empty.Length} sem cuba no ambiente '{room.Name}'.",
            empty.Length, empty.Select(x => x.elementoBancadaId).Distinct(), data);
    }

    private static object BuildElementSnapshot(Document doc, Element e, bool includeGeometry)
    {
        XYZ? p = RepresentativePoint(e);
        BoundingBoxXYZ? bb = null;
        try { bb = e.get_BoundingBox(null); } catch { }

        string? level = null;
        try
        {
            ElementId levelId = e.LevelId;
            if (levelId != ElementId.InvalidElementId)
                level = doc.GetElement(levelId)?.Name;
        }
        catch { }

        long? hostId = null;
        if (e is FamilyInstance fi && fi.Host != null) hostId = fi.Host.Id.Value;

        var parameters = new Dictionary<string, string?>();
        foreach (Parameter param in e.Parameters.Cast<Parameter>())
        {
            if (parameters.Count >= MaxParametersPerElement) break;
            if (!param.HasValue) continue;
            try
            {
                string name = param.Definition?.Name ?? "?";
                string? value = param.AsValueString() ?? param.AsString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    value = param.StorageType switch
                    {
                        StorageType.Integer => param.AsInteger().ToString(),
                        StorageType.Double => param.AsDouble().ToString("0.######"),
                        StorageType.ElementId => param.AsElementId().Value.ToString(),
                        _ => value
                    };
                }
                if (!string.IsNullOrWhiteSpace(value) && !parameters.ContainsKey(name))
                    parameters[name] = value;
            }
            catch { }
        }

        return new
        {
            elementId = e.Id.Value,
            uniqueId = e.UniqueId,
            categoria = e.Category?.Name,
            classe = e.GetType().Name,
            nome = SafeName(e),
            familia = e is FamilyInstance f1 ? f1.Symbol?.FamilyName : null,
            tipo = e is FamilyInstance f2 ? f2.Symbol?.Name : doc.GetElement(e.GetTypeId())?.Name,
            typeId = e.GetTypeId().Value,
            nivel = level,
            hostId,
            fixado = e.Pinned,
            ponto = p == null ? null : Point(p),
            bbox = BBox(bb),
            localizacao = LocationData(e),
            parametros = parameters,
            geometria = includeGeometry ? GeometryData(e) : null
        };
    }

    private static object? GeometryData(Element e)
    {
        try
        {
            Options options = new()
            {
                ComputeReferences = false,
                IncludeNonVisibleObjects = true,
                DetailLevel = ViewDetailLevel.Fine
            };
            GeometryElement? geometry = e.get_Geometry(options);
            if (geometry == null) return null;

            List<Solid> solids = new();
            CollectSolids(geometry, solids);
            var valid = solids.Where(s => s.Volume > 1e-9).ToList();

            var faces = new List<object>();
            foreach (Solid solid in valid)
            {
                foreach (Face face in solid.Faces)
                {
                    if (faces.Count >= MaxFacesPerElement) break;
                    if (face is not PlanarFace pf) continue;
                    EdgeArrayArray loops = pf.EdgeLoops;
                    if (loops.Size == 0) continue;
                    var loopData = LoopData(loops);
                    faces.Add(new
                    {
                        normal = PointVector(pf.FaceNormal),
                        origem = Point(pf.Origin),
                        areaM2 = InternalAreaToM2(pf.Area),
                        horizontal = Math.Abs(pf.FaceNormal.Z) > 0.95,
                        quantidadeLoops = loops.Size,
                        loops = loopData
                    });
                }
            }

            return new
            {
                quantidadeSolidos = valid.Count,
                volumeM3 = Math.Round(valid.Sum(s => InternalVolumeToM3(s.Volume)), 6),
                areaSuperficialM2 = Math.Round(valid.Sum(s => InternalAreaToM2(s.SurfaceArea)), 6),
                facesPlanas = faces
            };
        }
        catch (Exception ex)
        {
            return new { erro = ex.Message };
        }
    }

    private static List<InnerLoop> ExtractHorizontalInnerLoops(Element e)
    {
        List<InnerLoop> result = new();
        try
        {
            Options options = new() { ComputeReferences = false, IncludeNonVisibleObjects = true, DetailLevel = ViewDetailLevel.Fine };
            GeometryElement? geometry = e.get_Geometry(options);
            if (geometry == null) return result;
            List<Solid> solids = new();
            CollectSolids(geometry, solids);

            foreach (Solid solid in solids.Where(s => s.Volume > 1e-9))
            {
                foreach (Face face in solid.Faces)
                {
                    if (face is not PlanarFace pf || pf.FaceNormal.Z < 0.90) continue;
                    EdgeArrayArray loops = pf.EdgeLoops;
                    if (loops.Size < 2) continue;

                    var candidates = new List<(Pt2 center, double perimeter, int points)>();
                    foreach (EdgeArray loop in loops)
                    {
                        List<XYZ> pts = TessellateLoop(loop);
                        if (pts.Count < 3) continue;
                        double perimeter = LoopPerimeter(pts);
                        XYZ center = Average(pts);
                        candidates.Add((new Pt2(ToMm(center.X), ToMm(center.Y)), ToMm(perimeter), pts.Count));
                    }
                    if (candidates.Count < 2) continue;

                    int outer = candidates
                        .Select((x, i) => new { i, x.perimeter })
                        .OrderByDescending(x => x.perimeter)
                        .First().i;

                    for (int i = 0; i < candidates.Count; i++)
                    {
                        if (i == outer) continue;
                        var c = candidates[i];
                        if (c.perimeter < 120) continue;
                        result.Add(new InnerLoop(c.center, ToMm(pf.Origin.Z), c.perimeter));
                    }
                }
            }
        }
        catch { }
        return result;
    }

    private static object[] LoopData(EdgeArrayArray loops)
    {
        var data = new List<object>();
        int index = 0;
        foreach (EdgeArray loop in loops)
        {
            List<XYZ> pts = TessellateLoop(loop);
            XYZ center = pts.Count > 0 ? Average(pts) : XYZ.Zero;
            data.Add(new
            {
                indice = index++,
                quantidadePontos = pts.Count,
                centro = Point(center),
                perimetroMm = Math.Round(ToMm(LoopPerimeter(pts)), 1),
                pontos = pts.Take(80).Select(Point).ToArray()
            });
        }
        return data.ToArray();
    }

    private static void CollectSolids(GeometryElement geometry, List<Solid> solids)
    {
        foreach (GeometryObject obj in geometry)
        {
            if (obj is Solid solid && solid.Faces.Size > 0)
                solids.Add(solid);
            else if (obj is GeometryInstance gi)
            {
                GeometryElement? instance = gi.GetInstanceGeometry();
                if (instance != null) CollectSolids(instance, solids);
            }
        }
    }

    private static Room? FindRoom(Document doc, string? query)
    {
        string raw = RepairMojibake((query ?? string.Empty).Trim());
        if (raw.Length == 0) return null;

        if (long.TryParse(raw, out long elementId))
        {
            try
            {
                if (doc.GetElement(new ElementId(elementId)) is Room byId)
                    return byId;
            }
            catch { }
        }

        string q = NormalizeText(raw);
        var rooms = new FilteredElementCollector(doc)
            .OfCategory(BuiltInCategory.OST_Rooms)
            .WhereElementIsNotElementType()
            .Cast<Room>()
            .ToList();

        Room? exact = rooms.FirstOrDefault(r =>
            NormalizeText(r.Name) == q || NormalizeText(r.Number) == q);
        if (exact != null) return exact;

        return rooms.FirstOrDefault(r =>
            NormalizeText(r.Name).Contains(q, StringComparison.Ordinal) ||
            NormalizeText(r.Number).Contains(q, StringComparison.Ordinal));
    }

    private static IEnumerable<Element> ElementsInAndNearRoom(Document doc, Room room)
    {
        BoundingBoxXYZ? rb = room.get_BoundingBox(null);
        double expand = Mm(350);

        foreach (Element e in new FilteredElementCollector(doc).WhereElementIsNotElementType().ToElements())
        {
            if (e.Category == null || e is Room) continue;
            XYZ? p = RepresentativePoint(e);
            if (p != null)
            {
                bool inside = false;
                try { inside = room.IsPointInRoom(p); } catch { }
                if (inside) { yield return e; continue; }
            }

            if (rb == null) continue;
            BoundingBoxXYZ? eb = null;
            try { eb = e.get_BoundingBox(null); } catch { }
            if (eb == null) continue;
            XYZ center = (eb.Min + eb.Max) * 0.5;
            bool near = center.X >= rb.Min.X - expand && center.X <= rb.Max.X + expand &&
                        center.Y >= rb.Min.Y - expand && center.Y <= rb.Max.Y + expand &&
                        center.Z >= rb.Min.Z - expand && center.Z <= rb.Max.Z + expand;
            if (near) yield return e;
        }
    }

    private static List<Element> Resolve(Document doc, MaxCommand c)
    {
        if (c.ElementIds is { Count: > 0 })
            return c.ElementIds.Select(id => doc.GetElement(new ElementId(id))).Where(e => e != null).Cast<Element>().ToList();
        if (string.IsNullOrWhiteSpace(c.Busca)) return new();
        string n = RepairMojibake(c.Busca.Trim());
        return new FilteredElementCollector(doc)
            .WhereElementIsNotElementType()
            .ToElements()
            .Where(e => Contains(e.Category?.Name, n) || Contains(SafeName(e), n) ||
                (e is FamilyInstance fi && (Contains(fi.Symbol?.FamilyName, n) || Contains(fi.Symbol?.Name, n))) ||
                e.Parameters.Cast<Parameter>().Any(p => ParameterContains(p, n)))
            .ToList();
    }

    private static XYZ? RepresentativePoint(Element e)
    {
        try
        {
            if (e.Location is LocationPoint lp) return lp.Point;
            if (e.Location is LocationCurve lc) return lc.Curve.Evaluate(0.5, true);
            BoundingBoxXYZ? bb = e.get_BoundingBox(null);
            if (bb != null) return (bb.Min + bb.Max) * 0.5;
        }
        catch { }
        return null;
    }

    private static object? LocationData(Element e)
    {
        try
        {
            if (e.Location is LocationPoint lp)
                return new { tipo = "ponto", ponto = Point(lp.Point), rotacaoGraus = Math.Round(lp.Rotation * 180.0 / Math.PI, 3) };
            if (e.Location is LocationCurve lc)
                return new { tipo = "curva", inicio = Point(lc.Curve.GetEndPoint(0)), fim = Point(lc.Curve.GetEndPoint(1)), comprimentoMm = Math.Round(ToMm(lc.Curve.Length), 2) };
        }
        catch { }
        return null;
    }

    private static object[] RoomBoundary(Room room)
    {
        try
        {
            var opts = new SpatialElementBoundaryOptions();
            var loops = room.GetBoundarySegments(opts);
            if (loops == null) return Array.Empty<object>();
            return loops.Select(loop => (object)new
            {
                segmentos = loop.Select(seg => new
                {
                    elementoLimiteId = seg.ElementId.Value,
                    inicio = Point(seg.GetCurve().GetEndPoint(0)),
                    fim = Point(seg.GetCurve().GetEndPoint(1))
                }).ToArray()
            }).ToArray();
        }
        catch { return Array.Empty<object>(); }
    }

    private static object? BBox(BoundingBoxXYZ? bb) => bb == null ? null : new
    {
        min = Point(bb.Min),
        max = Point(bb.Max),
        centro = Point((bb.Min + bb.Max) * 0.5),
        tamanho = new
        {
            x = Math.Round(ToMm(bb.Max.X - bb.Min.X), 2),
            y = Math.Round(ToMm(bb.Max.Y - bb.Min.Y), 2),
            z = Math.Round(ToMm(bb.Max.Z - bb.Min.Z), 2)
        }
    };

    private static object Point(XYZ p) => new
    {
        x = Math.Round(ToMm(p.X), 3),
        y = Math.Round(ToMm(p.Y), 3),
        z = Math.Round(ToMm(p.Z), 3)
    };

    private static object PointVector(XYZ p) => new
    {
        x = Math.Round(p.X, 6),
        y = Math.Round(p.Y, 6),
        z = Math.Round(p.Z, 6)
    };

    private static List<XYZ> TessellateLoop(EdgeArray loop)
    {
        List<XYZ> pts = new();
        foreach (Edge edge in loop)
        {
            IList<XYZ> tess = edge.Tessellate();
            foreach (XYZ p in tess)
            {
                if (pts.Count == 0 || !pts[^1].IsAlmostEqualTo(p)) pts.Add(p);
            }
        }
        return pts;
    }

    private static XYZ Average(IReadOnlyList<XYZ> pts)
    {
        if (pts.Count == 0) return XYZ.Zero;
        double x = 0, y = 0, z = 0;
        foreach (XYZ p in pts) { x += p.X; y += p.Y; z += p.Z; }
        return new XYZ(x / pts.Count, y / pts.Count, z / pts.Count);
    }

    private static double LoopPerimeter(IReadOnlyList<XYZ> pts)
    {
        if (pts.Count < 2) return 0;
        double sum = 0;
        for (int i = 1; i < pts.Count; i++) sum += pts[i - 1].DistanceTo(pts[i]);
        if (!pts[0].IsAlmostEqualTo(pts[^1])) sum += pts[^1].DistanceTo(pts[0]);
        return sum;
    }

    private static bool IsSinkLike(Element e)
    {
        string text = string.Join(" ", new[]
        {
            e.Category?.Name, SafeName(e),
            e is FamilyInstance fi ? fi.Symbol?.FamilyName : null,
            e is FamilyInstance fi2 ? fi2.Symbol?.Name : null
        }.Where(x => !string.IsNullOrWhiteSpace(x)));
        return Contains(text, "cuba") || Contains(text, "lavat") || Contains(text, "sink");
    }

    private static bool ParameterContains(Parameter p, string needle)
    {
        try { return p.HasValue && Contains(p.AsValueString() ?? p.AsString(), needle); }
        catch { return false; }
    }

    private static bool Contains(string? value, string needle)
    {
        if (string.IsNullOrWhiteSpace(value) || string.IsNullOrWhiteSpace(needle)) return false;
        return NormalizeText(value).Contains(NormalizeText(RepairMojibake(needle)), StringComparison.Ordinal);
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;
        string repaired = RepairMojibake(value).Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(repaired.Length);
        bool previousSpace = false;
        foreach (char ch in repaired)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsWhiteSpace(ch))
            {
                if (!previousSpace) sb.Append(' ');
                previousSpace = true;
                continue;
            }
            previousSpace = false;
            sb.Append(char.ToLowerInvariant(ch));
        }
        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static string RepairMojibake(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        if (!value.Contains('Ã') && !value.Contains('Â')) return value;
        try
        {
            string repaired = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(value));
            return repaired.Contains('\uFFFD') ? value : repaired;
        }
        catch { return value; }
    }

    private static string SafeName(Element e)
    {
        try { return e.Name ?? string.Empty; }
        catch { return string.Empty; }
    }

    private static double Distance2D(double x1, double y1, double x2, double y2)
    {
        double dx = x1 - x2, dy = y1 - y2;
        return Math.Sqrt(dx * dx + dy * dy);
    }

    private static double Mm(double value) => UnitUtils.ConvertToInternalUnits(value, UnitTypeId.Millimeters);
    private static double ToMm(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.Millimeters);
    private static double InternalAreaToM2(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.SquareMeters);
    private static double InternalVolumeToM3(double value) => UnitUtils.ConvertFromInternalUnits(value, UnitTypeId.CubicMeters);

    private static MaxResult Ok(string? id, string message, int quantity, IEnumerable<long>? ids = null, object? data = null) =>
        new(id, true, message, quantity, ids?.Take(500).ToArray() ?? Array.Empty<long>(), null, data);

    private static MaxResult Fail(string? id, string message, string? error = null) =>
        new(id, false, message, 0, Array.Empty<long>(), error, null);

    private sealed record Pt2(double X, double Y);
    private sealed record InnerLoop(Pt2 Center, double ElevationMm, double PerimeterMm);
    private sealed record OpeningCandidate(long ElementId, string? Category, string? Name, string? Family, Pt2 Center, double ElevationMm, double PerimeterMm);
}
