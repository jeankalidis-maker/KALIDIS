// Bounded local Windows WIC JPEG -> PNG helper. Input bytes only; no file paths supplied to decoder.
import {spawn} from 'node:child_process';import {lstat,readFile,realpath} from 'node:fs/promises';import {createHash} from 'node:crypto';import path from 'node:path';import {fileURLToPath} from 'node:url';
const folder=path.resolve(path.dirname(fileURLToPath(import.meta.url)),'../TextureCodec/bin/Release/net8.0-windows');
const pins={'Kalidis.TextureCodec.dll':'33333de259fbed9611bb07662a7d1ae159458c7d5ed9f48e36f74eedb9e096b1','Kalidis.TextureCodec.deps.json':'0aaa9adcd582cda940141c1b5afcf7bdce7f4c15fd8fe474318e8e6a63b2ec8c','Kalidis.TextureCodec.runtimeconfig.json':'c71152bf25e40d647b2440c5b39be157a3d356106be9d5b678ab97bb87b4e782'};
async function verified(){
 if(process.platform!=='win32'||!process.env.ProgramFiles)throw Error('Windows texture decoder unavailable');
 if((await realpath(folder)).toLowerCase()!==folder.toLowerCase())throw Error('Decoder path alias');
 for(const [name,sha] of Object.entries(pins)){
  const f=path.join(folder,name),s=await lstat(f);if(!s.isFile()||s.isSymbolicLink()||s.size>1000000||createHash('sha256').update(await readFile(f)).digest('hex')!==sha)throw Error('Texture decoder build mismatch');
 }
 const dotnet=path.join(process.env.ProgramFiles,'dotnet','dotnet.exe'),s=await lstat(dotnet);
 if(!s.isFile()||s.isSymbolicLink())throw Error('Local dotnet unavailable');
 return dotnet;
}
export async function jpegCodecAvailable(){try{await verified();return true}catch{return false}}
export async function jpegToPng(bytes){
 if(!Buffer.isBuffer(bytes)||bytes.length<16||bytes.length>10000000||bytes[0]!==255||bytes[1]!==216)throw Error('Bounded JPEG required');
 const exe=await verified();
 return await new Promise((resolve,reject)=>{
  const p=spawn(exe,[path.join(folder,'Kalidis.TextureCodec.dll')],{stdio:['pipe','pipe','pipe'],windowsHide:true});
  const chunks=[];let count=0,err='',settled=false;
  const done=(e,v)=>{if(settled)return;settled=true;clearTimeout(timer);e?reject(e):resolve(v)};
  const timer=setTimeout(()=>{p.kill();done(Error('Texture decoder timeout'))},20000);
  p.on('error',e=>done(e));p.stdin.on('error',e=>{p.kill();done(e)});
  p.stdout.on('data',b=>{count+=b.length;if(count>10000000){p.kill();done(Error('Texture decoder output limit'))}else chunks.push(b)});
  p.stderr.on('data',b=>{if(err.length<1000)err+=b.toString().slice(0,1000-err.length)});
  p.on('close',code=>code===0&&count>32?done(null,Buffer.concat(chunks)):done(Error('Texture decode failed: '+err)));
  p.stdin.end(bytes);
 });
}