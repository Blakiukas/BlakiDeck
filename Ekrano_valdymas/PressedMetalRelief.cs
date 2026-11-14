using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
namespace Ekrano_valdymas;
// Cached surface-relative relief overlay: the underlying skin remains visible everywhere.
internal static class PressedMetalRelief
{
 internal const double IconPadding=6;
 internal static int Computations {get;private set;}
 internal static double Milliseconds {get;private set;}
 internal static BitmapSource Render(FrameworkElement owner,ImageSource icon,bool dimmed,double depthMultiplier=1,double highlightMultiplier=1,double contrast=1,double inset=0,Brush? material=null,double? sampling=null,bool normalizeIcon=false)
 {
  var watch=System.Diagnostics.Stopwatch.StartNew();
  double logical=Math.Max(owner.ActualWidth,owner.ActualHeight);
  double padding=normalizeIcon?IconPadding:0;
  int scale=(int)(sampling??LightMetalBevel.Sampling(owner)),size=Math.Max(1,(int)Math.Ceiling((logical+2*padding)*scale));
  BitmapSource Raster(DrawingVisual visual){var b=new RenderTargetBitmap(size,size,96*scale,96*scale,PixelFormats.Pbgra32);b.Render(visual);return b;}
  var v=new DrawingVisual();using(var dc=v.RenderOpen()){dc.PushTransform(new TranslateTransform(padding,padding));dc.DrawRectangle(material??CommonMetal.ForBevel(owner),null,new Rect(-padding,-padding,logical+2*padding,logical+2*padding));dc.Pop();}
  var pixels=new byte[size*size*4];Raster(v).CopyPixels(pixels,size*4,0);
  if(dimmed)for(int p=0;p<pixels.Length;p+=4)for(int c=0;c<3;c++)pixels[p+c]=(byte)Math.Round(pixels[p+c]*130/255d);
  var original=(byte[])pixels.Clone();
  var mask=new DrawingVisual();using(var dc=mask.RenderOpen()){
   dc.PushTransform(new TranslateTransform(padding,padding));
   double factor=Math.Min(Math.Max(1,owner.ActualWidth-2*inset)/icon.Width,Math.Max(1,owner.ActualHeight-2*inset)/icon.Height),w=icon.Width*factor,h=icon.Height*factor;
   dc.DrawImage(icon,new Rect((owner.ActualWidth-w)/2,(owner.ActualHeight-h)/2,w,h));dc.Pop();
  }
  var rgba=new byte[pixels.Length];Raster(mask).CopyPixels(rgba,size*4,0);
  // Normalize profile dimensions to the selected C preview's 128-unit icon.
  Apply(pixels,size,0,0,rgba,size,"pressed",Math.Max(1,(int)Math.Round(logical*scale/128d)),depthMultiplier,highlightMultiplier,contrast,normalizeIcon);
  for(int p=0;p<pixels.Length;p+=4){
   double before=(original[p]+original[p+1]+original[p+2])/3d,after=(pixels[p]+pixels[p+1]+pixels[p+2])/3d,delta=after-before;
   double opacity=delta>=0?delta/Math.Max(1,255-before):-delta/Math.Max(1,before);
   byte a=(byte)Math.Clamp(Math.Round(opacity*255),0,255);
   pixels[p]=pixels[p+1]=pixels[p+2]=delta>=0?a:(byte)0;pixels[p+3]=a;
  }
  var result=BitmapSource.Create(size,size,96*scale,96*scale,PixelFormats.Pbgra32,null,pixels,size*4);result.Freeze();
  Computations++;Milliseconds+=watch.Elapsed.TotalMilliseconds;return result;
 }
 static void Apply(byte[] surface,int strideWidth,int left,int top,byte[] icon,int size,string mode,int scale,double depthMultiplier,double highlightMultiplier,double contrast,bool normalizeIcon)
 {
  var map=new double[size*size];
  for(int n=0;n<map.Length;n++){
   int p=n*4;double a=icon[p+3]/255d;if(a<.002)continue;
   double value=(.35*(.2126*icon[p+2]+.7152*icon[p+1]+.0722*icon[p])+.65*Math.Max(icon[p],Math.Max(icon[p+1],icon[p+2])))/(255*a);
   // Preserve internal color contrast: saturated blue and white must not both clip to full height.
   double luminance=(.2126*icon[p+2]+.7152*icon[p+1]+.0722*icon[p])/(255*a);
   double height=.3+.7*Math.Clamp(luminance,0,1);
   map[n]=normalizeIcon?a*height:a*(.055+.945*Math.Pow(Math.Clamp(value,0,1),1.35));
  }
  double radius=(mode=="cast"?1.8:mode=="emerge"?3.5:normalizeIcon?1.05:.65)*scale;
  int r=(int)Math.Ceiling(radius*3);var kernel=Enumerable.Range(-r,2*r+1).Select(i=>Math.Exp(-i*i/(2*radius*radius))).ToArray();double sum=kernel.Sum();for(int i=0;i<kernel.Length;i++)kernel[i]/=sum;
  var horizontal=new double[map.Length];var smooth=new double[map.Length];
  for(int y=0;y<size;y++)for(int x=0;x<size;x++)for(int k=-r;k<=r;k++)if(x+k>=0&&x+k<size)horizontal[y*size+x]+=map[y*size+x+k]*kernel[k+r];
  for(int y=0;y<size;y++)for(int x=0;x<size;x++)for(int k=-r;k<=r;k++)if(y+k>=0&&y+k<size)smooth[y*size+x]+=horizontal[(y+k)*size+x]*kernel[k+r];
  double At(int x,int y)=>smooth[Math.Clamp(y,0,size-1)*size+Math.Clamp(x,0,size-1)];
  double depth=(mode=="cast"?3.8:mode=="emerge"?2.4:1.9)*depthMultiplier;
  const double lx=-.38,ly=-.52,lz=.765;double hx=lx,hy=ly,hz=lz+1,hn=Math.Sqrt(hx*hx+hy*hy+hz*hz);hx/=hn;hy/=hn;hz/=hn;
  double power=mode=="pressed"?44:24,shine=(mode=="pressed"?.5:.32)*highlightMultiplier;
  double reference=.32+.68*lz+shine*Math.Pow(hz,power);
  var lighting=new double[size*size];
  for(int y=0;y<size;y++)for(int x=0;x<size;x++){
   double nx=-(At(x+1,y)-At(x-1,y))*.5*scale*depth,ny=-(At(x,y+1)-At(x,y-1))*.5*scale*depth;
   double length=Math.Sqrt(nx*nx+ny*ny+1);nx/=length;ny/=length;double nz=1/length;
   double illumination=.32+.68*Math.Max(0,nx*lx+ny*ly+nz*lz)+shine*Math.Pow(Math.Max(0,nx*hx+ny*hy+nz*hz),power);
   lighting[y*size+x]=illumination/reference;
  }
  for(int y=0;y<size;y++)for(int x=0;x<size;x++){
   double ratio=lighting[y*size+x];
   if(normalizeIcon){
    // A soft directional relief shadow, not a min-filtered hard outline.
    int spread=Math.Max(1,(int)Math.Ceiling(scale*1.5));double shadow=0,weightSum=0;
    double sigma=Math.Max(.7,scale*.7);
    for(int dy=-spread;dy<=spread;dy++)for(int dx=-spread;dx<=spread;dx++){
     double weight=Math.Exp(-(dx*dx+dy*dy)/(2*sigma*sigma));
     double neighbor=lighting[Math.Clamp(y+dy,0,size-1)*size+Math.Clamp(x+dx,0,size-1)];
     shadow+=Math.Min(0,neighbor-1)*weight;weightSum+=weight;
    }
    ratio=1+Math.Max(0,ratio-1)+1.25*shadow/weightSum;
   }   int p=((top+y)*strideWidth+left+x)*4;
   // Reflected edge light remains visible on black finishes. Preserve the
   // underlying grain; only the normal-dependent lighting delta is added.
   for(int channel=0;channel<3;channel++){
    double basis=surface[p+channel];
    surface[p+channel]=(byte)Math.Clamp(Math.Round(basis+Math.Max(145,basis)*(ratio-1)*contrast+At(x,y)*3*Math.Min(1,depthMultiplier)*contrast),0,255);
   }
  }
 }
}




