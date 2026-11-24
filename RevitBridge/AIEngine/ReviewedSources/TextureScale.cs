using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Visual;
using System.Text.Json;
using Kalidis.AIEngine;
namespace Kalidis.TextureScale;
public sealed class TextureScale:IReviewedOperation {
 public string Name=>"texture_scale";
 public object Execute(Document d,JsonElement a){
  var mat=d.GetElement(new ElementId(a.GetProperty("id").GetInt64())) as Material??throw new ArgumentException("Material required");
  var old=d.GetElement(mat.AppearanceAssetId) as AppearanceAssetElement??throw new ArgumentException("Appearance required");
  Asset Bitmap(Asset e)=>(e.FindByName(Generic.GenericDiffuse) as AssetPropertyDoubleArray4d)?.GetSingleConnectedAsset()??throw new ArgumentException("Generic bitmap required");
  AssetPropertyDistance Dist(Asset b,string k)=>b.FindByName(k) as AssetPropertyDistance??throw new ArgumentException(k);
  double Read(Asset b,string k){var p=Dist(b,k);return UnitUtils.Convert(p.Value,p.GetUnitTypeId(),UnitTypeId.Millimeters);}
  double[] Sizes(Asset b)=>new[]{Read(b,UnifiedBitmap.TextureRealWorldScaleX),Read(b,UnifiedBitmap.TextureRealWorldScaleY)};
  var before=Sizes(Bitmap(old.GetRenderingAsset()));
  if(a.GetProperty("mode").GetString()=="audit")return new{id=mat.Id.Value,sizeMm=before,assetId=old.Id.Value};
  if(a.GetProperty("mode").GetString()!="duplicate")throw new ArgumentException("audit or duplicate");
  var name=a.GetProperty("name").GetString()??throw new ArgumentException("Name");
  double x=a.GetProperty("sizeXmm").GetDouble(),y=a.GetProperty("sizeYmm").GetDouble();
  if(!double.IsFinite(x)||!double.IsFinite(y)||x<10||y<10||x>10000||y>10000)throw new ArgumentException("Size range");
  var copy=mat.Duplicate(name);var asset=old.Duplicate(name+" Appearance");copy.AppearanceAssetId=asset.Id;
  using(var scope=new AppearanceAssetEditScope(d)){
   var b=Bitmap(scope.Start(asset.Id));
   if(b.FindByName(UnifiedBitmap.TextureScaleLock) is AssetPropertyBoolean l)l.Value=false;
   void Set(string k,double mm){var p=Dist(b,k);var v=UnitUtils.Convert(mm,UnitTypeId.Millimeters,p.GetUnitTypeId());if(!p.IsValidValue(v))throw new ArgumentException(k);p.Value=v;}
   Set(UnifiedBitmap.TextureRealWorldScaleX,x);Set(UnifiedBitmap.TextureRealWorldScaleY,y);scope.Commit(true);
  }
  d.Regenerate();var after=Sizes(Bitmap(asset.GetRenderingAsset()));var original=Sizes(Bitmap(old.GetRenderingAsset()));
  if(Math.Abs(after[0]-x)>.1||Math.Abs(after[1]-y)>.1||!original.SequenceEqual(before)||mat.AppearanceAssetId!=old.Id)throw new InvalidOperationException("Scale or source verification failed");
  return new{id=copy.Id.Value,assetId=asset.Id.Value,sizeMm=after,sourceSizeMm=original,sourcePreserved=true,note="Physical bitmap scale metadata, not rendered appearance certification"};
 }
}