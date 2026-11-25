using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.IO;
internal static class Program {
 [STAThread] static int Main(string[] args) {
  try {
   if(args.Length!=0)throw new ArgumentException("stdin JPEG only");
   using var input=new MemoryStream();using var stdin=Console.OpenStandardInput();
   var buffer=new byte[65536];int n;
   while((n=stdin.Read(buffer,0,buffer.Length))>0){if(input.Length+n>10000000)throw new ArgumentException("Input limit");input.Write(buffer,0,n);}
   if(input.Length<16)throw new ArgumentException("Short JPEG");
   input.Position=0;
   if(input.ReadByte()!=255||input.ReadByte()!=216)throw new ArgumentException("JPEG signature required");
   input.Position=0;
   var decoder=new JpegBitmapDecoder(input,BitmapCreateOptions.PreservePixelFormat,BitmapCacheOption.OnDemand);
   if(decoder.Frames.Count!=1)throw new ArgumentException("Single frame required");
   var f=decoder.Frames[0];if(f.PixelWidth<16||f.PixelHeight<16||f.PixelWidth>4096||f.PixelHeight>4096)throw new ArgumentException("Image dimensions outside bound");
   var b=new FormatConvertedBitmap(f,PixelFormats.Bgr24,null,0);
   // Copy pixel buffer to detach metadata and source from the encoder.
   int stride=checked(b.PixelWidth*3);var pixels=new byte[checked(stride*b.PixelHeight)];b.CopyPixels(pixels,stride,0);
   var plain=BitmapSource.Create(b.PixelWidth,b.PixelHeight,96,96,PixelFormats.Bgr24,null,pixels,stride);
   var enc=new PngBitmapEncoder();enc.Frames.Add(BitmapFrame.Create(plain));
   using var output=new MemoryStream();enc.Save(output);
   if(output.Length>10000000)throw new ArgumentException("Output limit");
   output.Position=0;using var stdout=Console.OpenStandardOutput();output.CopyTo(stdout);stdout.Flush();return 0;
  }catch(Exception ex){Console.Error.WriteLine(ex.GetType().Name+": "+ex.Message);return 2;}
 }
}