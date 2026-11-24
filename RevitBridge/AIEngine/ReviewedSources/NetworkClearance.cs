using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.NetworkClearance;
public sealed class NetworkClearance : IReviewedOperation {
 public string Name=>"network_clearance";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double ToMm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double Number(string k,double lo,double hi){double x=a.GetProperty(k).GetDouble();if(!double.IsFinite(x)||x<lo||x>hi)throw new ArgumentException(k);return Mm(x);}
  var mode=a.GetProperty("mode").GetString();
  var members=a.GetProperty("ids").EnumerateArray().Select(x=>new ElementId(x.GetInt64())).Distinct().ToArray();
  if(members.Length<2||members.Length>20)throw new ArgumentException("2-20 network members");
  var elements=members.Select(id=>d.GetElement(id)??throw new ArgumentException("Missing member")).ToArray();
  ConnectorManager Manager(Element e)=>e is MEPCurve c?c.ConnectorManager:(e as FamilyInstance)?.MEPModel?.ConnectorManager??throw new ArgumentException("No connectors");
  foreach(var e in elements){
   if(e.Pinned||e.GroupId!=ElementId.InvalidElementId||e is FamilyInstance f&&f.Host!=null)throw new ArgumentException("Protected member");
   if(e is MEPCurve c){if(c is not Autodesk.Revit.DB.Plumbing.Pipe&&c is not Autodesk.Revit.DB.Mechanical.Duct||c.Location is not LocationCurve lc||lc.Curve is not Line)throw new ArgumentException("Straight pipes or ducts only");}
   else if(e is not FamilyInstance||e.Category?.Id.Value!=(long)BuiltInCategory.OST_PipeFitting&&e.Category?.Id.Value!=(long)BuiltInCategory.OST_DuctFitting)throw new ArgumentException("Only pipe/duct fittings");
   if(e.GetDependentElements(null).Select(d.GetElement).Any(q=>q is Dimension||q is IndependentTag||q is FamilyInstance f2&&!members.Contains(f2.Id)))throw new ArgumentException("Protected dependent");
  }
  (long owner,int key,XYZ p,string links)[] Topology(){
   var rows=new List<(long,int,XYZ,string)>();
   foreach(var e in elements)foreach(Connector c in Manager(e).Connectors){
    if(c.ConnectorType!=ConnectorType.End)continue;
    var refs=c.AllRefs.Cast<Connector>().Where(q=>q.Owner.Id!=e.Id&&q.ConnectorType==ConnectorType.End&&c.IsConnectedTo(q)).ToArray();
    if(refs.Any(q=>!members.Contains(q.Owner.Id))||c.IsConnected&&refs.Length==0)throw new ArgumentException("Network has external or unsupported physical connections");
    rows.Add((e.Id.Value,c.Id,c.Origin,string.Join(",",refs.Select(q=>q.Owner.Id.Value+":"+q.Id).OrderBy(q=>q))));
   }
   return rows.OrderBy(q=>q.Item1).ThenBy(q=>q.Item2).ToArray();
  }
  var topology=Topology();
  var visited=new HashSet<long>{members[0].Value};
  for(int k=0;k<members.Length;k++)foreach(var row in topology.Where(q=>visited.Contains(q.owner)).ToArray())foreach(var x in row.links.Split(',',StringSplitOptions.RemoveEmptyEntries))visited.Add(long.Parse(x.Split(':')[0]));
  if(visited.Count!=members.Length)throw new ArgumentException("Members must form one physically connected component");
  var types=elements.Select(e=>e.GetTypeId()).ToArray();
  var p0=topology.Aggregate(XYZ.Zero,(sum,q)=>sum+q.p)/topology.Length;
  var expected=a.GetProperty("expectedMm").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
  if(expected.Length!=3||expected.Any(v=>!double.IsFinite(v)||Math.Abs(v)>1000000)||p0.DistanceTo(new XYZ(Mm(expected[0]),Mm(expected[1]),Mm(expected[2])))>Mm(.1))throw new InvalidOperationException("Stale network connector centroid");
  var clearance=Number("minimumMm",1,3000);var limit=Number("maxMoveMm",1,5000);
  (XYZ min,XYZ max) Bounds(Element e){
   var b=e.get_BoundingBox(null)??throw new ArgumentException("Missing bounds "+e.Id.Value);
   var cs=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();
   return(new XYZ(cs.Min(q=>q.X),cs.Min(q=>q.Y),cs.Min(q=>q.Z)),new XYZ(cs.Max(q=>q.X),cs.Max(q=>q.Y),cs.Max(q=>q.Z)));
  }
  (XYZ min,XYZ max) Union(){var b=elements.Select(Bounds).ToArray();return(new XYZ(b.Min(q=>q.min.X),b.Min(q=>q.min.Y),b.Min(q=>q.min.Z)),new XYZ(b.Max(q=>q.max.X),b.Max(q=>q.max.Y),b.Max(q=>q.max.Z)));}
  var original=Union();
  var supported=new[]{BuiltInCategory.OST_Furniture,BuiltInCategory.OST_Walls,BuiltInCategory.OST_StructuralColumns,BuiltInCategory.OST_Columns,BuiltInCategory.OST_Casework,BuiltInCategory.OST_Doors,BuiltInCategory.OST_Windows,BuiltInCategory.OST_SpecialityEquipment,BuiltInCategory.OST_PipeCurves,BuiltInCategory.OST_DuctCurves,BuiltInCategory.OST_Conduit,BuiltInCategory.OST_CableTray,BuiltInCategory.OST_StructuralFraming};
  var categories=a.GetProperty("categoryIds").EnumerateArray().Select(v=>(BuiltInCategory)v.GetInt64()).Distinct().ToArray();
  if(categories.Length<1||categories.Any(c=>!supported.Contains(c)))throw new ArgumentException("Unsupported obstacle category");
  var expansion=new XYZ(limit+clearance,limit+clearance,clearance);
  var outline=new Outline(original.min-expansion,original.max+expansion);
  var found=new FilteredElementCollector(d).WhereElementIsNotElementType().WherePasses(new ElementMulticategoryFilter(categories.ToList())).WherePasses(new BoundingBoxIntersectsFilter(outline)).Where(e=>!members.Contains(e.Id)).Take(31).ToArray();
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
  if(mode=="diagnose")return new{ memberIds=members.Select(q=>q.Value).ToArray(),positionMm=Vec(p0),proposedDeltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),predictedGapsMm=Dists((original.min+valid,original.max+valid)).Select(ToMm).ToArray(),candidateCount=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),scope="Whole isolated connected component translation; selected local obstacles"};
  if(mode!="repair")throw new ArgumentException("diagnose or repair");
  if(valid.GetLength()>Mm(0.01))ElementTransformUtils.MoveElements(d,members.ToList(),valid);
  d.Regenerate();var actual=Union();var after=Dists(actual);
  if(after.Any(x=>x<clearance-Mm(0.01))||(actual.max-actual.min).DistanceTo(original.max-original.min)>Mm(0.1))throw new InvalidOperationException("Repair verification failed");
  var afterTopology=Topology();
  if(afterTopology.Length!=topology.Length||elements.Where((e,k)=>e.GetTypeId()!=types[k]).Any()||afterTopology.Where((q,k)=>q.owner!=topology[k].owner||q.key!=topology[k].key||q.links!=topology[k].links||q.p.DistanceTo(topology[k].p+valid)>Mm(.1)).Any())throw new InvalidOperationException("Network topology, type or connector translation changed");
  for(int i=0;i<ids.Length;i++){var b=Bounds(d.GetElement(ids[i]));if(b.min.DistanceTo(obstacles[i].min)>Mm(0.1)||b.max.DistanceTo(obstacles[i].max)>Mm(0.1))throw new InvalidOperationException("Obstacle moved");}
  return new{memberIds=members.Select(q=>q.Value).ToArray(),connectedEnds=afterTopology.Count(q=>q.links.Length>0),topologyPreserved=true,deltaMm=Vec(valid),beforeGapsMm=before.Select(ToMm).ToArray(),afterGapsMm=after.Select(ToMm).ToArray(),finalMm=Vec(p0+valid),candidates=candidates.Length,obstacleIds=ids.Select(i=>i.Value).ToArray(),scope="Rigid translation of complete isolated network, not rerouting or hydraulic design"};
 }
}