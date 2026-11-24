import fs from 'node:fs';
import assert from 'node:assert/strict';
import {createCanvas} from '@napi-rs/canvas';
import {getDocument,OPS} from 'pdfjs-dist/legacy/build/pdf.mjs';
const file=process.argv[2];const data=new Uint8Array(fs.readFileSync(file));const fileBytes=data.length;const pdf=await getDocument({data,useSystemFonts:true,isEvalSupported:false,disableFontFace:true}).promise;
try{
 const results=[];
 for(let i=1;i<=pdf.numPages;i++){
  const page=await pdf.getPage(i),v=page.getViewport({scale:1}),ops=await page.getOperatorList(),txt=await page.getTextContent();
  const names=Object.fromEntries(Object.entries(OPS).map(([k,v])=>[v,k])),counts={};
  for(const k of ops.fnArray){const name=names[k]??String(k);counts[name]=(counts[name]??0)+1;}
  assert(v.width>0&&v.height>0&&ops.fnArray.length>20);
  const canvas=createCanvas(Math.ceil(v.width),Math.ceil(v.height));
  await page.render({canvas,canvasContext:canvas.getContext('2d'),viewport:v}).promise;
  fs.writeFileSync('AuditPage-'+i+'.png',canvas.toBuffer('image/png'));
  results.push({page:i,widthPt:v.width,heightPt:v.height,operators:ops.fnArray.length,counts,text:txt.items.map(x=>x.str).join(' ').slice(0,1000)});
 }
 console.log(JSON.stringify({pages:pdf.numPages,bytes:fileBytes,results},null,2));
}finally{await pdf.destroy();}
