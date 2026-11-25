# -*- coding: utf-8 -*-
"""Read only metadata of four already reviewed, pinned external RFA files."""
from pyrevit.api import DB
import hashlib
from System.IO import File, FileInfo, FileAttributes

import os
root=os.path.join(os.environ["LOCALAPPDATA"],"KalidisAIEngine","Sandbox")
if doc is None or os.path.normcase(os.path.dirname(doc.PathName))!=os.path.normcase(root) or doc.IsModified or doc.IsFamilyDocument or doc.IsWorkshared or doc.IsReadOnly or doc.IsModelInCloud:
    raise ValueError("Saved fixture required")
pins=[
("OnlineQuarantine-769efd9b62a64ddbbf415401c4ebba50","Column_Sqaure.rfa",360448,"0660610efb4b6c8dd8717be4c4c1b6f74f044643c7d427590e6c122a02720bf9"),
("OnlineQuarantine-ae52ee92989546ec99d536471431daef","I_Single-Flush.rfa",307200,"66ae731f85416ba0544038d9e442aaf399ff99ae8e1f4388be980b6636549d1f"),
("OnlineQuarantine-c3d2971b64fc48fdb44e627105c30437","Mullion_Rectangle.rfa",512000,"745e6c9bc4e39eba1a109f50641d46dd78bfde165684af464b1e24e6b34338ef"),
("OnlineQuarantine-eb1339adc0a1490b99f6e0ebd315c351","Column_Square_Tollerance.rfa",372736,"7fc2c31851d25c6611418cbb50c3e6a0cfaecdf345f012ecde7058fe596d512f")]
def digest(p):
    with open(p, 'rb') as stream:
        return hashlib.sha256(stream.read()).hexdigest()
rows=[]
for folder,name,size,pin in pins:
    directory=os.path.join(root,folder)
    p=os.path.join(directory,name)
    if File.GetAttributes(directory)&FileAttributes.ReparsePoint or File.GetAttributes(p)&FileAttributes.ReparsePoint or FileInfo(p).Length!=size or digest(p)!=pin:
        raise ValueError("Pinned path or hash mismatch")
    row={"fileName":name,"sha256":pin}
    try:
        info=DB.BasicFileInfo.Extract(p)
        try:
            row.update({"format":str(info.Format),"current":bool(info.IsSavedInCurrentVersion),"later":bool(info.IsSavedInLaterVersion),"workshared":bool(info.IsWorkshared)})
        finally:
            info.Dispose()
    except Exception as ex:
        row["metadataError"]=str(ex)
    try:
        t=DB.TransmissionData.ReadTransmissionData(DB.ModelPathUtils.ConvertUserVisiblePathToModelPath(p))
        if t is None:
            row["transmissionState"]="not_present"
            row["externalReferenceIds"]=None
        else:
            row["transmissionState"]="read"
            refs=[]
            for i in t.GetAllExternalFileReferenceIds():
                r=t.GetLastSavedReferenceData(i)
                refs.append({"id":i.Value,"type":str(r.ExternalFileReferenceType),"status":str(r.GetLinkedFileStatus())})
            row["externalReferences"]=refs
            t.Dispose()
    except Exception as ex:
        row["transmissionState"]="unavailable"
        row["transmissionError"]=str(ex)
    if digest(p)!=pin:
        raise ValueError("Source changed")
    rows.append(row)
result={"path":doc.PathName,"revitVersion":str(app.VersionNumber),"rows":rows,"openedDocuments":0}
