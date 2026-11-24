// Explicit connection of matching, coincident, opposed MEP end connectors.
using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.MepConnect;
public sealed class MepConnect : IReviewedOperation {
 public string Name=>"mep_connect";
 public object Execute(Document d,JsonElement a){
  double Mm(double n)=>UnitUtils.ConvertFromInternalUnits(n,UnitTypeId.Millimeters);
  Connector Get(string key,string ck){
   var e=d.GetElement(new ElementId(a.GetProperty(key).GetInt64()))??throw new ArgumentException("Missing element");
   if(e.Pinned||e.GroupId!=ElementId.InvalidElementId)throw new ArgumentException("Pinned or grouped element");
   var m=e is MEPCurve c?c.ConnectorManager:(e as FamilyInstance)?.MEPModel?.ConnectorManager;
   if(m==null)throw new ArgumentException("Missing connector manager");
   var con=m.Lookup(a.GetProperty(ck).GetInt32())??throw new ArgumentException("Missing connector");
   if(con.ConnectorType!=ConnectorType.End||con.IsConnected)throw new ArgumentException("Open end connector required");
   return con;
  }
  var x=Get("firstId","firstConnector");var y=Get("secondId","secondConnector");
  if(x.Owner.Id==y.Owner.Id||x.Domain!=y.Domain||x.Shape!=y.Shape)throw new ArgumentException("Incompatible connector pair");
  if(Mm(x.Origin.DistanceTo(y.Origin))>0.1)throw new ArgumentException("Connector gap exceeds 0.1 mm");
  if(x.CoordinateSystem.BasisZ.DotProduct(y.CoordinateSystem.BasisZ)>-0.999)throw new ArgumentException("End axes must oppose each other");
  if(x.Shape==ConnectorProfileType.Round){
   if(Math.Abs(Mm(x.Radius-y.Radius))>0.1)throw new ArgumentException("Radius mismatch");
  }else if(x.Shape==ConnectorProfileType.Rectangular||x.Shape==ConnectorProfileType.Oval){
   if(Math.Abs(Mm(x.Width-y.Width))>0.1||Math.Abs(Mm(x.Height-y.Height))>0.1)throw new ArgumentException("Section mismatch");
  }else throw new ArgumentException("Unsupported profile");
  var first=x.Owner.Id.Value;var second=y.Owner.Id.Value;
  x.ConnectTo(y);d.Regenerate();
  bool direct=x.IsConnectedTo(y);
  var refs=x.AllRefs.Cast<Connector>().Where(c=>c.Owner.Id!=x.Owner.Id&&c.ConnectorType==ConnectorType.End&&x.IsConnectedTo(c)).Select(c=>c.Owner.Id.Value).ToArray();
  if(!direct)throw new InvalidOperationException("No direct connection confirmed; fitting may be necessary");
  return new{firstId=first,secondId=second,directConnection=direct,physicalReferences=refs,note="Physical connectivity only, not a complete commissioned system."};
 }
}