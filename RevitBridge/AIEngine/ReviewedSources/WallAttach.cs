using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.WallAttach;
public sealed class WallAttach : IReviewedOperation {
 public string Name=>"wall_attach_roof";
 public object Execute(Document d,JsonElement a){
  var roof=d.GetElement(new ElementId(a.GetProperty("roofId").GetInt64())) as RoofBase??throw new ArgumentException("Roof required");
  var ids=a.GetProperty("wallIds").EnumerateArray().Select(v=>v.GetInt64()).Distinct().ToArray();
  if(ids.Length<1||ids.Length>20)throw new ArgumentException("1-20 walls");
  double Mm(double x)=>UnitUtils.ConvertFromInternalUnits(x,UnitTypeId.Millimeters);
  var rows=new List<object>();
  foreach(var id in ids){
   var w=d.GetElement(new ElementId(id)) as Wall??throw new ArgumentException("Invalid wall");
   if(w.Pinned||w.GroupId!=ElementId.InvalidElementId||w.GetAttachmentIds(AttachmentLocation.Top).Count!=0)throw new ArgumentException("Existing attachment, pin or group");
   var before=w.get_BoundingBox(null)??throw new ArgumentException("Missing bounds");
   w.AddAttachment(roof.Id,AttachmentLocation.Top);d.Regenerate();
   if(!w.GetAttachmentIds(AttachmentLocation.Top).Contains(roof.Id))throw new InvalidOperationException("Attachment readback failed");
   var after=w.get_BoundingBox(null)??throw new InvalidOperationException("Missing attached bounds");
   rows.Add(new{id,beforeTopMm=Mm(before.Max.Z),afterTopMm=Mm(after.Max.Z),roofId=roof.Id.Value});
  }
  return new{walls=rows,note="Native top attachment verified; structural and envelope design not certified."};
 }
}