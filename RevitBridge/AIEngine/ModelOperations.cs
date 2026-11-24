using System.Diagnostics;
using System.Text.Json;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.DB.Plumbing;
using Autodesk.Revit.DB.Mechanical;
using Autodesk.Revit.DB.Electrical;

namespace Kalidis.AIEngine;
internal static class ModelOperations
{
    private static double Mm(double x) => UnitUtils.ConvertToInternalUnits(x, UnitTypeId.Millimeters);
    private static double N(JsonElement r,string k) {
        double n=r.GetProperty(k).GetDouble();
        if(!double.IsFinite(n)||Math.Abs(n)>10000000) throw new ArgumentException("Invalid dimension: "+k);
        return n;
    }
    private static string S(JsonElement r,string k)=>r.GetProperty(k).GetString() ?? throw new ArgumentException(k);
    private static ElementId Id(JsonElement r,string k)=>new(r.GetProperty(k).GetInt64());
    private static T Get<T>(Document d,JsonElement r,string k) where T:Element =>
        d.GetElement(Id(r,k)) as T ?? throw new ArgumentException("Invalid "+k);
    private static XYZ P(JsonElement r,double z=0) => new(Mm(N(r,"x")),Mm(N(r,"y")),z);
    private static XYZ P3(JsonElement r) => new(Mm(N(r,"x")),Mm(N(r,"y")),Mm(N(r,"z")));
    private static CurveLoop Profile(JsonElement r,double z)
    {
        var p=r.GetProperty("points").EnumerateArray().Select(x=>P(x,z)).ToArray();
        if(p.Length<3 || p.Length>200) throw new ArgumentException("Profile needs 3 to 200 vertices");
        var loop=new CurveLoop();
        for(int i=0;i<p.Length;i++) loop.Append(Line.CreateBound(p[i],p[(i+1)%p.Length]));
        return loop;
    }
    public static object Batch(Document d,JsonElement r)
    {
        var timer=Stopwatch.StartNew();
        var steps=r.GetProperty("steps").EnumerateArray().ToArray();
        if(steps.Length<1 || steps.Length>200) throw new ArgumentException("Batch limit 1-200");
        bool commit=r.TryGetProperty("commit",out var flag)&&flag.ValueKind==JsonValueKind.True;
        var ids=new List<long>();
        using var tx=new Transaction(d,"Kalidis AI Engine - batch");
        if(tx.Start()!=TransactionStatus.Started) throw new InvalidOperationException("Transaction not started");
        try {
            foreach(var step in steps) ids.Add(Execute(d,step).Value);
            d.Regenerate();
            var status=commit?tx.Commit():tx.RollBack();
            bool ok=commit?status==TransactionStatus.Committed:status==TransactionStatus.RolledBack;
            return new {ok,committed=ok&&commit,dryRun=!commit,status=status.ToString(),elementIds=ids,verified=ids.All(i=>(d.GetElement(new ElementId(i))!=null)==commit),elapsedMs=timer.ElapsedMilliseconds};
        }
        catch { if(tx.GetStatus()==TransactionStatus.Started)tx.RollBack();throw; }
    }
    private static ElementId Execute(Document d,JsonElement r)
    {
        switch(S(r,"action"))
        {
            case "level": { var e=Level.Create(d,Mm(N(r,"elevationMm")));e.Name=S(r,"name");return e.Id; }
            case "wall": {
                var l=Get<Level>(d,r,"levelId");var t=Get<WallType>(d,r,"typeId");
                if(t.Kind!=WallKind.Basic)throw new ArgumentException("Basic wall type required");
                double h=N(r,"heightMm");if(h<=0||h>30000)throw new ArgumentException("Invalid wall height");
                return Wall.Create(d,Line.CreateBound(P(r.GetProperty("start"),l.Elevation),P(r.GetProperty("end"),l.Elevation)),t.Id,l.Id,Mm(h),0,false,false).Id;
            }
            case "floor": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FloorType>(d,r,"typeId");
                return Floor.Create(d,new List<CurveLoop>{Profile(r,l.Elevation)},t.Id,l.Id).Id;
            }
            case "ceiling": {
                var l=Get<Level>(d,r,"levelId");var t=Get<CeilingType>(d,r,"typeId");
                return Ceiling.Create(d,new List<CurveLoop>{Profile(r,0)},t.Id,l.Id).Id;
            }
            case "room": {
                var l=Get<Level>(d,r,"levelId");var p=P(r);
                var room=d.Create.NewRoom(l,new UV(p.X,p.Y));if(room==null)throw new InvalidOperationException("Room failed");
                room.Name=S(r,"name");return room.Id;
            }
            case "grid": {var e=Grid.Create(d,Line.CreateBound(P(r.GetProperty("start")),P(r.GetProperty("end"))));e.Name=S(r,"name");return e.Id;}
            case "material": return Material.Create(d,S(r,"name"));
            case "view3d": {
                var t=Get<ViewFamilyType>(d,r,"typeId");var v=View3D.CreateIsometric(d,t.Id);v.Name=S(r,"name");return v.Id;
            }
            case "plan": {
                var t=Get<ViewFamilyType>(d,r,"typeId");var l=Get<Level>(d,r,"levelId");var v=ViewPlan.Create(d,t.Id,l.Id);v.Name=S(r,"name");return v.Id;
            }
            case "beam": {
                var l=Get<Level>(d,r,"levelId"); var t=Get<FamilySymbol>(d,r,"typeId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(Line.CreateBound(P3(r.GetProperty("start")),P3(r.GetProperty("end"))),t,l,StructuralType.Beam).Id;
            }
            case "column":
            case "component": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FamilySymbol>(d,r,"typeId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(P3(r.GetProperty("point")),t,l,S(r,"action")=="column"?StructuralType.Column:StructuralType.NonStructural).Id;
            }
            case "hosted_component": {
                var l=Get<Level>(d,r,"levelId");var t=Get<FamilySymbol>(d,r,"typeId");var host=Get<Wall>(d,r,"hostId");
                if(!t.IsActive){t.Activate();d.Regenerate();}
                return d.Create.NewFamilyInstance(P3(r.GetProperty("point")),t,host,l,StructuralType.NonStructural).Id;
            }
            case "pipe": return Pipe.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            case "duct": return Duct.Create(d,Id(r,"systemTypeId"),Id(r,"typeId"),Id(r,"levelId"),P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            case "conduit": return Conduit.Create(d,Id(r,"typeId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")),Id(r,"levelId")).Id;
            case "cable_tray": return CableTray.Create(d,Id(r,"typeId"),P3(r.GetProperty("start")),P3(r.GetProperty("end")),Id(r,"levelId")).Id;
            case "opening": {
                var wall=Get<Wall>(d,r,"hostId");return d.Create.NewOpening(wall,P3(r.GetProperty("start")),P3(r.GetProperty("end"))).Id;
            }
            case "text": {
                var v=Get<View>(d,r,"viewId");return TextNote.Create(d,v.Id,P3(r.GetProperty("point")),S(r,"text"),Id(r,"typeId")).Id;
            }
            case "detail_line": {
                var v=Get<View>(d,r,"viewId");return d.Create.NewDetailCurve(v,Line.CreateBound(P3(r.GetProperty("start")),P3(r.GetProperty("end")))).Id;
            }
            case "viewport": return Viewport.Create(d,Id(r,"sheetId"),Id(r,"viewId"),P3(r.GetProperty("point"))).Id;
            default:throw new ArgumentException("Unsupported batch action: "+S(r,"action"));
        }
    }
    public static object ExportFamily(Document d,JsonElement r,string sandbox)
    {
        var f=Get<Family>(d,r,"familyId");
        if(!f.IsEditable||f.IsInPlace)throw new InvalidOperationException("Family not editable");
        string file=S(r,"fileName");
        if(file!=Path.GetFileName(file)||!file.EndsWith(".rfa",StringComparison.OrdinalIgnoreCase)||file.IndexOfAny(Path.GetInvalidFileNameChars())>=0)throw new ArgumentException("Use simple RFA filename");
        string output=Path.Combine(sandbox,file);
        if(File.Exists(output))throw new IOException("Refusing to overwrite RFA");
        var fd=d.EditFamily(f);
        try {fd.SaveAs(output,new SaveAsOptions{OverwriteExistingFile=false});return new {ok=true,path=output};}
        finally {fd.Close(false);}
    }
}
