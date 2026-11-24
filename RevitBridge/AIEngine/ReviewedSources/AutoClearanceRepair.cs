using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.AutoClearanceRepair;
public sealed class AutoClearanceRepair : IReviewedOperation {
 public string Name=>"auto_clearance_repair";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double Number(string k,double lo,double hi){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||x<lo||x>hi)throw new ArgumentException(k);return Mm(x);}
  var mode=a.GetProperty("mode").GetString();
  if(mode=="documents")return new{documents=d.Application.Documents.Cast<Document>().Select(x=>new{title=x.Title,path=x.PathName,modified=x.IsModified,family=x.IsFamilyDocument,workshared=x.IsWorkshared}).ToArray()};
  var id=new ElementId(a.GetProperty("id").GetInt64());
  var target=d.GetElement(id) as FamilyInstance??throw new ArgumentException("Family instance required");
  if(target.Category?.Id.Value!=(long)BuiltInCategory.OST_Furniture||target.Host!=null||target.GroupId!=ElementId.InvalidElementId||target.Pinned||target.Location is not LocationPoint loc)throw new ArgumentException("Only unhosted unpinned ungrouped furniture point instances");
  var expected=a.GetProperty("expectedMm").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
  if(expected.Length!=3||expected.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000000))throw new ArgumentException("Invalid expected position");
  var p0=loc.Point;
  if(p0.DistanceTo(new XYZ(Mm(expected[0]),Mm(expected[1]),Mm(expected[2])))>Mm(0.1))throw new InvalidOperationException("Stale position");
  var ids=a.GetProperty("obstacleIds").EnumerateArray().Select(v=>new ElementId(v.GetInt64())).Distinct().ToArray();
  if(ids.Length<1||ids.Length>30||ids.Contains(id))throw new ArgumentException("1-30 distinct obstacles");
  var clearance=Number("minimumMm",1,3000);var limit=Number("maxMoveMm",1,5000);
  (XYZ min,XYZ max) Bounds(Element e){
   var b=e.get_BoundingBox(null)??throw new ArgumentException("Missing bounds "+e.Id.Value);
   var cs=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(cs.Min(q=>q.X),cs.Min(q=>q.Y),cs.Min(q=>q.Z)),new XYZ(cs.Max(q=>q.X),cs.Max(q=>q.Y),cs.Max(q=>q.Z)));
  }
  var original=Bounds(target);var obstacles=ids.Select(i=>Bounds(d.GetElement(i)??throw new ArgumentException("Missing obstacle "+i.Value))).ToArray();
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
  if(mode=="diagnose")return new{ id=id.Value,positionMm=Vec(p0),proposedDeltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),predictedGapsMm=Dists((original.min+valid,original.max+valid)).Select(ToMm).ToArray(),candidateCount=candidates.Length,scope="Explicit obstacles only, conservative AABB"};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  if(valid.GetLength()>Mm(0.01))ElementTransformUtils.MoveElement(d,id,valid);
  d.Regenerate();var actual=Bounds(target);var after=Dists(actual);
  if(after.Any(x=>x<clearance-Mm(0.01))||((LocationPoint)target.Location).Point.DistanceTo(p0+valid)>Mm(0.1)||(actual.max-actual.min).DistanceTo(original.max-original.min)>Mm(0.1))throw new InvalidOperationException("Repair verification failed");
  for(int i=0;i<ids.Length;i++){var b=Bounds(d.GetElement(ids[i]));if(b.min.DistanceTo(obstacles[i].min)>Mm(0.1)||b.max.DistanceTo(obstacles[i].max)>Mm(0.1))throw new InvalidOperationException("Obstacle moved");}
  return new{id=id.Value,deltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),afterGapsMm=after.Select(ToMm).ToArray(),finalMm=Vec(((LocationPoint)target.Location).Point),candidates=candidates.Length,scope="Explicit obstacles only; conservative AABB; no door swings or circulation validation"};
 }
}