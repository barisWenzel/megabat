using System;
using System.Drawing;
using Grasshopper;
using Grasshopper.Kernel;

namespace Megabat
{
  public class MegabatInfo : GH_AssemblyInfo
  {
    public override string Name => "Megabat";
    public override Bitmap Icon => Icons.Logo();
    public override string Description => "Point cloud tools for scan-to-BIM: thin, crop, detect planes, snap walls and openings from sketches, sections.";
    public override Guid Id => new Guid("6f1a2c4e-8b3d-4e7a-9c15-2d4b6e8f0a11");
    public override string AuthorName => "Baris Wenzel";
    public override string AuthorContact => "https://github.com/barisWenzel/megabat";
    public override string Version => "0.1.0";
    public override GH_LibraryLicense License => GH_LibraryLicense.opensource;
  }

  // On load: tab icon and PreviewGuard with its switch in the canvas toolbar
  public class MegabatPriority : GH_AssemblyPriority
  {
    public override GH_LoadingInstruction PriorityLoad()
    {
      Instances.ComponentServer.AddCategoryIcon(Tab.Name, Icons.Logo(16));
      Instances.ComponentServer.AddCategorySymbolName(Tab.Name, 'M');
      Instances.CanvasCreated += canvas => { Display.PreviewGuard.Start(); Display.CloudConduit.Start(); };
      return GH_LoadingInstruction.Proceed;
    }
  }

  public static class Tab
  {
    public const string Name = "Megabat";
    public const string Cloud = "1 Cloud";
    public const string Segments = "2 Segments";
    public const string Elements = "3 Elements";
    public const string Section = "4 Section";
    public const string Display = "5 Display";
  }
}
