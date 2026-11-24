// First-party solid overlap check, explicit element pairs, no automatic edits.
using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.SolidClash;
public sealed class SolidClash : IReviewedOperation {
 public string Name=>"solid_clash";
 public object Execute(Document d,JsonElement a){
  var pairs=a.GetProperty("pairs").EnumerateArray().Select(p=>p.EnumerateArray().Select(x=>x.GetInt64()).ToArray()).ToArray();
  if(pairs.Length<1||pairs.Length>20||pairs.Any(p=>p.Length!=2||p[0]==p[1]))throw new ArgumentException("1-20 distinct ID pairs");
  double tolerance=a.TryGetProperty("toleranceM3",out var t)?t.GetDouble():0.000001;
  if(!double.IsFinite(tolerance)||tolerance<0||tolerance>1)throw new ArgumentException("Invalid overlap tolerance");
  IEnumerable<Solid> Solids(GeometryElement? g,int depth){
   if(g==null||depth>8)yield break;
   foreach(var o in g){
    if(o is Solid s&&s.Faces.Size>0&&s.Volume>1e-9)yield return s;
    else if(o is GeometryInstance i)foreach(var sub in Solids(i.GetInstanceGeometry(),depth+1))yield return sub;
   }
  }
  var cache=new Dictionary<long,Solid[]>();
  Solid[] Get(long id){
   if(cache.TryGetValue(id,out var v))return v;
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing element "+id);
   if(e is SpatialElement)throw new ArgumentException("Spatial elements are not physical solids");
   v=Solids(e.get_Geometry(new Options{DetailLevel=ViewDetailLevel.Fine}),0).Take(101).ToArray();
   if(v.Length>100)throw new ArgumentException("Too many solids per element");
   cache.Add(id,v);return v;
  }
  var rows=pairs.Select(p=>{
   var x=Get(p[0]);var y=Get(p[1]);
   double max=0,sum=0;int failed=0,tests=0;
   if(x.Length*y.Length>400)throw new ArgumentException("Too many solid combinations");
   foreach(var s in x)foreach(var q in y){
    tests++;
    try{
     using var intersect=BooleanOperationsUtils.ExecuteBooleanOperation(s,q,BooleanOperationsType.Intersect);
     var v=UnitUtils.ConvertFromInternalUnits(Math.Abs(intersect.Volume),UnitTypeId.CubicMeters);
     sum+=v;max=Math.Max(max,v);
    }catch(Autodesk.Revit.Exceptions.InvalidOperationException){failed++;}
   }
   string status=max>tolerance?"overlap":failed>0||tests==0?"inconclusive":"no_overlap_above_tolerance";
   return new{firstId=p[0],secondId=p[1],status,firstSolidCount=x.Length,secondSolidCount=y.Length,tests,failed,maxPairOverlapM3=max,sumPairOverlapM3=sum};
  }).ToArray();
  return new{count=rows.Length,toleranceM3=tolerance,pairs=rows,note="Physical overlap can be intentional. Pairwise sums may double-count; no automatic correction. Failed Boolean operations are inconclusive."};
 }
}