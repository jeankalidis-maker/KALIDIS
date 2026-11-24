// Conservative XY polygon validation for straight horizontal Revit profiles.
export function validateClosedProfile(points,tol=0.01){
 if(points.length<3)throw Error('Closed profile needs three points');
 const n=points.length,dist=(a,b)=>Math.hypot(a.x-b.x,a.y-b.y);
 const cross=(a,b,c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
 const sign=(a,b,c)=>{const v=cross(a,b,c),e=tol*dist(a,b);return v>e?1:v< -e?-1:0};
 const on=(a,b,c)=>sign(a,b,c)===0&&c.x>=Math.min(a.x,b.x)-tol&&c.x<=Math.max(a.x,b.x)+tol&&c.y>=Math.min(a.y,b.y)-tol&&c.y<=Math.max(a.y,b.y)+tol;
 const hit=(a,b,c,d)=>{const s=sign(a,b,c),t=sign(a,b,d),u=sign(c,d,a),v=sign(c,d,b);return s*t<0&&u*v<0||on(a,b,c)||on(a,b,d)||on(c,d,a)||on(c,d,b)};
 for(let i=0;i<n;i++)for(let j=i+1;j<n;j++)if(dist(points[i],points[j])<=tol)throw Error('Repeated profile vertex');
 let area2=0;
 for(let i=0;i<n;i++){
  const a=points[i],b=points[(i+1)%n],c=points[(i+2)%n];
  area2+=cross(points[0],a,b);
  if(sign(a,b,c)===0&&((b.x-a.x)*(c.x-b.x)+(b.y-a.y)*(c.y-b.y))<0)throw Error('Backtracking profile edge');
  for(let j=i+2;j<n;j++){
   if(i===0&&j===n-1)continue;
   if(hit(a,b,points[j],points[(j+1)%n]))throw Error('Self-intersecting or touching profile');
  }
 }
 if(Math.abs(area2)/2<=tol*tol)throw Error('Degenerate profile area');
 return Math.abs(area2)/2;
}
