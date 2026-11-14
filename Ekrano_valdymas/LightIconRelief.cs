namespace Ekrano_valdymas;

// Image-space metal relief for arbitrary user icons, computed once per cache
// entry. No per-frame work, asset exports or assumptions about a logo's shape.
internal static class LightIconRelief
{
    internal static void Render(byte[] pixels,int width,int height,byte[] grain)
    {
        int count=width*height;
        var value=new float[count];var relief=new float[count];
        for(int n=0;n<count;n++)
        {
            int p=n*4;double alpha=pixels[p+3]/255d;
            double luma=(.2126*pixels[p+2]+.7152*pixels[p+1]+.0722*pixels[p])/255;
            double maximum=Math.Max(pixels[p],Math.Max(pixels[p+1],pixels[p+2]))/255d;
            value[n]=(float)(.35*luma+.65*maximum);
            relief[n]=(float)((.22+.78*value[n])*alpha);
        }
        // Soften existing raster highlights before deriving a shallow bevel.
        int radius=Math.Max(1,(int)Math.Round(Math.Min(width,height)/96d));
        var horizontal=new float[count];var smooth=new float[count];
        for(int y=0;y<height;y++)
        {
            double sum=0;for(int x=-radius;x<=radius;x++)sum+=relief[y*width+Math.Clamp(x,0,width-1)];
            for(int x=0;x<width;x++)
            {
                horizontal[y*width+x]=(float)(sum/(radius*2+1));
                sum+=relief[y*width+Math.Clamp(x+radius+1,0,width-1)]-relief[y*width+Math.Clamp(x-radius,0,width-1)];
            }
        }
        for(int x=0;x<width;x++)
        {
            double sum=0;for(int y=-radius;y<=radius;y++)sum+=horizontal[Math.Clamp(y,0,height-1)*width+x];
            for(int y=0;y<height;y++)
            {
                smooth[y*width+x]=(float)(sum/(radius*2+1));
                sum+=horizontal[Math.Clamp(y+radius+1,0,height-1)*width+x]-horizontal[Math.Clamp(y-radius,0,height-1)*width+x];
            }
        }
        float At(int x,int y)=>smooth[Math.Clamp(y,0,height-1)*width+Math.Clamp(x,0,width-1)];
        var low=IconAppearance.DarkTone(true);var high=IconAppearance.LightTone(true);
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)
        {
            int n=y*width+x,p=n*4;if(pixels[p+3]==0)continue;
            double v=value[n];
            double dx=At(x+radius,y)-At(x-radius,y);
            double dy=At(x,y+radius)-At(x,y-radius);
            // Upper-left light: light-facing edges rise; opposite edges recess.
            double edge=(dx*.60+dy*.80)*210;
            double sweep=1-(x/(double)Math.Max(1,width-1)+y/(double)Math.Max(1,height-1));
            double body=14+125*Math.Pow(v,.78);
            double brushed=(grain[(y%128)*128+x%128]-128)*.13;
            double tone=Math.Clamp(body+edge+sweep*12+brushed*(.25+.75*v),low.R,high.R);
            double t=(tone-low.R)/(high.R-low.R);
            pixels[p]=(byte)Math.Round(low.B+(high.B-low.B)*t);
            pixels[p+1]=(byte)Math.Round(low.G+(high.G-low.G)*t);
            pixels[p+2]=(byte)Math.Round(tone);
            // The original alpha is deliberately untouched, including holes.
        }
    }
}
