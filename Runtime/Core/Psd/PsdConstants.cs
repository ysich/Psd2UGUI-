namespace Psd2Ugui.Core.Psd
{
    public enum PsdColorMode
    {
        Bitmap = 0,
        Grayscale = 1,
        Indexed = 2,
        Rgb = 3,
        Cmyk = 4,
        Multichannel = 7,
        Duotone = 8,
        Lab = 9
    }

    /// <summary>附加图层信息（Additional Layer Information）的键。</summary>
    public static class PsdKeys
    {
        public const string UnicodeName = "luni";
        public const string LayerId = "lyid";
        public const string SectionDivider = "lsct";
        public const string SectionDividerSetting = "lsdk";
        public const string NameSource = "lnsr";
        public const string SolidColor = "SoCo";
        public const string GradientFill = "GdFl";
        public const string PatternFill = "PtFl";
        public const string VectorStrokeContent = "vscg";
        public const string VectorMask = "vmsk";
        public const string VectorMaskStroked = "vsms";
        public const string TypeTool = "TySh";
        public const string TypeToolObjectSetting = "TySh";
        public const string Effects = "lfx2";
        public const string EffectsOld = "lrFX";
        public const string BlendClipped = "clbl";
        public const string BlendInterior = "infx";
        public const string Knockout = "knko";
        public const string Protected = "lspf";
        public const string SheetColor = "lclr";
        public const string Metadata = "shmd";
        public const string FillOpacity = "iOpa";
        public const string PixelSourceData = "PxSD";
        public const string SmartObjectLayer = "SoLd";
        public const string SmartObjectPlaced = "PlLd";
        public const string TextEngineData = "Txt2";
        public const string Artboard = "artb";
        public const string ArtboardData = "artd";
        public const string LayerMaskAsGlobalMask = "LMsk";
        public const string TransparencyShapesLayer = "tsly";
    }

    /// <summary>分组分隔符类型（lsct 的第一个 uint32）。</summary>
    public static class PsdSectionDivider
    {
        public const int Other = 0;
        public const int OpenFolder = 1;
        public const int ClosedFolder = 2;
        public const int BoundingSectionDivider = 3;
    }

    public static class PsdLayerFlags
    {
        public const int TransparencyProtected = 1;

        /// <summary>该位为 1 表示图层被隐藏；0 才是可见。规范里的字面描述与取值相反。</summary>
        public const int Hidden = 2;

        public const int Obsolete = 4;
        public const int PixelDataIrrelevant = 8;
    }

    public static class PsdCompression
    {
        public const int Raw = 0;
        public const int Rle = 1;
        public const int Zip = 2;
        public const int ZipWithPrediction = 3;
    }

    /// <summary>通道 ID 约定。</summary>
    public static class PsdChannelId
    {
        public const int Red = 0;
        public const int Green = 1;
        public const int Blue = 2;
        public const int Transparency = -1;
        public const int UserMask = -2;
        public const int RealUserMask = -3;
        public const int CmykCyan = 0;
        public const int CmykMagenta = 1;
        public const int CmykYellow = 2;
        public const int CmykBlack = 3;
        public const int Grayscale = 0;
    }

    public static class PsdImageResourceIds
    {
        public const int ResolutionInfo = 1005;
        public const int AlphaChannelNames = 1006;
        public const int IccProfile = 1039;
        public const int LayerStateInfo = 1024;
        public const int VersionInfo = 1057;
        public const int ExifData1 = 1058;
        public const int XmpMetadata = 1060;
        public const int IptcNaa = 1028;
        public const int LayersGroupInfo = 1026;
    }
}
