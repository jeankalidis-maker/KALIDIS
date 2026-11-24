using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.Clearance;
public sealed class Clearance : IReviewedOperation {
 public string Name=>"clearance_screen";
 public object Execute(Document d,JsonElement a){
  var pairs=a.GetProperty("pairs").EnumerateArray().Select(x=>x.EnumerateArray().Select(v=>v.GetInt64()).ToArray()).ToArray();
  if(pairs.Length<1||pairs.Length>100||pairs.Any(p=>p.Length!=2||p[0]==p[1]))throw new ArgumentException("1-100 ID pairs");
  var requested=a.GetProperty("minimumMm").GetDouble();if(!double.IsFinite(requested)||requested<0||requested>100000)throw new ArgumentException("Invalid minimum");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  (XYZ min,XYZ max) Bounds(long id){
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing "+id);
   var b=e.get_BoundingBox(null)??throw new ArgumentException("Missing bounds "+id);
   var c=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(c.Min(p=>p.X),c.Min(p=>p.Y),c.Min(p=>p.Z)),new XYZ(c.Max(p=>p.X),c.Max(p=>p.Y),c.Max(p=>p.Z)));
  }
  double Gap(double a,double b,double c,double e)=>Math.Max(0,Math.Max(c-b,a-e));
  var rows=pairs.Select(p=>{
   var x=Bounds(p[0]);var y=Bounds(p[1]);
   var dx=Gap(x.min.X,x.max.X,y.min.X,y.max.X);var dy=Gap(x.min.Y,x.max.Y,y.min.Y,y.max.Y);var dz=Gap(x.min.Z,x.max.Z,y.min.Z,y.max.Z);
   var distance=Mm(Math.Sqrt(dx*dx+dy*dy+dz*dz));
   return new{firstId=p[0],secondId=p[1],boundingBoxGapMm=distance,axisGapsMm=new[]{Mm(dx),Mm(dy),Mm(dz)},status=distance>=requested?"screen_pass":"requires_geometry_review"};
  }).ToArray();
  return new{minimumMm=requested,pairs=rows,note="World axis-aligned bounding-box gap is a conservative lower bound, not exact solid clearance; a screen failure is inconclusive."};
 }
}