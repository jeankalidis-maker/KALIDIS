using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Architecture;
using Autodesk.Revit.DB.Structure;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.OrthogonalPlan;
public sealed class OrthogonalPlan:IReviewedOperation {
 public string Name=>"orthogonal_plan_preflight";
 public object Execute(Document d,JsonElement a){
  double M(double x)=>x/304.8;
  T E<T>(string k) where T:Element=>d.GetElement(new ElementId(a.GetProperty(k).GetInt64())) as T??throw new ArgumentException(k);
  var level=E<Level>("levelId");var wt=E<WallType>("wallTypeId");var ft=E<FloorType>("floorTypeId");var dt=E<FamilySymbol>("doorTypeId");
  var h=a.GetProperty("heightMm").GetDouble();if(!double.IsFinite(h)||h<2200||h>6000||wt.Kind!=WallKind.Basic||dt.Category?.Id.Value!=(long)BuiltInCategory.OST_Doors)throw new ArgumentException("Invalid building types or height");
  XYZ P(JsonElement j){var v=j.EnumerateArray().Select(x=>x.GetDouble()).ToArray();if(v.Length!=2||v.Any(x=>!double.IsFinite(x)||Math.Abs(x)>1000000))throw new ArgumentException("XY point");return new XYZ(M(v[0]),M(v[1]),level.ProjectElevation);}
  Line L(XYZ x,XYZ y){if(x.DistanceTo(y)<M(500)||Math.Abs(x.X-y.X)>M(.01)&&Math.Abs(x.Y-y.Y)>M(.01))throw new ArgumentException("Orthogonal line required");return Line.CreateBound(x,y);}
  var perimeter=a.GetProperty("perimeter").EnumerateArray().Select(P).ToArray();var partitions=a.GetProperty("partitions").EnumerateArray().Select(j=>L(P(j.GetProperty("start")),P(j.GetProperty("end")))).ToArray();
  var seeds=a.GetProperty("rooms").EnumerateArray().Select(P).ToArray();var doors=a.GetProperty("doors").EnumerateArray().ToArray();
  if(perimeter.Length<4||perimeter.Length>40||partitions.Length>30||seeds.Length<1||seeds.Length>30||doors.Length>30)throw new ArgumentException("Plan size");
  var lines=Enumerable.Range(0,perimeter.Length).Select(i=>L(perimeter[i],perimeter[(i+1)%perimeter.Length])).Concat(partitions).ToArray();
  var width=dt.get_Parameter(BuiltInParameter.DOOR_WIDTH)?.AsDouble()??0;var height=dt.get_Parameter(BuiltInParameter.DOOR_HEIGHT)?.AsDouble()??0;
  if(width<M(600)||height<=0||height>=M(h))throw new ArgumentException("Invalid door size");
  bool Inside(XYZ p){bool b=false;for(int i=0,j=perimeter.Length-1;i<perimeter.Length;j=i++){var x=perimeter[i];var y=perimeter[j];if((x.Y>p.Y)!=(y.Y>p.Y)&&p.X<(y.X-x.X)*(p.Y-x.Y)/(y.Y-x.Y)+x.X)b=!b;}return b;}

  // Topology preflight: reject ambiguous or invalid geometry before model writes.
  double tol=M(.1);
  bool Near(XYZ x,XYZ y)=>x.DistanceTo(y)<=tol;
  bool On(Line l,XYZ p)=>l.Distance(p)<=tol &&
    p.X>=Math.Min(l.GetEndPoint(0).X,l.GetEndPoint(1).X)-tol &&
    p.X<=Math.Max(l.GetEndPoint(0).X,l.GetEndPoint(1).X)+tol &&
    p.Y>=Math.Min(l.GetEndPoint(0).Y,l.GetEndPoint(1).Y)-tol &&
    p.Y<=Math.Max(l.GetEndPoint(0).Y,l.GetEndPoint(1).Y)+tol;
  bool Horizontal(Line l)=>Math.Abs(l.GetEndPoint(0).Y-l.GetEndPoint(1).Y)<=tol;
  bool Overlap(Line l,Line m){
    var x=l.GetEndPoint(0);var y=l.GetEndPoint(1);var u=m.GetEndPoint(0);var v=m.GetEndPoint(1);
    if(Horizontal(l)!=Horizontal(m))return false;
    if(Horizontal(l))return Math.Abs(x.Y-u.Y)<=tol &&
      Math.Min(Math.Max(x.X,y.X),Math.Max(u.X,v.X))-Math.Max(Math.Min(x.X,y.X),Math.Min(u.X,v.X))>tol;
    return Math.Abs(x.X-u.X)<=tol &&
      Math.Min(Math.Max(x.Y,y.Y),Math.Max(u.Y,v.Y))-Math.Max(Math.Min(x.Y,y.Y),Math.Min(u.Y,v.Y))>tol;
  }
  bool Hit(Line l,Line m){
    if(Overlap(l,m)||On(l,m.GetEndPoint(0))||On(l,m.GetEndPoint(1))||On(m,l.GetEndPoint(0))||On(m,l.GetEndPoint(1)))return true;
    if(Horizontal(l)==Horizontal(m))return false;
    var hline=Horizontal(l)?l:m;var vline=Horizontal(l)?m:l;
    var p=new XYZ(vline.GetEndPoint(0).X,hline.GetEndPoint(0).Y,level.ProjectElevation);
    return On(l,p)&&On(m,p);
  }
  double area2=0;
  for(int i=0;i<perimeter.Length;i++){
    var p=perimeter[i];var q=perimeter[(i+1)%perimeter.Length];
    area2+=p.X*q.Y-q.X*p.Y;
    for(int j=i+1;j<perimeter.Length;j++){
      bool adjacent=j==i+1||(i==0&&j==perimeter.Length-1);
      if(Near(p,perimeter[j]))throw new ArgumentException("Duplicate perimeter vertex");
      if(Overlap(lines[i],lines[j])||(!adjacent&&Hit(lines[i],lines[j])))throw new ArgumentException("Self-intersecting perimeter");
    }
  }
  if(Math.Abs(area2)/2<M(1000)*M(1000))throw new ArgumentException("Perimeter area too small");
  for(int i=perimeter.Length;i<lines.Length;i++){
    var l=lines[i];
    for(int j=0;j<i;j++)if(Overlap(l,lines[j]))throw new ArgumentException("Duplicate or overlapping partition");
    var cuts=new List<double>{0,1};var p=l.GetEndPoint(0);var q=l.GetEndPoint(1);
    for(int j=0;j<perimeter.Length;j++){
      var edge=lines[j];
      if(Horizontal(l)==Horizontal(edge))continue;
      var hline=Horizontal(l)?l:edge;var vline=Horizontal(l)?edge:l;
      var cross=new XYZ(vline.GetEndPoint(0).X,hline.GetEndPoint(0).Y,level.ProjectElevation);
      if(On(l,cross)&&On(edge,cross))cuts.Add((cross-p).DotProduct(q-p)/(q-p).DotProduct(q-p));
    }
    cuts=cuts.Distinct().OrderBy(x=>x).ToList();
    for(int k=1;k<cuts.Count;k++){
      var mid=p+(q-p)*((cuts[k]+cuts[k-1])/2);
      if(!Inside(mid))throw new ArgumentException("Partition exits perimeter");
    }
    foreach(var end in new[]{p,q})if(!Inside(end)&&!lines.Take(perimeter.Length).Any(e=>On(e,end)))throw new ArgumentException("Partition endpoint outside");
  }

  if(seeds.Any(p=>!Inside(p)||lines.Any(l=>l.Distance(p)<M(200))))throw new ArgumentException("Room seed outside or too close to wall");
  var bounds=new Outline(new XYZ(perimeter.Min(p=>p.X)-M(500),perimeter.Min(p=>p.Y)-M(500),level.ProjectElevation-M(500)),new XYZ(perimeter.Max(p=>p.X)+M(500),perimeter.Max(p=>p.Y)+M(500),level.ProjectElevation+M(h+500)));
  if(new FilteredElementCollector(d).WhereElementIsNotElementType().WherePasses(new BoundingBoxIntersectsFilter(bounds)).Any(e=>e is Wall||e is Floor||e is FamilyInstance))throw new InvalidOperationException("Target region occupied");
  var walls=lines.Select(l=>Wall.Create(d,l,wt.Id,level.Id,M(h),0,false,false)).ToArray();
  var loop=new CurveLoop();foreach(var l in lines.Take(perimeter.Length))loop.Append(l);var floor=Floor.Create(d,new List<CurveLoop>{loop},ft.Id,level.Id);
  if(!dt.IsActive){dt.Activate();d.Regenerate();}
  var instances=new List<FamilyInstance>();
  foreach(var j in doors){
   int k=j.GetProperty("wallIndex").GetInt32();var p=P(j.GetProperty("point"));var w=j.GetProperty("widthMm").GetDouble();
   if(k<0||k>=walls.Length||Math.Abs(M(w)-width)>M(.1)||lines[k].Distance(p)>M(.1)||p.DistanceTo(lines[k].GetEndPoint(0))<width/2+M(200)||p.DistanceTo(lines[k].GetEndPoint(1))<width/2+M(200))throw new ArgumentException("Door width or location mismatch");
   if(instances.Any(v=>((LocationPoint)v.Location).Point.DistanceTo(p)<width+M(100)))throw new ArgumentException("Overlapping doors");
   var inst=d.Create.NewFamilyInstance(p,dt,walls[k],level,StructuralType.NonStructural);if(inst.Host?.Id!=walls[k].Id)throw new InvalidOperationException("Host mismatch");instances.Add(inst);
  }
  d.Regenerate();var rooms=seeds.Select((p,i)=>{var r=d.Create.NewRoom(level,new UV(p.X,p.Y));r.Name="Raster "+(i+1);return r;}).ToArray();d.Regenerate();
  if(rooms.Any(r=>r.Area<M(1000)*M(1000)||r.GetBoundarySegments(new SpatialElementBoundaryOptions())==null)||rooms.Select(r=>r.Id).Distinct().Count()!=seeds.Length)throw new InvalidOperationException("Enclosure failed");
  for(int i=0;i<walls.Length;i++)if(Math.Abs(((LocationCurve)walls[i].Location).Curve.Length-lines[i].Length)>M(.1))throw new InvalidOperationException("Wall length mismatch");
  return new{wallIds=walls.Select(x=>x.Id.Value).ToArray(),floorId=floor.Id.Value,doorIds=instances.Select(x=>x.Id.Value).ToArray(),roomIds=rooms.Select(x=>x.Id.Value).ToArray(),roomAreasM2=rooms.Select(x=>x.Area*.09290304).ToArray()};
 }
}
