// First-party reviewed operation. Read-only geometry inspection.
using System.Text.Json;
using Autodesk.Revit.DB;
using Kalidis.AIEngine;
namespace Kalidis.GeometryReview;
public sealed class GeometryReview : IReviewedOperation {
 public string Name=>"geometry_review";
 public object Execute(Document d,JsonElement args){
  var ids=args.GetProperty("ids").EnumerateArray().Select(x=>x.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>100)throw new ArgumentException("1-100 IDs");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double[] Point(XYZ x)=>new[]{Mm(x.X),Mm(x.Y),Mm(x.Z)};
  var rows=ids.Select(id=>{
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing element "+id);
   var b=e.get_BoundingBox(null);
   var corners=b==null?Array.Empty<XYZ>():(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   double[]? min=corners.Length==0?null:Point(new XYZ(corners.Min(p=>p.X),corners.Min(p=>p.Y),corners.Min(p=>p.Z)));
   double[]? max=corners.Length==0?null:Point(new XYZ(corners.Max(p=>p.X),corners.Max(p=>p.Y),corners.Max(p=>p.Z)));
   var point=e.Location as LocationPoint;var line=e.Location as LocationCurve;
   return new{id,uniqueId=e.UniqueId,name=e.Name,category=e.Category?.Name,pinned=e.Pinned,minMm=min,maxMm=max,locationMm=point==null?null:Point(point.Point),curveLengthMm=line==null?(double?)null:Mm(line.Curve.Length)};
  }).ToArray();
  return new{count=rows.Length,elements=rows,note="Bounding boxes may include symbolic or nonphysical geometry."};
 }
}