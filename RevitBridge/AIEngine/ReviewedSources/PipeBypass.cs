using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Plumbing;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.PipeBypass;
public sealed class PipeBypass:IReviewedOperation{
 public string Name=>"pipe_bypass";
 public object Execute(Document d,JsonElement a){
  double Mm(double x)=>UnitUtils.ConvertToInternalUnits(x,UnitTypeId.Millimeters);
  double Out(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  double N(string k,double lo,double hi){var v=a.GetProperty(k).GetDouble();if(!double.IsFinite(v)||v<lo||v>hi)throw new ArgumentException(k);return Mm(v);}
  var pipe=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as Pipe??throw new ArgumentException("Pipe required");
  if(pipe.Pinned||pipe.GroupId!=ElementId.InvalidElementId||pipe.Location is not LocationCurve lc||lc.Curve is not Line line||Math.Abs(line.Direction.Z)>1e-8)throw new ArgumentException("Independent horizontal straight pipe required");
  var id=pipe.Id;var start=line.GetEndPoint(0);var end=line.GetEndPoint(1);var dir=line.Direction;var length=line.Length;
  var offset=N("offsetMm",-5000,5000);var leg=N("endLegMm",500,10000);var clearance=N("minimumMm",1,2000);
  if(Math.Abs(offset)<Mm(500)||length-2*leg<Mm(1000))throw new ArgumentException("Insufficient bypass length");
  var expected=a.GetProperty("expectedMm").EnumerateArray().Select(v=>v.GetDouble()).ToArray();
  if(expected.Length!=3||expected.Any(v=>!double.IsFinite(v))||line.Evaluate(.5,true).DistanceTo(new XYZ(Mm(expected[0]),Mm(expected[1]),Mm(expected[2])))>Mm(.1))throw new InvalidOperationException("Stale midpoint");
  if(pipe.GetDependentElements(null).Select(d.GetElement).Any(q=>q is Dimension||q is IndependentTag||q is FamilyInstance||q is InsulationLiningBase))throw new ArgumentException("Protected dependency");
  Connector[] Ends(Pipe p)=>p.ConnectorManager.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).ToArray();
  Connector At(Pipe p,XYZ q)=>Ends(p).OrderBy(c=>c.Origin.DistanceTo(q)).First();
  (ElementId id,int key,XYZ point) External(XYZ p){
   var c=At(pipe,p);var r=c.AllRefs.Cast<Connector>().Where(x=>x.Owner.Id!=id&&x.ConnectorType==ConnectorType.End&&c.IsConnectedTo(x)).ToArray();
   if(r.Length!=1||r[0].Owner is not Pipe neighbor||neighbor.Pinned||neighbor.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Exactly one unprotected external pipe at each end");
   return(neighbor.Id,r[0].Id,r[0].Origin);
  }
  var e0=External(start);var e1=External(end);if(e0.id==e1.id)throw new ArgumentException("Distinct external pipes required");
  var type=pipe.GetTypeId();var system=pipe.get_Parameter(BuiltInParameter.RBS_PIPING_SYSTEM_TYPE_PARAM)?.AsElementId()??ElementId.InvalidElementId;var level=pipe.ReferenceLevel.Id;
  var diameter=pipe.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble();
  if(system==ElementId.InvalidElementId)throw new ArgumentException("System type missing");
  var obstacles=a.GetProperty("obstacleIds").EnumerateArray().Select(v=>new ElementId(v.GetInt64())).Distinct().ToArray();
  if(obstacles.Length<1||obstacles.Length>20||obstacles.Any(v=>v==id||v==e0.id||v==e1.id))throw new ArgumentException("1-20 explicit obstacles");
  (XYZ min,XYZ max) Bounds(Element e){var b=e.get_BoundingBox(null)??throw new ArgumentException("No bounds");var pts=(from x in new[]{b.Min.X,b.Max.X} from y in new[]{b.Min.Y,b.Max.Y} from z in new[]{b.Min.Z,b.Max.Z} select b.Transform.OfPoint(new XYZ(x,y,z))).ToArray();return(new XYZ(pts.Min(p=>p.X),pts.Min(p=>p.Y),pts.Min(p=>p.Z)),new XYZ(pts.Max(p=>p.X),pts.Max(p=>p.Y),pts.Max(p=>p.Z)));}
  var obs=obstacles.Select(v=>Bounds(d.GetElement(v)??throw new ArgumentException("Missing obstacle"))).ToArray();
  double Gap((XYZ min,XYZ max) a,(XYZ min,XYZ max) b){double G(double x,double y,double u,double v)=>Math.Max(0,Math.Max(u-y,x-v));var x=G(a.min.X,a.max.X,b.min.X,b.max.X);var y=G(a.min.Y,a.max.Y,b.min.Y,b.max.Y);var z=G(a.min.Z,a.max.Z,b.min.Z,b.max.Z);return Math.Sqrt(x*x+y*y+z*z);}
  var oldBounds=Bounds(pipe);var before=obs.Min(v=>Gap(oldBounds,v));
  if(before>=clearance)throw new ArgumentException("Existing pipe already satisfies requested clearance");
  var side=XYZ.BasisZ.CrossProduct(dir)*offset;
  var points=new[]{start,start+dir*leg,start+dir*leg+side,end-dir*leg+side,end-dir*leg,end};
  var deleted=d.Delete(id);if(deleted.Any(v=>v!=id))throw new InvalidOperationException("Unexpected dependent deletion");
  d.Regenerate();
  var pipes=new List<Pipe>();for(int i=0;i<5;i++){var p=Pipe.Create(d,system,type,level,points[i],points[i+1]);p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).Set(diameter);pipes.Add(p);}
  d.Regenerate();var fittings=new List<FamilyInstance>();
  for(int i=0;i<4;i++){var f=d.Create.NewElbowFitting(At(pipes[i],points[i+1]),At(pipes[i+1],points[i+1]));if(f==null)throw new InvalidOperationException("Elbow creation failed");fittings.Add(f);d.Regenerate();}
  void Connect(Pipe p,XYZ endpoint,(ElementId id,int key,XYZ point) ext){
   var n=d.GetElement(ext.id) as Pipe??throw new InvalidOperationException("Neighbor lost");
   var c=n.ConnectorManager.Lookup(ext.key);var x=At(p,endpoint);if(c==null||c.Origin.DistanceTo(ext.point)>Mm(.1)||x.Origin.DistanceTo(endpoint)>Mm(.1)||c.IsConnected||x.IsConnected)throw new InvalidOperationException("Endpoint changed or occupied");
   x.ConnectTo(c);d.Regenerate();if(!x.IsConnectedTo(c))throw new InvalidOperationException("External connection not restored");
  }
  Connect(pipes[0],start,e0);Connect(pipes[4],end,e1);
  var all=pipes.Cast<Element>().Concat(fittings).ToArray();
  var after=all.Min(e=>obs.Min(o=>Gap(Bounds(e),o)));
  if(after<clearance-Mm(.01))throw new InvalidOperationException("Bypass clearance insufficient: "+Out(after));
  if(pipes.Any(p=>p.GetTypeId()!=type||Math.Abs(p.get_Parameter(BuiltInParameter.RBS_PIPE_DIAMETER_PARAM).AsDouble()-diameter)>Mm(.1)||Ends(p).Any(c=>!c.IsConnected))||fittings.Any(f=>f.MEPModel.ConnectorManager.Connectors.Cast<Connector>().Where(c=>c.ConnectorType==ConnectorType.End).Any(c=>!c.IsConnected)))throw new InvalidOperationException("Network verification failed");
  return new{replacedId=id.Value,pipeIds=pipes.Select(p=>p.Id.Value).ToArray(),fittingIds=fittings.Select(f=>f.Id.Value).ToArray(),neighborIds=new[]{e0.id.Value,e1.id.Value},beforeGapMm=Out(before),afterGapMm=Out(after),externalEndpointsPreserved=true,note="Experimental horizontal offset with four elbows; explicit obstacles only, not hydraulic sizing or code certification. Original segment ID replaced."};
 }
}