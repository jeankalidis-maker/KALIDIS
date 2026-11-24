using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.MepTopology;
public sealed class MepTopology : IReviewedOperation {
 public string Name=>"mep_topology";
 public object Execute(Document d,JsonElement a){
  var ids=a.GetProperty("ids").EnumerateArray().Select(v=>v.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>100)throw new ArgumentException("1-100 IDs");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  var rows=new List<object>();int open=0,connected=0;
  foreach(long id in ids){
   var e=d.GetElement(new ElementId(id))??throw new ArgumentException("Missing "+id);
   var manager=e is MEPCurve c?c.ConnectorManager:(e as FamilyInstance)?.MEPModel?.ConnectorManager;
   if(manager==null)throw new ArgumentException("No connector manager "+id);
   foreach(Connector con in manager.Connectors){
    if(con.ConnectorType!=ConnectorType.End)continue;
    var refs=con.AllRefs.Cast<Connector>().Where(x=>x.Owner.Id!=e.Id&&x.ConnectorType==ConnectorType.End&&con.IsConnectedTo(x)).Select(x=>new{ownerId=x.Owner.Id.Value,connectorId=x.Id}).ToArray();
    if(con.IsConnected)connected++;else open++;
    var p=con.Origin;
    rows.Add(new{ownerId=id,connectorId=con.Id,domain=con.Domain.ToString(),shape=con.Shape.ToString(),isConnected=con.IsConnected,originMm=new[]{Mm(p.X),Mm(p.Y),Mm(p.Z)},physicalEndRefs=refs});
   }
  }
  return new{elements=ids.Length,openEnds=open,connectedEnds=connected,connectors=rows,note="Open ends may be intentional. End-connector topology is not flow calculation or system commissioning."};
 }
}