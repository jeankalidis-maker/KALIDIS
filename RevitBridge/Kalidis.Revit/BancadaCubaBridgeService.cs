using System.Text;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;

namespace Kalidis.Revit;

/// <summary>
/// Ação especializada para bancada + cubas.
/// Fluxo seguro: identifica o sketch da bancada, encontra apenas os loops internos
/// compatíveis com as cubas, alinha uma cuba por loop e recria os loops internos
/// usando o contorno horizontal real das cubas. Se a correspondência não for
/// inequívoca, não altera o modelo.
/// </summary>
public static class BancadaCubaBridgeService
{
    private const string CommandPath = @"C:\KALIDIS\Bridge\comando.json";
    private const string ResultPath = @"C:\KALIDIS\Bridge\resultado.json";
    private const string Action = "alinhar_recortar_cubas_bancada";
    private static DateTime _lastWriteUtc = DateTime.MinValue;

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
            string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
            using JsonDocument json = JsonDocument.Parse(raw);
            return json.RootElement.TryGetProperty("acao", out JsonElement a) &&
                   string.Equals(a.GetString(), Action, StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    public static void TryProcess(UIApplication uiApp)
    {
        if (!File.Exists(CommandPath)) return;
        DateTime write = File.GetLastWriteTimeUtc(CommandPath);
        if (write <= _lastWriteUtc) return;

        string raw = File.ReadAllText(CommandPath, Encoding.UTF8);
        string? id = null;
        object result;
        try
        {
            using JsonDocument json = JsonDocument.Parse(raw);
            JsonElement root = json.RootElement;
            id = root.TryGetProperty("id", out JsonElement idEl) ? idEl.GetString() : null;
            if (!root.TryGetProperty("acao", out JsonElement a) ||
                !string.Equals(a.GetString(), Action, StringComparison.OrdinalIgnoreCase))
            {
                _lastWriteUtc = write;
                return;
            }

            UIDocument? uiDoc = uiApp.ActiveUIDocument;
            result = uiDoc == null
                ? Fail(id, "Nenhum documento Revit ativo.")
                : Execute(uiDoc.Document, root, id);
        }
        catch (Exception ex)
        {
            result = Fail(id, "Falha ao alinhar cubas e ajustar recortes.", ex.Message);
        }

        File.WriteAllText(ResultPath,
            JsonSerializer.Serialize(result, JsonOptions) + Environment.NewLine,
            new UTF8Encoding(false));
        _lastWriteUtc = write;
    }

    private static object Execute(Document doc, JsonElement root, string? id)
    {
        long bancadaId = GetLong(root, "bancadaId");
        long[] cubaIds = GetLongArray(root, "cubaIds");
        bool executar = !root.TryGetProperty("executar", out JsonElement ex) || ex.ValueKind != JsonValueKind.False;

        if (bancadaId <= 0) return Fail(id, "Informe bancadaId.");
        if (cubaIds.Length < 1) return Fail(id, "Informe cubaIds.");
        if (doc.GetElement(new ElementId(bancadaId)) is not Floor bancada)
            return Fail(id, $"O elemento {bancadaId} não é um piso/bancada editável por sketch.");

        Sketch? sketch = doc.GetElement(bancada.SketchId) as Sketch;
        if (sketch == null)
            return Fail(id, $"A bancada {bancadaId} não possui Sketch acessível.");

        List<FamilyInstance> cubas = cubaIds
            .Select(x => doc.GetElement(new ElementId(x)) as FamilyInstance)
            .Where(x => x != null)
            .Cast<FamilyInstance>()
            .ToList();
        if (cubas.Count != cubaIds.Length)
            return Fail(id, "Uma ou mais cubas não existem mais no modelo.");

        List<LoopData> profile = ReadSketchLoops(sketch);
        if (profile.Count < 2)
            return Fail(id, "O sketch da bancada não possui loops internos suficientes.");

        LoopData outer = profile.OrderByDescending(x => x.Perimeter).First();
        List<LoopData> inner = profile
            .Where(x => !ReferenceEquals(x, outer))
            .Where(x => Mm(x.Perimeter) >= 1400 && Mm(x.Perimeter) <= 4200)
            .OrderBy(x => x.Center.X)
            .ToList();

        // Proteção forte: só edita quando há correspondência 1:1.
        if (inner.Count != cubas.Count)
        {
            return Fail(id,
                $"Proteção acionada: encontrei {inner.Count} abertura(s) válida(s) no sketch e {cubas.Count} cuba(s). Nada foi alterado.",
                null,
                new { bancadaId, sketchId = sketch.Id.Value, loopsTotais = profile.Count, aberturasValidas = inner.Select(ToInfo).ToArray() });
        }

        double targetPerimeter = inner.Average(x => x.Perimeter);
        List<SinkData> sinks = new();
        foreach (FamilyInstance cuba in cubas)
        {
            LoopData? footprint = FindSinkFootprint(cuba, targetPerimeter);
            if (footprint == null)
                return Fail(id, $"Não consegui obter contorno horizontal confiável da cuba {cuba.Id.Value}. Nada foi alterado.");
            sinks.Add(new SinkData(cuba, footprint));
        }

        // Cubas são equivalentes; ordenação espacial evita duplicar destino.
        sinks = sinks.OrderBy(x => x.Footprint.Center.X).ThenBy(x => x.Element.Id.Value).ToList();
        inner = inner.OrderBy(x => x.Center.X).ToList();

        var plan = new List<PlanItem>();
        for (int i = 0; i < sinks.Count; i++)
        {
            XYZ delta = inner[i].Center - sinks[i].Footprint.Center;
            delta = new XYZ(delta.X, delta.Y, 0);
            plan.Add(new PlanItem(sinks[i], inner[i], delta));
        }

        // Antes de qualquer alteração, valida referências dos loops do sketch.
        foreach (PlanItem p in plan)
        {
            if (p.Target.CurveElementIds.Count != p.Target.Curves.Count ||
                p.Target.CurveElementIds.Any(x => x == ElementId.InvalidElementId))
            {
                return Fail(id,
                    "Não consegui mapear com segurança as curvas das aberturas para elementos do sketch. Nada foi alterado.",
                    null,
                    new { target = ToInfo(p.Target) });
            }
        }

        var diagnostics = plan.Select(p => new
        {
            cubaId = p.Sink.Element.Id.Value,
            aberturaCentroMm = PointMm(p.Target.Center),
            cubaCentroAtualMm = PointMm(p.Sink.Footprint.Center),
            deslocamentoMm = PointMm(p.Delta),
            perimetroAberturaMm = Math.Round(Mm(p.Target.Perimeter), 1),
            perimetroCubaMm = Math.Round(Mm(p.Sink.Footprint.Perimeter), 1)
        }).ToArray();

        if (!executar)
            return Ok(id, "Diagnóstico concluído; nenhuma alteração executada.", cubas.Count, cubaIds, new { bancadaId, sketchId = sketch.Id.Value, diagnostics });

        // 1) Ajusta os recortes no sketch, mantendo os centros originais das 5 aberturas.
        // O novo loop recebe a forma/tamanho real da cuba e é transladado ao centro alvo.
        Plane plane = sketch.SketchPlane.GetPlane();
        double planeZ = plane.Origin.Z;
        List<List<Curve>> replacementLoops = new();
        foreach (PlanItem p in plan)
        {
            XYZ d = p.Delta + new XYZ(0, 0, planeZ - p.Sink.Footprint.Center.Z);
            Transform tr = Transform.CreateTranslation(d);
            replacementLoops.Add(p.Sink.Footprint.Curves.Select(c => c.CreateTransformed(tr)).ToList());
        }

        using (SketchEditScope scope = new(doc, "KALIDIS - Ajustar recortes das cubas"))
        {
            scope.Start(sketch.Id);
            using Transaction tx = new(doc, "KALIDIS - Recortes da bancada");
            tx.Start();

            foreach (PlanItem p in plan)
                doc.Delete(p.Target.CurveElementIds);

            foreach (List<Curve> loop in replacementLoops)
                foreach (Curve curve in loop)
                    doc.Create.NewModelCurve(curve, sketch.SketchPlane);

            tx.Commit();
            scope.Commit(new ContinueFailures());
        }

        // 2) Alinha uma cuba em cada abertura, sem reaproveitar deslocamento acumulado.
        using (Transaction tx = new(doc, "KALIDIS - Alinhar cubas às aberturas"))
        {
            tx.Start();
            foreach (PlanItem p in plan)
                ElementTransformUtils.MoveElement(doc, p.Sink.Element.Id, p.Delta);

            // Regenerate exige documento modificável; por isso ocorre dentro da transação.
            doc.Regenerate();
            tx.Commit();
        }

        // 3) Validação simples de sobreposição pelos centros finais.
        var finalCenters = plan.Select(p => p.Target.Center).ToArray();
        double minDistanceMm = double.MaxValue;
        for (int i = 0; i < finalCenters.Length; i++)
            for (int j = i + 1; j < finalCenters.Length; j++)
                minDistanceMm = Math.Min(minDistanceMm, Mm(finalCenters[i].DistanceTo(finalCenters[j])));

        return Ok(id,
            $"{cubas.Count} cuba(s) alinhada(s) e {inner.Count} recorte(s) ajustado(s) pela geometria real das cubas.",
            cubas.Count,
            cubaIds,
            new
            {
                bancadaId,
                sketchId = sketch.Id.Value,
                cubas = cubas.Count,
                recortes = inner.Count,
                distanciaMinimaEntreCentrosMm = Math.Round(minDistanceMm, 1),
                diagnostics
            });
    }

    private static List<LoopData> ReadSketchLoops(Sketch sketch)
    {
        List<LoopData> result = new();
        foreach (CurveArray array in sketch.Profile)
        {
            List<Curve> curves = new();
            List<ElementId> refs = new();
            foreach (Curve c in array)
            {
                curves.Add(c.Clone());
                ElementId id = ElementId.InvalidElementId;
                try { id = c.Reference?.ElementId ?? ElementId.InvalidElementId; } catch { }
                refs.Add(id);
            }
            LoopData? data = MakeLoop(curves, refs);
            if (data != null) result.Add(data);
        }
        return result;
    }

    private static LoopData? FindSinkFootprint(Element element, double targetPerimeter)
    {
        Options opt = new() { DetailLevel = ViewDetailLevel.Fine, IncludeNonVisibleObjects = true, ComputeReferences = false };
        GeometryElement? ge = element.get_Geometry(opt);
        if (ge == null) return null;
        List<LoopData> candidates = new();
        CollectHorizontalLoops(ge, Transform.Identity, candidates);
        return candidates
            .Where(x => Mm(x.Perimeter) >= 1200 && Mm(x.Perimeter) <= 4200)
            .OrderBy(x => Math.Abs(x.Perimeter - targetPerimeter))
            .FirstOrDefault();
    }

    private static void CollectHorizontalLoops(GeometryElement ge, Transform tr, List<LoopData> output)
    {
        foreach (GeometryObject go in ge)
        {
            if (go is GeometryInstance gi)
            {
                CollectHorizontalLoops(gi.GetInstanceGeometry(), tr, output);
                continue;
            }
            if (go is not Solid solid || solid.Faces.Size == 0) continue;
            foreach (Face f in solid.Faces)
            {
                if (f is not PlanarFace pf || Math.Abs(pf.FaceNormal.Z) < 0.98) continue;
                foreach (EdgeArray edgeLoop in pf.EdgeLoops)
                {
                    List<Curve> curves = new();
                    foreach (Edge edge in edgeLoop)
                        curves.Add(edge.AsCurveFollowingFace(pf));
                    LoopData? data = MakeLoop(curves, null);
                    if (data != null) output.Add(data);
                }
            }
        }
    }

    private static LoopData? MakeLoop(List<Curve> curves, List<ElementId>? ids)
    {
        if (curves.Count < 2) return null;
        double perimeter = curves.Sum(x => x.Length);
        List<XYZ> pts = new();
        foreach (Curve c in curves)
        {
            IList<XYZ> tess = c.Tessellate();
            pts.AddRange(tess);
        }
        if (pts.Count == 0) return null;
        XYZ center = new(pts.Average(p => p.X), pts.Average(p => p.Y), pts.Average(p => p.Z));
        return new LoopData(curves, ids ?? new List<ElementId>(), center, perimeter);
    }

    private static long GetLong(JsonElement root, string name)
        => root.TryGetProperty(name, out JsonElement e) && e.TryGetInt64(out long v) ? v : 0;

    private static long[] GetLongArray(JsonElement root, string name)
    {
        if (!root.TryGetProperty(name, out JsonElement e) || e.ValueKind != JsonValueKind.Array)
            return Array.Empty<long>();
        return e.EnumerateArray().Where(x => x.TryGetInt64(out _)).Select(x => x.GetInt64()).ToArray();
    }

    private static double Mm(double feet) => UnitUtils.ConvertFromInternalUnits(feet, UnitTypeId.Millimeters);
    private static object PointMm(XYZ p) => new { x = Math.Round(Mm(p.X), 2), y = Math.Round(Mm(p.Y), 2), z = Math.Round(Mm(p.Z), 2) };
    private static object ToInfo(LoopData x) => new { centroMm = PointMm(x.Center), perimetroMm = Math.Round(Mm(x.Perimeter), 1), curvas = x.Curves.Count };

    private static object Ok(string? id, string message, int qty, IEnumerable<long> ids, object? data = null)
        => new { id, sucesso = true, mensagem = message, quantidade = qty, elementIds = ids.ToArray(), erro = (string?)null, dados = data };

    private static object Fail(string? id, string message, string? error = null, object? data = null)
        => new { id, sucesso = false, mensagem = message, quantidade = 0, elementIds = Array.Empty<long>(), erro = error, dados = data };

    private sealed record LoopData(List<Curve> Curves, List<ElementId> CurveElementIds, XYZ Center, double Perimeter);
    private sealed record SinkData(FamilyInstance Element, LoopData Footprint);
    private sealed record PlanItem(SinkData Sink, LoopData Target, XYZ Delta);

    private sealed class ContinueFailures : IFailuresPreprocessor
    {
        public FailureProcessingResult PreprocessFailures(FailuresAccessor failuresAccessor)
            => FailureProcessingResult.Continue;
    }
}
