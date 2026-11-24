// First-party reviewed extension: deterministic readback of physical geometry and room boundaries.
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using Kalidis.AIEngine;
namespace Kalidis.SolidAudit;
public sealed class SolidAudit : IReviewedOperation {
 public string Name=>"solid_audit";
 public object Execute(Document d,JsonElement a){
  var ids=a.GetProperty("ids").EnumerateArray().Select(x=>x.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>30)throw new ArgumentException("1-30 element IDs");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] Pt(XYZ x)=>new[]{Mm(x.X),Mm(x.Y),Mm(x.Z)};
  IEnumerable<Solid> Solids(GeometryElement? g,int depth){
   if(g==null||depth>8)yield break;
   foreach(var item in g){
    if(item is Solid s&&s.Faces.Size>0&&s.Volume>1e-9)yield return s;
    else if(item is GeometryInstance inst)foreach(var nested in Solids(inst.GetInstanceGeometry(),depth+1))yield return nested;
   }
  }
  var results=ids.Select(id=>{
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing "+id);
   var solids=Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),0).ToArray();
   var volume=UnitUtils.ConvertFromInternalUnits(solids.Sum(s=>s.Volume),UnitTypeId.CubicMeters);
   var area=UnitUtils.ConvertFromInternalUnits(solids.Sum(s=>s.SurfaceArea),UnitTypeId.SquareMeters);
   object? room=null;
   if(e is Room r){
    var loops=r.GetBoundarySegments(new SpatialElementBoundaryOptions());
    room=new{number=r.Number,areaM2=UnitUtils.ConvertFromInternalUnits(r.Area,UnitTypeId.SquareMeters),loops=loops?.Select(loop=>loop.Select(seg=>new{hostId=seg.ElementId.Value,startMm=Pt(seg.GetCurve().GetEndPoint(0)),endMm=Pt(seg.GetCurve().GetEndPoint(1)),lengthMm=Mm(seg.GetCurve().Length)}).ToArray()).ToArray()};
   }
   bool rebarHost=RebarHostData.GetRebarHostData(e)!=null;
   return new{id,name=e.Name,category=e.Category?.Name,solidCount=solids.Length,volumeM3=volume,surfaceM2=area,rebarHost,room};
  }).ToArray();
  return new{count=results.Length,elements=results,note="Solid volumes are sums, not Boolean unions. Room boundary endpoints do not describe curved segments fully. Host-data presence is not structural adequacy."};
 }
}