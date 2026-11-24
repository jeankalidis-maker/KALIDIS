using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.LinkedRepair;
public sealed class LinkedRepair : IReviewedOperation {
 public string Name=>"linked_repair";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double Number(string k,double lo,double hi){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||x<lo||x>hi)throw new ArgumentException(k);return Mm(x);}
  var mode=a.GetProperty("mode").GetString();
  if(mode=="load_fixture"){
   string root=Path.GetDirectoryName(d.PathName)??throw new ArgumentException("Missing sandbox");
   string file=a.GetProperty("fileName").GetString()??throw new ArgumentException("Filename");
   if(Path.GetFileName(file)!=file||!file.StartsWith("LinkFixture-",StringComparison.Ordinal)||!file.EndsWith(".rvt",StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Only dedicated test fixture");
   string path=Path.Combine(root,file);if(!File.Exists(path)||File.GetAttributes(path).HasFlag(FileAttributes.ReparsePoint))throw new ArgumentException("Invalid fixture");
   var res=RevitLinkType.Create(d,ModelPathUtils.ConvertUserVisiblePathToModelPath(path),new RevitLinkOptions(false));
   var fixtureLink=RevitLinkInstance.Create(d,res.ElementId);d.Regenerate();
   if(fixtureLink.GetLinkDocument()==null)throw new InvalidOperationException("Link not loaded");
   return new{linkId=fixtureLink.Id.Value,typeId=res.ElementId.Value,title=fixtureLink.GetLinkDocument().Title};
  }
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
  var local=found.Select(Bounds).ToArray();
  var link=d.GetElement(new ElementId(a.GetProperty("linkId").GetInt64())) as RevitLinkInstance??throw new ArgumentException("Link instance required");
  var ld=link.GetLinkDocument()??throw new ArgumentException("Link unloaded");
  var tr=link.GetTotalTransform();
  var linkedIds=a.GetProperty("linkedIds").EnumerateArray().Select(v=>new ElementId(v.GetInt64())).Distinct().ToArray();
  if(linkedIds.Length<1||linkedIds.Length>30||local.Length+linkedIds.Length>30)throw new ArgumentException("1-30 total obstacles");
  (XYZ min,XYZ max) LinkedBounds(Element e){
   var b=Bounds(e);
   var pts=(from x in new[]{b.min.X,b.max.X} from y in new[]{b.min.Y,b.max.Y} from z in new[]{b.min.Z,b.max.Z} select tr.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(pts.Min(p=>p.X),pts.Min(p=>p.Y),pts.Min(p=>p.Z)),new XYZ(pts.Max(p=>p.X),pts.Max(p=>p.Y),pts.Max(p=>p.Z)));
  }
  var linked=linkedIds.Select(i=>LinkedBounds(ld.GetElement(i)??throw new ArgumentException("Missing linked element"))).ToArray();
  var obstacles=local.Concat(linked).ToArray();
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
  if(mode=="diagnose")return new{ id=id.Value,positionMm=Vec(p0),proposedDeltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),predictedGapsMm=Dists((original.min+valid,original.max+valid)).Select(ToMm).ToArray(),candidateCount=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),linkId=link.Id.Value,linkedIds=linkedIds.Select(i=>i.Value).ToArray(),scope="Local selected categories plus explicit linked obstacles transformed into host coordinates"};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  if(valid.GetLength()>Mm(0.01))ElementTransformUtils.MoveElement(d,id,valid);
  d.Regenerate();var actual=Bounds(target);var after=Dists(actual);
  if(after.Any(x=>x<clearance-Mm(0.01))||((LocationPoint)target.Location).Point.DistanceTo(p0+valid)>Mm(0.1)||(actual.max-actual.min).DistanceTo(original.max-original.min)>Mm(0.1))throw new InvalidOperationException("Repair verification failed");
  for(int i=0;i<ids.Length;i++){var b=Bounds(d.GetElement(ids[i]));if(b.min.DistanceTo(obstacles[i].min)>Mm(0.1)||b.max.DistanceTo(obstacles[i].max)>Mm(0.1))throw new InvalidOperationException("Obstacle moved");}
  for(int i=0;i<linkedIds.Length;i++){var b=LinkedBounds(ld.GetElement(linkedIds[i]));if(b.min.DistanceTo(linked[i].min)>Mm(0.1)||b.max.DistanceTo(linked[i].max)>Mm(0.1))throw new InvalidOperationException("Linked bounds changed");}
  return new{id=id.Value,deltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),afterGapsMm=after.Select(ToMm).ToArray(),finalMm=Vec(((LocationPoint)target.Location).Point),candidates=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),linkId=link.Id.Value,linkedIds=linkedIds.Select(i=>i.Value).ToArray(),scope="Local and explicitly selected linked obstacles; no linked edits or circulation validation"};
 }
}