// Rasterizes C# RoomRenderPlan commands. No room parsing, validation, neighbour
// selection, geometry or lighting algorithm is implemented in JavaScript.
window.remainsRoomRenderer = (() => {
  const assets=new Map();
  let dirty,queued=false,useFrame=0;
  const stats={backend:'',draws:0,missing:0,error:null};
  function notify(){if(!queued){queued=true;requestAnimationFrame(()=>{queued=false;dirty?.();});}}
  function image(url){
    if(!url)return null;
    if(!assets.has(url)){const i=new Image();i.onload=notify;i.onerror=notify;i.src=url;assets.set(url,i);}
    const i=assets.get(url);i.used=useFrame;return i.complete&&i.naturalWidth?i:null;
  }
  const linear=v=>v<=.04045?v/12.92:((v+.055)/1.055)**2.4;
  const srgb=v=>v<=.0031308?12.92*v:1.055*Math.max(0,v)**(1/2.4)-.055;
  const decode=Float32Array.from({length:256},(_,i)=>linear(i/255));
  function bounds(c){const a=c.x-c.originX*c.scaleX,b=c.y-c.originY*c.scaleY,d=a+c.width*c.scaleX,e=b+c.height*c.scaleY;return {x:Math.min(a,d),y:Math.min(b,e),w:Math.abs(d-a),h:Math.abs(e-b)};}

  class Gpu {
    constructor(){
      this.canvas=document.createElement('canvas');
      this.gl=this.canvas.getContext('webgl2',{alpha:false,antialias:false,premultipliedAlpha:false,preserveDrawingBuffer:true});
      if(!this.gl)throw new Error('WebGL2 unavailable');this.lost=false;this.canvas.addEventListener('webglcontextlost',e=>{e.preventDefault();this.lost=true;notify();});this.canvas.addEventListener('webglcontextrestored',()=>{gpu=null;notify();});
      const g=this.gl;this.textures=new Map();
      const vertex=`#version 300 es
      in vec2 point;uniform vec2 resolution,viewOrigin;uniform float pixelsPerMeter;
      uniform vec2 registration,size,pivot,scale;out vec2 uv,world,screen;
      void main(){uv=point;world=registration+(point*size-pivot)*scale;screen=(world-viewOrigin)*pixelsPerMeter;
      gl_Position=vec4(screen/resolution*vec2(2.,-2.)+vec2(-1.,1.),0.,1.);}`;
      const fragment=`#version 300 es
      precision highp float;in vec2 uv,world,screen;out vec4 outputColor;
      uniform sampler2D shape,material,eraseMap;uniform vec2 materialSize,resolution;
      uniform float density,opacity;uniform int kind,blendMode,affected;
      void main(){vec4 c=kind==0?texture(shape,uv):texture(material,world*density/materialSize);
      if(kind==1)c.a*=texture(shape,uv).a;
      if(kind==3){float a=texture(shape,uv).a;outputColor=vec4(a);return;}
      c.a*=opacity;if(affected==1)c.a*=1.-texture(eraseMap,vec2(screen.x/resolution.x,1.-screen.y/resolution.y)).r;
      if(blendMode==1)c.rgb=mix(vec3(1.),c.rgb,c.a);else if(blendMode==2)c.rgb=mix(vec3(.5),c.rgb,c.a);
      outputColor=c;}`;
      this.program=this.programOf(vertex,fragment);
      const outputVertex=`#version 300 es
      in vec2 point;out vec2 uv;void main(){uv=point;gl_Position=vec4(point*2.-1.,0.,1.);}`;
      const outputFragment=`#version 300 es
      precision highp float;in vec2 uv;uniform sampler2D scene;out vec4 outputColor;
      void main(){vec3 c=texture(scene,uv).rgb;vec3 low=c*12.92,high=1.055*pow(max(c,vec3(0.)),vec3(1./2.4))-.055;
      outputColor=vec4(mix(high,low,lessThanEqual(c,vec3(.0031308))),1.);}`;
      this.output=this.programOf(outputVertex,outputFragment);
      this.vertices=g.createBuffer();g.bindBuffer(g.ARRAY_BUFFER,this.vertices);g.bufferData(g.ARRAY_BUFFER,new Float32Array([0,0,1,0,0,1,0,1,1,0,1,1]),g.STATIC_DRAW);
      this.white=g.createTexture();g.bindTexture(g.TEXTURE_2D,this.white);g.texImage2D(g.TEXTURE_2D,0,g.RGBA,1,1,0,g.RGBA,g.UNSIGNED_BYTE,new Uint8Array([255,255,255,255]));g.texParameteri(g.TEXTURE_2D,g.TEXTURE_MIN_FILTER,g.NEAREST);
      this.target=null;
    }
    programOf(vertex,fragment){const g=this.gl;const compile=(type,source)=>{const s=g.createShader(type);g.shaderSource(s,source);g.compileShader(s);if(!g.getShaderParameter(s,g.COMPILE_STATUS))throw new Error(g.getShaderInfoLog(s));return s;};const p=g.createProgram(),v=compile(g.VERTEX_SHADER,vertex),f=compile(g.FRAGMENT_SHADER,fragment);g.attachShader(p,v);g.attachShader(p,f);g.linkProgram(p);g.deleteShader(v);g.deleteShader(f);if(!g.getProgramParameter(p,g.LINK_STATUS))throw new Error(g.getProgramInfoLog(p));return p;}
    targetOf(w,h,linearTarget=false){const g=this.gl,t=g.createTexture(),floating=linearTarget&&g.getExtension('EXT_color_buffer_float')&&g.getExtension('EXT_float_blend');g.bindTexture(g.TEXTURE_2D,t);g.texImage2D(g.TEXTURE_2D,0,floating?g.RGBA16F:g.RGBA8,w,h,0,g.RGBA,floating?g.HALF_FLOAT:g.UNSIGNED_BYTE,null);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_MIN_FILTER,g.LINEAR);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_MAG_FILTER,g.LINEAR);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_WRAP_S,g.CLAMP_TO_EDGE);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_WRAP_T,g.CLAMP_TO_EDGE);const f=g.createFramebuffer();g.bindFramebuffer(g.FRAMEBUFFER,f);g.framebufferTexture2D(g.FRAMEBUFFER,g.COLOR_ATTACHMENT0,g.TEXTURE_2D,t,0);if(g.checkFramebufferStatus(g.FRAMEBUFFER)!==g.FRAMEBUFFER_COMPLETE)throw new Error('Incomplete render target');return {texture:t,framebuffer:f};}
    texture(url,repeat=false){const g=this.gl;const i=image(url);if(!i)return null;if(!this.textures.has(url)){const t=g.createTexture();g.bindTexture(g.TEXTURE_2D,t);g.pixelStorei(g.UNPACK_FLIP_Y_WEBGL,false);g.pixelStorei(g.UNPACK_PREMULTIPLY_ALPHA_WEBGL,false);g.pixelStorei(g.UNPACK_COLORSPACE_CONVERSION_WEBGL,g.NONE);g.texImage2D(g.TEXTURE_2D,0,g.SRGB8_ALPHA8,g.RGBA,g.UNSIGNED_BYTE,i);g.generateMipmap(g.TEXTURE_2D);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_MIN_FILTER,g.LINEAR_MIPMAP_NEAREST);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_MAG_FILTER,g.LINEAR);this.textures.set(url,{texture:t,width:i.naturalWidth,height:i.naturalHeight});}const entry=this.textures.get(url);entry.used=useFrame;g.bindTexture(g.TEXTURE_2D,entry.texture);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_WRAP_S,repeat?g.REPEAT:g.CLAMP_TO_EDGE);g.texParameteri(g.TEXTURE_2D,g.TEXTURE_WRAP_T,repeat?g.REPEAT:g.CLAMP_TO_EDGE);return entry;}
    bind(program){const g=this.gl;g.useProgram(program);g.bindBuffer(g.ARRAY_BUFFER,this.vertices);const point=g.getAttribLocation(program,'point');g.enableVertexAttribArray(point);g.vertexAttribPointer(point,2,g.FLOAT,false,0,0);}
    render(plan,view){
      if(this.lost)throw new Error('WebGL context lost');const g=this.gl,w=view.width,h=view.height,ppm=view.zoom/view.cell,origin=[view.panX*view.cell,view.panRow*view.cell];
      if(!this.target||this.canvas.width!==w||this.canvas.height!==h){this.canvas.width=w;this.canvas.height=h;if(this.target)for(const t of [this.target,this.erase]){g.deleteTexture(t.texture);g.deleteFramebuffer(t.framebuffer);}this.target=this.targetOf(w,h,true);this.erase=this.targetOf(w,h);}
      g.viewport(0,0,w,h);g.disable(g.DEPTH_TEST);g.disable(g.CULL_FACE);g.enable(g.BLEND);
      this.bind(this.program);const loc=name=>g.getUniformLocation(this.program,name);
      g.uniform2f(loc('resolution'),w,h);g.uniform2f(loc('viewOrigin'),...origin);g.uniform1f(loc('pixelsPerMeter'),ppm);g.uniform1f(loc('density'),plan.texelsPerMeter);
      const draw=(c,erasing=false)=>{
        const b=bounds(c);if((b.x-origin[0])*ppm>w||(b.y-origin[1])*ppm>h||(b.x+b.w-origin[0])*ppm<0||(b.y+b.h-origin[1])*ppm<0)return;
        g.activeTexture(g.TEXTURE0);const shape=c.image?this.texture(c.image):null;if(c.image&&!shape){stats.missing++;return;}g.bindTexture(g.TEXTURE_2D,shape?.texture??this.white);g.uniform1i(loc('shape'),0);
        g.activeTexture(g.TEXTURE1);const material=c.texture?this.texture(c.texture,true):null;if(c.texture&&!material){stats.missing++;return;}g.bindTexture(g.TEXTURE_2D,material?.texture??this.white);g.uniform1i(loc('material'),1);g.uniform2f(loc('materialSize'),material?.width??1,material?.height??1);
        g.activeTexture(g.TEXTURE2);g.bindTexture(g.TEXTURE_2D,erasing?this.white:this.erase.texture);g.uniform1i(loc('eraseMap'),2);
        g.uniform2f(loc('registration'),c.x,c.y);g.uniform2f(loc('size'),c.width,c.height);g.uniform2f(loc('pivot'),c.originX,c.originY);g.uniform2f(loc('scale'),c.scaleX,c.scaleY);g.uniform1f(loc('opacity'),c.alpha);
        g.uniform1i(loc('kind'),erasing?3:c.kind==='material'?1:c.kind==='fill'?2:0);g.uniform1i(loc('affected'),!erasing&&c.eraseAffected?1:0);
        const blend=c.blend==='multiply'?1:c.blend==='overlay'?2:0;g.uniform1i(loc('blendMode'),blend);
        if(erasing){g.blendEquation(g.MAX);g.blendFunc(g.ONE,g.ONE);}else{g.blendEquation(g.FUNC_ADD);if(blend===1)g.blendFunc(g.DST_COLOR,g.ZERO);else if(blend===2)g.blendFunc(g.DST_COLOR,g.SRC_COLOR);else g.blendFuncSeparate(g.SRC_ALPHA,g.ONE_MINUS_SRC_ALPHA,g.ONE,g.ONE_MINUS_SRC_ALPHA);}
        g.drawArrays(g.TRIANGLES,0,6);stats.draws++;
      };
      g.bindFramebuffer(g.FRAMEBUFFER,this.erase.framebuffer);g.clearColor(0,0,0,0);g.clear(g.COLOR_BUFFER_BIT);for(const c of plan.erases)draw(c,true);
      g.bindFramebuffer(g.FRAMEBUFFER,this.target.framebuffer);g.clearColor(linear(36/255),linear(48/255),linear(58/255),1);g.clear(g.COLOR_BUFFER_BIT);
      if(plan.farBackground){const i=image(plan.farBackground);if(i){const size=Math.max(w/i.naturalWidth,h/i.naturalHeight);draw({image:plan.farBackground,x:origin[0],y:origin[1],width:i.naturalWidth*size/ppm,height:i.naturalHeight*size/ppm,originX:0,originY:0,scaleX:1,scaleY:1,alpha:1,blend:'normal',kind:'sprite'});}else stats.missing++;}
      for(const c of plan.commands)draw(c);
      g.bindFramebuffer(g.FRAMEBUFFER,null);g.disable(g.BLEND);this.bind(this.output);g.activeTexture(g.TEXTURE0);g.bindTexture(g.TEXTURE_2D,this.target.texture);g.uniform1i(g.getUniformLocation(this.output,'scene'),0);g.drawArrays(g.TRIANGLES,0,6);
      const error=g.getError();if(error!==g.NO_ERROR)throw new Error('WebGL draw error0x'+error.toString(16));stats.backend='webgl2';return this.canvas;
    }
  }

  // Software fallback samples linear pixels/mip levels explicitly; Canvas is
  // used only to decode PNGs and display the final sRGB result.
  function raster(asset){
    if(asset.raster)return asset.raster;
    const canvas=document.createElement('canvas');canvas.width=asset.naturalWidth;canvas.height=asset.naturalHeight;const ctx=canvas.getContext('2d',{willReadFrequently:true});ctx.drawImage(asset,0,0);
    asset.raster=[{width:canvas.width,height:canvas.height,data:ctx.getImageData(0,0,canvas.width,canvas.height).data,encoded:true}];return asset.raster;
  }
  function channel(level,index,c){const value=level.data[index*4+c];return level.encoded?(c===3?value/255:decode[value]):value;}
  function bilinear(level,u,v,repeat,out){
    const w=level.width,h=level.height,x=u*w-.5,y=v*h-.5,ix=Math.floor(x),iy=Math.floor(y),fx=x-ix,fy=y-iy;
    const x0=repeat?(ix%w+w)%w:Math.max(0,Math.min(w-1,ix)),x1=repeat?((ix+1)%w+w)%w:Math.max(0,Math.min(w-1,ix+1));
    const y0=repeat?(iy%h+h)%h:Math.max(0,Math.min(h-1,iy)),y1=repeat?((iy+1)%h+h)%h:Math.max(0,Math.min(h-1,iy+1));
    const a=y0*w+x0,b=y0*w+x1,c=y1*w+x0,d=y1*w+x1;
    for(let k=0;k<4;k++)out[k]=(channel(level,a,k)*(1-fx)+channel(level,b,k)*fx)*(1-fy)+(channel(level,c,k)*(1-fx)+channel(level,d,k)*fx)*fy;
  }
  function mip(asset,rho){
    const levels=raster(asset),maximum=Math.floor(Math.log2(Math.max(asset.naturalWidth,asset.naturalHeight))),wanted=Math.min(maximum,Math.max(0,Math.round(Math.log2(Math.max(1,rho))))),sample=new Float32Array(4);
    while(levels.length<=wanted){const last=levels[levels.length-1],w=Math.max(1,last.width>>1),h=Math.max(1,last.height>>1),next={width:w,height:h,data:new Float32Array(w*h*4),encoded:false};for(let y=0;y<h;y++)for(let x=0;x<w;x++){bilinear(last,(x+.5)/w,(y+.5)/h,false,sample);next.data.set(sample,(y*w+x)*4);}levels.push(next);}
    return levels[wanted];
  }
  class Cpu {
    constructor(){this.canvas=document.createElement('canvas');}
    render(plan,view){
      const w=view.width,h=view.height,ppm=view.zoom/view.cell,ox=view.panX*view.cell,oy=view.panRow*view.cell;
      this.canvas.width=w;this.canvas.height=h;const ctx=this.canvas.getContext('2d'),rgb=new Float32Array(w*h*3),erase=new Uint8Array(w*h),shapePixel=new Float32Array(4),materialPixel=new Float32Array(4);
      for(let p=0;p<w*h;p++){rgb[p*3]=decode[36];rgb[p*3+1]=decode[48];rgb[p*3+2]=decode[58];}
      const draw=(c,erasing=false)=>{
        const b=bounds(c),left=Math.max(0,Math.floor((b.x-ox)*ppm)),top=Math.max(0,Math.floor((b.y-oy)*ppm)),right=Math.min(w,Math.ceil((b.x+b.w-ox)*ppm)),bottom=Math.min(h,Math.ceil((b.y+b.h-oy)*ppm));if(right<=left||bottom<=top)return;
        const shape=c.image?image(c.image):null,texture=c.texture?image(c.texture):null;if(c.image&&!shape||c.texture&&!texture){stats.missing++;return;}
        const shapeLevel=shape?mip(shape,Math.max(shape.naturalWidth/(ppm*c.width*Math.abs(c.scaleX)),shape.naturalHeight/(ppm*c.height*Math.abs(c.scaleY)))):null;
        const materialLevel=texture?mip(texture,plan.texelsPerMeter/ppm):null;
        for(let y=top;y<bottom;y++)for(let x=left;x<right;x++){
          const wx=ox+(x+.5)/ppm,wy=oy+(y+.5)/ppm,u=((wx-c.x)/c.scaleX+c.originX)/c.width,v=((wy-c.y)/c.scaleY+c.originY)/c.height;if(u<0||u>1||v<0||v>1)continue;
          if(shapeLevel)bilinear(shapeLevel,u,v,false,shapePixel);else shapePixel.fill(1);
          const at=y*w+x;
          if(erasing){erase[at]=Math.max(erase[at],Math.round(shapePixel[3]*255));continue;}
          if(materialLevel)bilinear(materialLevel,wx*plan.texelsPerMeter/texture.naturalWidth,wy*plan.texelsPerMeter/texture.naturalHeight,true,materialPixel);else materialPixel.set(shapePixel);
          let alpha=materialPixel[3]*(c.kind==='material'?shapePixel[3]:1)*c.alpha*(c.eraseAffected?1-erase[at]/255:1);alpha=Math.max(0,Math.min(1,alpha));
          for(let ch=0;ch<3;ch++){const color=materialPixel[ch],old=rgb[at*3+ch];rgb[at*3+ch]=c.blend==='multiply'?old*(1-alpha+color*alpha):c.blend==='overlay'?2*old*(.5*(1-alpha)+color*alpha):color*alpha+old*(1-alpha);}
        }
        stats.draws++;
      };
      for(const c of plan.erases)draw(c,true);
      if(plan.farBackground){const i=image(plan.farBackground);if(i){const size=Math.max(w/i.naturalWidth,h/i.naturalHeight);draw({image:plan.farBackground,x:ox,y:oy,width:i.naturalWidth*size/ppm,height:i.naturalHeight*size/ppm,originX:0,originY:0,scaleX:1,scaleY:1,alpha:1,kind:'sprite'});}else stats.missing++;}
      for(const c of plan.commands)draw(c);
      const result=ctx.createImageData(w,h);for(let p=0;p<w*h;p++){for(let c=0;c<3;c++)result.data[p*4+c]=Math.round(Math.max(0,Math.min(1,srgb(rgb[p*3+c])))*255);result.data[p*4+3]=255;}ctx.putImageData(result,0,0);stats.backend='canvas-cpu';return this.canvas;
    }
  }
  let gpu,cpu;
  return {
    render(plan,view,onchange,forceCpu=false){dirty=onchange;useFrame++;stats.draws=0;stats.missing=0;stats.error=null;
      const finish=canvas=>{for(const [key,i] of assets)if(i.used<useFrame-1)assets.delete(key);if(gpu&&!gpu.lost)for(const [key,t] of gpu.textures)if(t.used<useFrame-1){gpu.gl.deleteTexture(t.texture);gpu.textures.delete(key);}return canvas;};
      if(!forceCpu)try{gpu??=new Gpu();return finish(gpu.render(plan,view));}catch(e){stats.error=String(e);}
      cpu??=new Cpu();return finish(cpu.render(plan,view));
    },
    stats(){return {...stats};},
    reset(){assets.clear();gpu=null;cpu=null;}
  };
})();
