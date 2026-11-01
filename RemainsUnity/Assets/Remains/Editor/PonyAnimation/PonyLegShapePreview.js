// Browser adapter for PonyLegShapeModel. C# export fixtures verify parity.
function createLegShapeModel(rest,bank,math){
 const {add,sub,mul,dot,cross,length,matrix,reason,jointReason}=math;
 const hip=rest[0][0],scale=length(sub(rest[0][1],hip))+length(sub(rest[1][1],rest[1][0]));
 const side=p=>{const axis=sub(p[1],p[0]),offset=sub(p[2],p[0]);return [dot(offset,axis)/dot(axis,axis),cross(axis,offset)/length(axis)];};
 const driver=points=>({hip,knee:mul(add(points[0][1],points[1][0]),.5),foot:points[1][1]});
 function features(t){let u=sub(t.knee,t.hip),v=sub(t.foot,t.knee);const a=length(u),b=length(v);if(a<.025||b<.025)throw Error('Вырожденная конфигурация ноги.');const f=mul(sub(t.foot,t.hip),1/scale);u=mul(u,1/a);v=mul(v,1/b);return [...f,...u,dot(u,v),cross(u,v),Math.log(a/length(sub(rest[0][1],hip))),Math.log(b/length(sub(rest[1][1],rest[1][0])))];}
 const fromBasis=(p,axis)=>add(mul(axis,p[0]),mul([-axis[1],axis[0]],p[1]));
 const third=(a,b,along,across)=>{const axis=sub(b,a),l=length(axis);if(l<1e-5)throw Error('Результат схлопнулся.');return add(add(a,mul(axis,along)),mul([-axis[1],axis[0]],across/l));};
 function decode(s,t){const u=sub(t.knee,t.hip),v=sub(t.foot,t.knee),a=add(t.hip,[s[0],s[1]]),b=add(t.knee,fromBasis([s[2],s[3]],u)),c=add(t.knee,fromBasis([s[4],s[5]],v)),d=t.foot;return [[a,b,third(a,b,s[6],s[7]*side(rest[0])[1])],[c,d,third(c,d,s[8],s[9]*side(rest[1])[1])]];}
 function predict(target,filter='all',previousVariant=null){
  const f=features(target),kernel=d=>1/(d+.0004)**2;
  const nearest=bank.samples.map((s,index)=>({s,index,d:f.reduce((sum,v,k)=>sum+((v-s.features[k])*bank.weights[k])**2,0)})).filter(n=>!n.s.mirrored&&(filter==='all'||n.s.sources.some(s=>s.family===filter))).sort((a,b)=>a.d-b.d||a.index-b.index).slice(0,bank.neighbours);
  if(!nearest.length)throw Error('Нет совместимых примеров формы: '+filter);
  const scores=new Map();nearest.forEach(n=>scores.set(n.s.variant,(scores.get(n.s.variant)||0)+kernel(n.d)));
  const ranked=[...scores].sort((a,b)=>b[1]-a[1]||(a[0]<b[0]?-1:1));let variant=ranked[0][0];if(previousVariant&&scores.get(previousVariant)>=ranked[0][1]*.8)variant=previousVariant;
  let selected=nearest.filter(n=>n.s.variant===variant);if(selected[0].d<1e-10)selected=selected.slice(0,1);
  const sum=selected.reduce((s,n)=>s+kernel(n.d),0),weights=selected.map(n=>kernel(n.d)/sum),shape=Array(10).fill(0);selected.forEach((n,i)=>shape.forEach((_,k)=>shape[k]+=n.s.shape[k]*weights[i]));
  const ambiguous=nearest.filter(n=>n.s.variant!==variant&&n.d<=nearest[0].d+.0009).map(n=>n.s.id);
  return {variant,points:decode(shape,target),indices:selected.map(n=>n.index),sources:selected.map(n=>({id:n.s.id,provenance:n.s.sources})),weights,distance:Math.sqrt(nearest[0].d),ambiguous};
 }
 function evaluate(base,body,limits,strength=1,filter='all',previousVariant=null){
  if(!Number.isFinite(strength)||strength<0||strength>1)throw Error('Неверная сила формы.');
  const local=base.map(p=>p.map(v=>sub(v,body))),prediction=predict(driver(local),filter,previousVariant);let problem=null;
  for(let step=0;step<=12;step++){
   const usedStrength=step===12?0:strength*2**-step,points=local.map((p,n)=>p.map((v,i)=>add(add(v,mul(sub(prediction.points[n][i],v),usedStrength)),body)));
   const failed=points.map((p,n)=>reason(matrix(rest[n],p))).find(Boolean)||jointReason(points,limits);
   if(!failed)return {points,prediction,usedStrength,limitedReason:usedStrength<strength?problem:null,variant:usedStrength>0?prediction.variant:bank.defaultVariant};problem=failed;
  }
  throw Error('Даже базовая поза не проходит ограничения: '+problem);
 }
 return {evaluate,predict,driver};
}
