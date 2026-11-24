using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.MaterialAppearance;
public sealed class MaterialAppearance : IReviewedOperation {
 public string Name=>"material_appearance";
 public object Execute(Document d,JsonElement a){
  var mode=a.GetProperty("mode").GetString();
  var mats=new FilteredElementCollector(d).OfClass(typeof(Material)).Cast<Material>();
  if(mode=="list"){
   var list=mats.Where(m=>m.AppearanceAssetId!=ElementId.InvalidElementId).Select(m=>{
    var asset=d.GetElement(m.AppearanceAssetId) as AppearanceAssetElement;
    var prop=asset?.GetRenderingAsset().FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d;
    return new{id=m.Id.Value,name=m.Name,assetId=m.AppearanceAssetId.Value,genericDiffuse=prop!=null};
   }).Where(x=>x.genericDiffuse).Take(100).ToArray();
   return new{materials=list};
  }
  if(mode!="duplicate")throw new ArgumentException("Unknown mode");
  var source=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as Material??throw new ArgumentException("Material required");
  var assetSource=d.GetElement(source.AppearanceAssetId) as AppearanceAssetElement??throw new ArgumentException("Source has no appearance asset");
  var original=assetSource.GetRenderingAsset().FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d??throw new ArgumentException("Generic diffuse required");
  if(original.NumberOfConnectedProperties>0)throw new ArgumentException("Textured source not supported");
  var rgb=a.GetProperty("rgb").EnumerateArray().Select(v=>v.GetByte()).ToArray();if(rgb.Length!=3)throw new ArgumentException("RGB required");
  var name=a.GetProperty("name").GetString()??throw new ArgumentException("Name required");
  var mat=source.Duplicate(name);var asset=assetSource.Duplicate(name+" Appearance");
  mat.AppearanceAssetId=asset.Id;mat.Color=new Color(rgb[0],rgb[1],rgb[2]);
  using(var scope=new AppearanceAssetEditScope(d)){
   var edit=scope.Start(asset.Id);
   var p=edit.FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d??throw new InvalidOperationException("Missing diffuse");
   p.SetValueAsColor(mat.Color);scope.Commit(true);
  }
  d.Regenerate();var actual=((AssetPropertyDoubleArray4d)asset.GetRenderingAsset().FindByName(Generic.GenericDiffuse)).GetValueAsColor();
  if(actual.Red!=rgb[0]||actual.Green!=rgb[1]||actual.Blue!=rgb[2]||source.AppearanceAssetId!=assetSource.Id)throw new InvalidOperationException("Color/source readback failed");
  return new{id=mat.Id.Value,assetId=asset.Id.Value,sourceId=source.Id.Value,rgb=rgb.Select(x=>(int)x),note="Native generic render color; no bitmap textures or physical property calibration."};
 }
}