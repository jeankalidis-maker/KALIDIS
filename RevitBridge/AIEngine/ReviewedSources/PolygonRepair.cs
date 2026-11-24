using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.PolygonRepair;
public sealed class PolygonRepair : IReviewedOperation {
 public string Name=>"polygon_repair";
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
  var room=d.GetElement(new ElementId(a.GetProperty("roomId").GetInt64())) as Autodesk.Revit.DB.Architecture.Room??throw new ArgumentException("Room required");
  var loops=room.GetBoundarySegments(new SpatialElementBoundaryOptions());
  if(room.Area<=0||loops==null||loops.Count<1||loops.Count>20||loops.Sum(x=>x.Count)>200||loops.SelectMany(x=>x).Any(x=>x.GetCurve() is not Line))throw new ArgumentException("Enclosed straight-edged polygon room required");
  var corners=loops.SelectMany(x=>x).Select(x=>x.GetCurve().GetEndPoint(0)).ToArray();
  var minX=corners.Min(x=>x.X);var maxX=corners.Max(x=>x.X);var minY=corners.Min(x=>x.Y);var maxY=corners.Max(x=>x.Y);
  // Expanded rectangle must not touch or intersect any boundary segment, including holes.
  bool Crosses(XYZ p,XYZ q,double x0,double x1,double y0,double y1){
   double lo=0,hi=1;
   bool Clip(double start,double delta,double min,double max){
    if(Math.Abs(delta)<1e-12)return start>=min&&start<=max;
    var a=(min-start)/delta;var b=(max-start)/delta;
    lo=Math.Max(lo,Math.Min(a,b));hi=Math.Min(hi,Math.Max(a,b));return lo<=hi;
   }
   return Clip(p.X,q.X-p.X,x0,x1)&&Clip(p.Y,q.Y-p.Y,y0,y1);
  }
  var edges=loops.SelectMany(x=>x).Select(x=>x.GetCurve()).ToArray();
  bool Inside((XYZ min,XYZ max) b){
   double x0=b.min.X-clearance,x1=b.max.X+clearance,y0=b.min.Y-clearance,y1=b.max.Y+clearance;
   if(x0<minX||x1>maxX||y0<minY||y1>maxY)return false;
   if(edges.Any(e=>Crosses(e.GetEndPoint(0),e.GetEndPoint(1),x0,x1,y0,y1)))return false;
   return (from x in new[]{x0,x1} from y in new[]{y0,y1} from z in new[]{b.min.Z+Mm(1),b.max.Z-Mm(1)} select new XYZ(x,y,z)).All(room.IsPointInRoom);
  }
  if(!room.IsPointInRoom((original.min+original.max)*0.5))throw new ArgumentException("Target not in room");
  var supported=new[]{BuiltInCategory.OST_Furniture,BuiltInCategory.OST_Walls,BuiltInCategory.OST_StructuralColumns,BuiltInCategory.OST_Columns,BuiltInCategory.OST_Casework,BuiltInCategory.OST_Doors,BuiltInCategory.OST_Windows,BuiltInCategory.OST_SpecialityEquipment,BuiltInCategory.OST_PipeCurves,BuiltInCategory.OST_DuctCurves,BuiltInCategory.OST_Conduit,BuiltInCategory.OST_CableTray,BuiltInCategory.OST_StructuralFraming};
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
  dx.Add(minX+clearance-original.min.X+Mm(0.2));dx.Add(maxX-clearance-original.max.X-Mm(0.2));dy.Add(minY+clearance-original.min.Y+Mm(0.2));dy.Add(maxY-clearance-original.max.Y-Mm(0.2));
  foreach(var c in corners){dx.Add(c.X+margin-original.min.X);dx.Add(c.X-margin-original.max.X);dy.Add(c.Y+margin-original.min.Y);dy.Add(c.Y-margin-original.max.Y);}
  if(dx.Distinct().Count()*dy.Distinct().Count()>200000)throw new InvalidOperationException("Candidate limit exceeded");
  var candidates=(from x in dx.Distinct() from y in dy.Distinct() let v=new XYZ(x,y,0) where v.GetLength()<=limit+1e-9 orderby v.GetLength(),x,y select v).ToArray();
  var valid=candidates.FirstOrDefault(v=>Inside((original.min+v,original.max+v))&&Dists((original.min+v,original.max+v)).All(x=>x>=clearance));
  if(valid==null)throw new InvalidOperationException("No conservative candidate within movement limit; no edit");
  double[] Vec(XYZ q)=>new[]{ToMm(q.X),ToMm(q.Y),ToMm(q.Z)};
  if(mode=="diagnose")return new{ id=id.Value,positionMm=Vec(p0),proposedDeltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),predictedGapsMm=Dists((original.min+valid,original.max+valid)).Select(ToMm).ToArray(),candidateCount=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),roomId=room.Id.Value,scope="Polygonal room containment and selected local obstacles; conservative AABB"};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  if(valid.GetLength()>Mm(0.01))ElementTransformUtils.MoveElement(d,id,valid);
  d.Regenerate();var actual=Bounds(target);var after=Dists(actual);
  if(!Inside(actual)||after.Any(x=>x<clearance-Mm(0.01))||((LocationPoint)target.Location).Point.DistanceTo(p0+valid)>Mm(0.1)||(actual.max-actual.min).DistanceTo(original.max-original.min)>Mm(0.1))throw new InvalidOperationException("Repair verification failed");
  for(int i=0;i<ids.Length;i++){var b=Bounds(d.GetElement(ids[i]));if(b.min.DistanceTo(obstacles[i].min)>Mm(0.1)||b.max.DistanceTo(obstacles[i].max)>Mm(0.1))throw new InvalidOperationException("Obstacle moved");}
  return new{id=id.Value,deltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),afterGapsMm=after.Select(ToMm).ToArray(),finalMm=Vec(((LocationPoint)target.Location).Point),candidates=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),roomId=room.Id.Value,insideRoom=true,scope="Polygonal room containment; excludes linked models, door swings and circulation validation"};
 }
}