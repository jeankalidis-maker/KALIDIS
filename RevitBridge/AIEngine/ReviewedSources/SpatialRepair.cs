using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.SpatialRepair;
public sealed class SpatialRepair : IReviewedOperation {
 public string Name=>"spatial_repair";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double Number(string k,double lo,double hi){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||x<lo||x>hi)throw new ArgumentException(k);return Mm(x);}
  var mode=a.GetProperty("mode").GetString();
  var id=new ElementId(a.GetProperty("id").GetInt64());
  var target=d.GetElement(id) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(target.Category?.Id.Value!=(long)BuiltInCategory.OST_Furniture||target.Host!=null||target.GroupId!=ElementId.InvalidElementId||target.Pinned||target.Location is not LocationPoint loc)throw new ArgumentException("Only unhosted unpinned ungrouped furniture point instances");
  var expected=a.GetProperty("expectedMm").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
  if(expected.Length!=3||expected.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000000))throw new ArgumentException("Invalid expected position");
  var p0=loc.Point;
  if(p0.DistanceTo(new XYZ(Mm(expected[0]),Mm(expected[1]),Mm(expected[2])))>Mm(0.1))throw new InvalidOperationException("Stale position");
  var clearance=Number("minimumMm",1,3000);var limit=Number("maxMoveMm",1,5000);
  (XYZ min,XYZ max) Bounds(Element e){
   var b=e.get_BoundingBox(null)??throw new ArgumentException("Missing bounds "+e.Id.Value);
   var cs=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(cs.Min(q=>q.X),cs.Min(q=>q.Y),cs.Min(q=>q.Z)),new XYZ(cs.Max(q=>q.X),cs.Max(q=>q.Y),cs.Max(q=>q.Z)));
  }
  var original=Bounds(target);
  var supported=new[]{BuiltInCategory.OST_Furniture,BuiltInCategory.OST_Walls,BuiltInCategory.OST_StructuralColumns,BuiltInCategory.OST_Columns,BuiltInCategory.OST_Casework,BuiltInCategory.OST_Doors,BuiltInCategory.OST_Windows,BuiltInCategory.OST_SpecialityEquipment};
  var categories=a.GetProperty("categoryIds").EnumerateArray().Select(v=>(BuiltInCategory)v.GetInt64()).Distinct().ToArray();
  if(categories.Length<1||categories.Any(c=>!supported.Contains(c)))throw new ArgumentException("Unsupported obstacle category");
  var expansion=new XYZ(limit+clearance,limit+clearance,clearance);
  var outline=new Outline(original.min-expansion,original.max+expansion);
  var found=new FilteredElementCollector(d).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(categories.ToList())).WherePasses(new BoundingBoxIntersectsFilter(outline)).Where(e=>e.Id!=id).Take(31).ToArray();
  if(found.Length>30)throw new InvalidOperationException("More than 30 nearby obstacles; reduce movement scope");
  var ids=found.Select(e=>e.Id).ToArray();
  var obstacles=found.Select(Bounds).ToArray();
  double Gap(double x,double y,double u,double v)=>Math.Max(0,Math.Max(u-y,x-v));
  double Distance((XYZ min,XYZ max) x,(XYZ min,XYZ max) y){
   double a=Gap(x.min.X,x.max.X,y.min.X,y.max.X),b=Gap(x.min.Y,x.max.Y,y.min.Y,y.max.Y),c=Gap(x.min.Z,x.max.Z,y.min.Z,y.max.Z);
   return Math.Sqrt(a*a+b*b+c*c);
  }
  double[] Dists((XYZ min,XYZ max) b)=>obstacles.Select(o=>Distance(b,o)).ToArray();
  var before=Dists(original);
  var dx=new List<double>{0};var dy=new List<double>{0};var margin=clearance+Mm(0.2);
  foreach(var o in obstacles){dx.Add(o.min.X-original.max.X-margin);dx.Add(o.max.X-original.min.X+margin);dy.Add(o.min.Y-original.max.Y-margin);dy.Add(o.max.Y-original.min.Y+margin);}
  var candidates=(from x in dx.Distinct() from y in dy.Distinct() let v=new XYZ(x,y,0) where v.GetLength()<=limit+1e-9 orderby v.GetLength(),x,y select v).ToArray();
  var valid=candidates.FirstOrDefault(v=>Dists((original.min+v,original.max+v)).All(x=>x>=clearance));
  if(valid==null)throw new InvalidOperationException("No conservative candidate within movement limit; no edit");
  double[] Vec(XYZ q)=>new[]{ToMm(q.X),ToMm(q.Y),ToMm(q.Z)};
  if(mode=="diagnose")return new{ id=id.Value,positionMm=Vec(p0),proposedDeltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),predictedGapsMm=Dists((original.min+valid,original.max+valid)).Select(ToMm).ToArray(),candidateCount=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),scope="Automatic local discovery in selected categories; conservative AABB; excludes linked models"};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  if(valid.GetLength()>Mm(0.01))ElementTransformUtils.MoveElement(d,id,valid);
  d.Regenerate();var actual=Bounds(target);var after=Dists(actual);
  if(after.Any(x=>x<clearance-Mm(0.01))||((LocationPoint)target.Location).Point.DistanceTo(p0+valid)>Mm(0.1)||(actual.max-actual.min).DistanceTo(original.max-original.min)>Mm(0.1))throw new InvalidOperationException("Repair verification failed");
  for(int i=0;i<ids.Length;i++){var b=Bounds(d.GetElement(ids[i]));if(b.min.DistanceTo(obstacles[i].min)>Mm(0.1)||b.max.DistanceTo(obstacles[i].max)>Mm(0.1))throw new InvalidOperationException("Obstacle moved");}
  return new{id=id.Value,deltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),afterGapsMm=after.Select(ToMm).ToArray(),finalMm=Vec(((LocationPoint)target.Location).Point),candidates=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),scope="Selected local categories; excludes linked models, door swings and circulation validation"};
 }
}