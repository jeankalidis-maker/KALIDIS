using Autodesk.Revit.DB;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.RealisticPreview;
public sealed class RealisticPreview:IReviewedOperation{
 public string Name=>"realistic_preview";
 public object Execute(Document d,JsonElement a){
  var id=new ElementId(a.GetProperty("id").GetInt64());var e=d.GetElement(id)??throw new ArgumentException("Element required");
  var b=e.get_BoundingBox(null)??throw new ArgumentException("Missing bounds");
  var t=new FilteredElementCollector(d).OfClass(typeof(ViewFamilyType)).Cast<ViewFamilyType>().First(x=>x.ViewFamily==ViewFamily.ThreeDimensional);
  var v=View3D.CreateIsometric(d,t.Id);v.Name="Kalidis Realistic "+Guid.NewGuid().ToString("N")[..8];
  var pad=UnitUtils.ConvertToInternalUnits(500,UnitTypeId.Millimeters);var p=new XYZ(pad,pad,pad);
  v.SetSectionBox(new BoundingBoxXYZ{Min=b.Min-p,Max=b.Max+p});v.IsSectionBoxActive=true;
  var center=(b.Min+b.Max)/2;var forward=new XYZ(-1,1,-1).Normalize();var right=forward.CrossProduct(XYZ.BasisZ).Normalize();var up=right.CrossProduct(forward).Normalize();
  v.SetOrientation(new ViewOrientation3D(center-forward*20,up,forward));v.DetailLevel=ViewDetailLevel.Fine;v.DisplayStyle=DisplayStyle.Realistic;
  d.Regenerate();if(v.DisplayStyle!=DisplayStyle.Realistic||!v.IsSectionBoxActive)throw new InvalidOperationException("Preview settings mismatch");
  return new{viewId=v.Id.Value,name=v.Name,style=v.DisplayStyle.ToString(),elementId=id.Value,note="Section box around element; nearby objects may appear."};
 }
}