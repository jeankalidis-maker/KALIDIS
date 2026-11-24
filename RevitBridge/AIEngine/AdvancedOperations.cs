using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.UI;
namespace Kalidis.AIEngine;
internal static class AdvancedOperations
{
    internal static readonly string[] Actions={"types","native_status","library","create_family","create_family_spec","load_family","family_info","export_view","save_test","sheet","schedule"};
    private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString()??throw new ArgumentException(k);
    private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
    private static double Mm(double n)=>UnitUtils.ConvertToInternalUnits(n,UnitTypeId.Millimeters);
    private static string FileIn(string root,string name,string ext)
    {
        if(name!=Path.GetFileName(name)||name.IndexOfAny(Path.GetInvalidFileNameChars())>=0||!name.EndsWith(ext,StringComparison.OrdinalIgnoreCase))throw new ArgumentException("Invalid file name");
        return Path.Combine(root,name);
    }
    private static string[] Roots()=>new[]{Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Autodesk","RVT 2025","Libraries"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),"Autodesk","RVT 2025","Family Templates")};
    internal static object Read(UIApplication app,Document d,JsonElement r,string action)
    {
        if(action=="native_status"){
            var names=Enum.GetValues<PostableCommand>(); return new {ok=true,commands=names.Select(x=>{bool available=false;try{available=app.CanPostCommand(RevitCommandId.LookupPostableCommandId(x));}catch{}return new{name=x.ToString(),available};}).ToArray()};
        }
        if(action=="types")return new {ok=true,types=new FilteredElementCollector(d).WhereElementIsElementType().Cast<ElementType>().Take(10000).Select(x=>new{id=x.Id.Value,name=x.Name,category=x.Category?.Name,kind=x.GetType().Name}).ToArray()};
        if(action=="family_info"){
            var f=d.GetElement(Id(r,"familyId")) as Family??throw new ArgumentException("Invalid family");
            return new{ok=true,name=f.Name,editable=f.IsEditable,inPlace=f.IsInPlace,types=f.GetFamilySymbolIds().Select(i=>new{id=i.Value,name=d.GetElement(i)?.Name}).ToArray()};
        }
        if(action=="library"){
            string q=S(r,"query");var files=Roots().Where(Directory.Exists).SelectMany(p=>Directory.EnumerateFiles(p,"*.rf?",new EnumerationOptions{RecurseSubdirectories=true,IgnoreInaccessible=true})).Where(p=>Path.GetFileName(p).Contains(q,StringComparison.OrdinalIgnoreCase)).Take(200).ToArray();
            return new{ok=true,files};
        }
        throw new ArgumentException(action);
    }
    internal static object Write(UIApplication app,Document d,JsonElement r,string action,string root)
    {
        if(action=="create_family_spec"){
            string template=Path.GetFullPath(S(r,"template"));
            if(!Roots().Skip(1).Any(p=>template.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!template.EndsWith(".rft",StringComparison.OrdinalIgnoreCase)||!File.Exists(template))throw new ArgumentException("Use installed RFT template");
            string output=FileIn(root,S(r,"fileName"),".rfa");if(File.Exists(output))throw new IOException("No overwrite");
            var shapes=r.GetProperty("shapes").EnumerateArray().ToArray();
            if(shapes.Length<1||shapes.Length>100)throw new ArgumentException("1-100 extrusions");
            double dimension(JsonElement o,string k,double min,double max){double v=o.GetProperty(k).GetDouble();if(!double.IsFinite(v)||v<min||v>max)throw new ArgumentException("Invalid "+k);return Mm(v);}
            var fd=app.Application.NewFamilyDocument(template);
            try{
                using(var tx=new Transaction(fd,"Kalidis - family specification")){
                    if(tx.Start()!=TransactionStatus.Started)throw new InvalidOperationException("Transaction failed");
                    var fm=fd.FamilyManager;if(fm.CurrentType==null)fm.NewType("Default");
                    var parameters=new Dictionary<string,FamilyParameter>(StringComparer.OrdinalIgnoreCase);
                    foreach(var shape in shapes){
                        var z=dimension(shape,"zMm",-30000,30000);
                        var h=dimension(shape,"heightMm",1,30000);
                        var points=shape.GetProperty("points").EnumerateArray().Select(p=>new XYZ(dimension(p,"x",-30000,30000),dimension(p,"y",-30000,30000),z)).ToArray();
                        if(points.Length<3||points.Length>100)throw new ArgumentException("3-100 vertices");
                        var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,new XYZ(0,0,z)));
                        var ca=new CurveArray();for(int i=0;i<points.Length;i++)ca.Append(Line.CreateBound(points[i],points[(i+1)%points.Length]));
                        var profile=new CurveArrArray();profile.Append(ca);
                        bool solid=!shape.TryGetProperty("void",out var hole)||hole.ValueKind!=JsonValueKind.True;
                        var extrusion=fd.FamilyCreate.NewExtrusion(solid,profile,plane,h);
                        if(shape.TryGetProperty("heightParameter",out var label)){
                            string name=label.GetString()??throw new ArgumentException("Invalid parameter");
                            if(name.Length<1||name.Length>80)throw new ArgumentException("Invalid parameter length");
                            if(!parameters.TryGetValue(name,out var fp)){
                                fp=fm.AddParameter(name,GroupTypeId.Geometry,SpecTypeId.Length,false);
                                parameters.Add(name,fp);fm.Set(fp,h);
                            }else if(Math.Abs(fm.CurrentType.AsDouble(fp).GetValueOrDefault()-h)>1e-8)throw new ArgumentException("Shared height parameters require identical initial heights");
                            var ep=extrusion.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                            if(ep==null||!fm.CanElementParameterBeAssociated(ep))throw new InvalidOperationException("Cannot associate height");
                            fm.AssociateElementParameterToFamilyParameter(ep,fp);
                        }
                    }
                    if(r.TryGetProperty("formulas",out var formulas))foreach(var f in formulas.EnumerateObject()){
                        if(!parameters.TryGetValue(f.Name,out var fp))throw new ArgumentException("Unknown formula parameter");
                        fm.SetFormula(fp,f.Value.GetString());
                    }
                    fd.Regenerate();
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Family transaction failed");
                }
                fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});
                return new{ok=true,path=output,extrusionCount=shapes.Length};
            }finally{fd.Close(false);}
        }
        if(action=="create_family"){
            string template=Path.GetFullPath(S(r,"template"));
            if(!Roots().Skip(1).Any(p=>template.StartsWith(p+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!template.EndsWith(".rft",StringComparison.OrdinalIgnoreCase)||!File.Exists(template))throw new ArgumentException("Use installed RFT template");
            string output=FileIn(root,S(r,"fileName"),".rfa");if(File.Exists(output))throw new IOException("No overwrite");
            double w=r.GetProperty("widthMm").GetDouble(),l=r.GetProperty("lengthMm").GetDouble(),h=r.GetProperty("heightMm").GetDouble();
            if(new[]{w,l,h}.Any(n=>!double.IsFinite(n)||n<10||n>30000))throw new ArgumentException("Invalid dimensions");
            var fd=app.Application.NewFamilyDocument(template);
            try{
                using(var tx=new Transaction(fd,"Kalidis - family solid")){
                    tx.Start();
                    var plane=SketchPlane.Create(fd,Plane.CreateByNormalAndOrigin(XYZ.BasisZ,XYZ.Zero));
                    var points=new[]{XYZ.Zero,new XYZ(Mm(w),0,0),new XYZ(Mm(w),Mm(l),0),new XYZ(0,Mm(l),0)};
                    var ca=new CurveArray();for(int i=0;i<4;i++)ca.Append(Line.CreateBound(points[i],points[(i+1)%4]));
                    var profile=new CurveArrArray();profile.Append(ca);
                    var solid=fd.FamilyCreate.NewExtrusion(true,profile,plane,Mm(h));
                    var fm=fd.FamilyManager;if(fm.CurrentType==null)fm.NewType("Default");
                    var height=fm.AddParameter("Kalidis_Height",GroupTypeId.Geometry,SpecTypeId.Length,false);
                    var ep=solid.get_Parameter(BuiltInParameter.EXTRUSION_END_PARAM);
                    if(ep==null||!fm.CanElementParameterBeAssociated(ep))throw new InvalidOperationException("Height association unsupported");
                    fm.AssociateElementParameterToFamilyParameter(ep,height);fm.Set(height,Mm(h));
                    if(tx.Commit()!=TransactionStatus.Committed)throw new InvalidOperationException("Family transaction failed");
                }
                fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});return new{ok=true,path=output,parametric="height",widthMm=w,lengthMm=l,heightMm=h};
            }finally{fd.Close(false);}
        }
        if(action=="export_view"){
            string output=FileIn(root,S(r,"fileName"),".png");if(File.Exists(output))throw new IOException("No overwrite");
            var opts=new ImageExportOptions{ExportRange=ExportRange.CurrentView,FilePath=output,HLRandWFViewsFileType=ImageFileType.PNG,ShadowViewsFileType=ImageFileType.PNG,ZoomType=ZoomFitType.FitToPage,PixelSize=1600};
            d.ExportImage(opts);return new{ok=File.Exists(output),path=output};
        }
        if(action=="save_test"){d.Save();return new{ok=true,path=d.PathName};}
        using var t=new Transaction(d,"Kalidis - "+action);t.Start();
        try{
            ElementId id;
            if(action=="load_family"){
                string p=Path.GetFullPath(S(r,"path"));
                if(!Roots().Take(1).Append(root.TrimEnd(Path.DirectorySeparatorChar)).Any(x=>p.StartsWith(x+Path.DirectorySeparatorChar,StringComparison.OrdinalIgnoreCase))||!p.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||!File.Exists(p))throw new ArgumentException("Family must be staged locally");
                if(!d.LoadFamily(p,out Family f)||f==null)throw new InvalidOperationException("Load failed or already loaded");id=f.Id;
            }else if(action=="sheet"){
                var v=ViewSheet.Create(d,Id(r,"titleBlockId"));v.SheetNumber=S(r,"number");v.Name=S(r,"name");id=v.Id;
            }else if(action=="schedule"){
                var v=ViewSchedule.CreateSchedule(d,Id(r,"categoryId"));v.Name=S(r,"name");id=v.Id;
            }else throw new ArgumentException(action);
            bool commit=r.TryGetProperty("commit",out var c)&&c.ValueKind==JsonValueKind.True;
            var state=commit?t.Commit():t.RollBack();
            return new{ok=state==(commit?TransactionStatus.Committed:TransactionStatus.RolledBack),status=state.ToString(),id=id.Value,dryRun=!commit};
        }catch{if(t.GetStatus()==TransactionStatus.Started)t.RollBack();throw;}
    }
}
