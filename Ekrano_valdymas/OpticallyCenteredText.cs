using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace Ekrano_valdymas
{
    internal sealed class OpticallyCenteredText : FrameworkElement
    {
        public static readonly DependencyProperty TextProperty = DependencyProperty.Register(
            nameof(Text), typeof(string), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextBrushProperty = DependencyProperty.Register(
            nameof(TextBrush), typeof(Brush), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextFontFamilyProperty = DependencyProperty.Register(
            nameof(TextFontFamily), typeof(FontFamily), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(SystemFonts.MessageFontFamily, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextFontSizeProperty = DependencyProperty.Register(
            nameof(TextFontSize), typeof(double), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(28d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty TextFontWeightProperty = DependencyProperty.Register(
            nameof(TextFontWeight), typeof(FontWeight), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(FontWeights.Bold, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty LineAdvanceProperty = DependencyProperty.Register(
            nameof(LineAdvance), typeof(double), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(0d, FrameworkPropertyMetadataOptions.AffectsMeasure | FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty TextFontStyleProperty = DependencyProperty.Register(
            nameof(TextFontStyle),typeof(FontStyle),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(FontStyles.Normal,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
        public FontStyle TextFontStyle { get=>(FontStyle)GetValue(TextFontStyleProperty);set=>SetValue(TextFontStyleProperty,value); }
        public static readonly DependencyProperty InsetStrengthProperty = DependencyProperty.Register(
            nameof(InsetStrength),typeof(double),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(1d,FrameworkPropertyMetadataOptions.AffectsRender));
        public double InsetStrength { get=>(double)GetValue(InsetStrengthProperty);set=>SetValue(InsetStrengthProperty,value); }
        public static readonly DependencyProperty InkAlignmentProperty=DependencyProperty.Register(
            nameof(InkAlignment),typeof(HorizontalAlignment),typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(HorizontalAlignment.Center,FrameworkPropertyMetadataOptions.AffectsRender));
        public HorizontalAlignment InkAlignment { get => (HorizontalAlignment)GetValue(InkAlignmentProperty); set => SetValue(InkAlignmentProperty,value); }
        public VerticalAlignment InkVerticalAlignment { get; set; } = VerticalAlignment.Center;
        public bool Ellipsis { get; set; }
        public static readonly DependencyProperty SkinMetalTextProperty=DependencyProperty.Register(nameof(SkinMetalText),typeof(bool),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
        public static readonly DependencyProperty SkinTextStyleProperty=DependencyProperty.Register(nameof(SkinTextStyle),typeof(string),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(null,FrameworkPropertyMetadataOptions.AffectsRender));
        public string? SkinTextStyle {get=>(string?)GetValue(SkinTextStyleProperty);set=>SetValue(SkinTextStyleProperty,value);}
        public bool SkinMetalText {get=>(bool)GetValue(SkinMetalTextProperty);set=>SetValue(SkinMetalTextProperty,value);}
        public bool UseGlyphGeometry { get; set; }
        public bool CenterCapToBaseline { get; set; } = true;
        public bool CenterSymbolsOnCapReference { get; set; }
        public static readonly DependencyProperty CompactLineBoxProperty=DependencyProperty.Register(nameof(CompactLineBox),typeof(bool),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsMeasure|FrameworkPropertyMetadataOptions.AffectsRender));
        public bool CompactLineBox { get=>(bool)GetValue(CompactLineBoxProperty); set=>SetValue(CompactLineBoxProperty,value); }
        internal double InkLuminance {get;set;}=1;
        private Brush Attenuate(Brush brush)
        {
            if(InkLuminance>=1)return brush;
            Color Dim(Color c)=>Color.FromArgb(c.A,(byte)Math.Round(c.R*InkLuminance),(byte)Math.Round(c.G*InkLuminance),(byte)Math.Round(c.B*InkLuminance));
            if(brush is SolidColorBrush solid)return new SolidColorBrush(Dim(solid.Color));
            if(brush is LinearGradientBrush gradient){var copy=gradient.Clone();foreach(var stop in copy.GradientStops)stop.Color=Dim(stop.Color);return copy;}
            return brush;
        }
        public static readonly DependencyProperty InsetMetalTextProperty = DependencyProperty.Register(
            nameof(InsetMetalText), typeof(bool), typeof(OpticallyCenteredText),
            new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.AffectsRender));
        public bool InsetMetalText { get => (bool)GetValue(InsetMetalTextProperty); set => SetValue(InsetMetalTextProperty,value); }
        public static readonly DependencyProperty DarkMetalInsetProperty = DependencyProperty.Register(
            nameof(DarkMetalInset),typeof(bool),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
        public bool DarkMetalInset {get=>(bool)GetValue(DarkMetalInsetProperty);set=>SetValue(DarkMetalInsetProperty,value);}
        public static readonly DependencyProperty InsetOnBrightMetalProperty = DependencyProperty.Register(
            nameof(InsetOnBrightMetal),typeof(bool),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(true,FrameworkPropertyMetadataOptions.AffectsRender));
        public bool InsetOnBrightMetal { get => (bool)GetValue(InsetOnBrightMetalProperty); set => SetValue(InsetOnBrightMetalProperty,value); }
        private double InkX(Rect ink) => (InkAlignment == HorizontalAlignment.Left ? 0 : InkAlignment == HorizontalAlignment.Right ? ActualWidth - ink.Width : (ActualWidth - ink.Width) / 2) - ink.X;

        public string Text { get => (string)GetValue(TextProperty); set => SetValue(TextProperty, value); }
        public Brush TextBrush { get => (Brush)GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }
        public FontFamily TextFontFamily { get => (FontFamily)GetValue(TextFontFamilyProperty); set => SetValue(TextFontFamilyProperty, value); }
        public double TextFontSize { get => (double)GetValue(TextFontSizeProperty); set => SetValue(TextFontSizeProperty, value); }
        public FontWeight TextFontWeight { get => (FontWeight)GetValue(TextFontWeightProperty); set => SetValue(TextFontWeightProperty, value); }
        public double LineAdvance { get => (double)GetValue(LineAdvanceProperty); set => SetValue(LineAdvanceProperty, value); }

        // These caches belong only to this element and only to its current
        // text/font environment. Natural, fitted and ellipsized variants share
        // the same glyphs between measure, rendering and ink inspection.
        private readonly record struct FontInputs(FontFamily Family, FontStyle Style, FontWeight Weight,
            DpiScale Dpi, FlowDirection Flow, TextFormattingMode FormattingMode, string CultureName);
        private readonly record struct LayoutInputs(long Revision, double Width, double Height, double Size,
            double Advance, bool Fit, bool Ellipsis, bool Caps, bool Symbols, bool Compact, bool GlyphGeometry,
            HorizontalAlignment Horizontal, VerticalAlignment Vertical);
        private sealed class GlyphLine(FormattedText formatted, Geometry glyph, Brush foreground)
        {
            internal readonly FormattedText Formatted = formatted;
            internal readonly Geometry Glyph = glyph;
            internal Brush Foreground = foreground;
        }
        private sealed class TextShapes(double size, double? trimWidth, bool wholeText, GlyphLine[] lines)
        {
            internal readonly double Size = size;
            internal readonly double? TrimWidth = trimWidth;
            internal readonly bool WholeText = wholeText;
            internal readonly GlyphLine[] Lines = lines;
            internal readonly FormattedText[] Formatted = lines.Select(line => line.Formatted).ToArray();
        }
        private sealed class RenderLayout(LayoutInputs key, TextShapes shapes, Rect reference, double advance, double offset, Point[] origins)
        {
            internal readonly LayoutInputs Key = key;
            internal readonly TextShapes Shapes = shapes;
            internal readonly Rect Reference = reference;
            internal readonly double Advance = advance, Offset = offset;
            internal readonly Point[] Origins = origins;
            internal readonly Geometry?[] Positioned = new Geometry?[origins.Length];
            internal Rect RenderedInk = Rect.Empty;
            internal bool RenderedInkKnown;
        }
        private FontInputs? shapingInputs;
        private CultureInfo? shapingCulture;
        private string? shapingText;
        private string[] shapingLines = [];
        private readonly List<TextShapes> shapeVariants = new(4);
        private readonly List<(double Size, GlyphLine Line)> capVariants = new(4);
        private string? alternateText;
        private TextShapes? alternateShapes;
        private RenderLayout? renderLayout;
        private long shapingRevision, geometryBuilds;

        public OpticallyCenteredText() => Unloaded += (_, _) => ClearTextLayoutCache();

        private void EnsureShapingInputs()
        {
            var culture = CultureInfo.CurrentUICulture;
            var inputs = new FontInputs(TextFontFamily, TextFontStyle, TextFontWeight, VisualTreeHelper.GetDpi(this),
                FlowDirection, TextOptions.GetTextFormattingMode(this), culture.Name);
            if (shapingInputs != inputs || !ReferenceEquals(shapingCulture, culture))
            {
                shapingInputs = inputs; shapingCulture = culture;
                shapeVariants.Clear(); capVariants.Clear(); alternateShapes = null; alternateText = null; renderLayout = null; shapingRevision++;
            }
            string text = Text ?? string.Empty;
            if (!string.Equals(shapingText, text, StringComparison.Ordinal))
            {
                shapingText = text; shapingLines = text.Replace("\r", string.Empty).Split('\n');
                shapeVariants.Clear(); alternateShapes = null; alternateText = null; renderLayout = null; shapingRevision++;
            }
        }

        private GlyphLine BuildLine(string text, double size, double? trimWidth)
        {
            var inputs = shapingInputs!.Value;
            var formatted = new FormattedText(text, shapingCulture!, FlowDirection.LeftToRight,
                new Typeface(inputs.Family, inputs.Style, inputs.Weight, FontStretches.Normal),
                size, TextBrush, inputs.Dpi.PixelsPerDip);
            if (trimWidth.HasValue)
            {
                formatted.MaxTextWidth = trimWidth.Value; formatted.MaxLineCount = 1;
                formatted.Trimming = TextTrimming.CharacterEllipsis;
            }
            Geometry glyph = formatted.BuildGeometry(new Point()); geometryBuilds++;
            if(IsNetworkRate(text))
            {
                FormattedText Part(string value)=>new(value,shapingCulture!,FlowDirection.LeftToRight,new Typeface(inputs.Family,inputs.Style,inputs.Weight,FontStretches.Normal),size,TextBrush,inputs.Dpi.PixelsPerDip);
                var parts=text.Split(' ',StringSplitOptions.RemoveEmptyEntries);
                var group=new GeometryGroup();double digit=Enumerable.Range(0,10).Max(n=>Part(n.ToString()).WidthIncludingTrailingWhitespace);
                int digits=Math.Max(4,parts[1].Count(char.IsDigit));
                double prefix=Part("DL ").WidthIncludingTrailingWhitespace,numberWidth=digit*digits+Part(".").WidthIncludingTrailingWhitespace,unitX=prefix+numberWidth+Part(" ").WidthIncludingTrailingWhitespace;
                group.Children.Add(Part(parts[0]).BuildGeometry(new Point()));
                double x=prefix+numberWidth-parts[1].Sum(c=>char.IsDigit(c)?digit:Part(c.ToString()).WidthIncludingTrailingWhitespace);
                foreach(char c in parts[1]){var item=Part(c.ToString());double advance=char.IsDigit(c)?digit:item.WidthIncludingTrailingWhitespace;group.Children.Add(item.BuildGeometry(new Point(x+(advance-item.WidthIncludingTrailingWhitespace)/2,0)));x+=advance;}
                group.Children.Add(Part(parts[2]).BuildGeometry(new Point(unitX,0)));
                glyph=group;formatted=Part(parts[0]+" "+new string('8',digits)+". MB/s");
            }
            if (glyph.CanFreeze) glyph.Freeze();
            return new GlyphLine(formatted, glyph, TextBrush);
        }

        internal static bool IsNetworkRate(string text)=>
            (text.StartsWith("DL ",StringComparison.Ordinal)||text.StartsWith("UP ",StringComparison.Ordinal))&&text.Split(' ',StringSplitOptions.RemoveEmptyEntries) is var parts&&parts.Length==3&&parts[2].EndsWith("B/s",StringComparison.Ordinal);

        private TextShapes GetShapes(double size, double? trimWidth = null, bool keepWholeText = false)
        {
            EnsureShapingInputs();
            bool whole = keepWholeText && (shapingText!.Contains('\r') || shapingText.Contains('\n'));
            int index = -1;
            for(int candidate=0;candidate<shapeVariants.Count;candidate++)
                if(shapeVariants[candidate].Size.Equals(size)&&shapeVariants[candidate].TrimWidth.Equals(trimWidth)&&shapeVariants[candidate].WholeText==whole){index=candidate;break;}
            if (index >= 0)
            {
                var found = shapeVariants[index]; shapeVariants.RemoveAt(index); shapeVariants.Add(found); return found;
            }
            string[] lines = whole ? [shapingText!] : shapingLines;
            var result = new TextShapes(size, trimWidth, whole, lines.Select(line => BuildLine(line, size, trimWidth)).ToArray());
            if (shapeVariants.Count == 4) shapeVariants.RemoveAt(0);
            shapeVariants.Add(result); return result;
        }

        private GlyphLine GetCaps(double size)
        {
            EnsureShapingInputs();
            int index = -1;
            for(int candidate=0;candidate<capVariants.Count;candidate++)
                if(capVariants[candidate].Size.Equals(size)){index=candidate;break;}
            if (index >= 0)
            {
                var found = capVariants[index]; capVariants.RemoveAt(index); capVariants.Add(found); return found.Line;
            }
            var line = BuildLine("H", size, null);
            if (capVariants.Count == 4) capVariants.RemoveAt(0);
            capVariants.Add((size, line)); return line;
        }

        internal (double Width, double Reference, double Extent, double TopOverflow, double BottomOverflow) GetNaturalMetrics(double size)
        {
            var line = GetShapes(size, keepWholeText: true).Lines[0]; var cap = GetCaps(size);
            Rect ink = line.Glyph.Bounds; double top = cap.Glyph.Bounds.Top;
            double above = ink.IsEmpty ? 0 : Math.Max(0, top - ink.Top), below = ink.IsEmpty ? 0 : Math.Max(0, ink.Bottom - cap.Formatted.Baseline);
            double reference = cap.Formatted.Baseline - top;
            return (Math.Max(line.Formatted.WidthIncludingTrailingWhitespace, ink.IsEmpty ? 0 : ink.Width), reference,
                reference + Math.Max(above, below) * 2, above, below);
        }

        internal (double Width, double Reference, double BottomOverflow, int LineCount) GetValueRowMetrics(double size, string? content = null)
        {
            EnsureShapingInputs();
            TextShapes shapes;
            if (content == null || string.Equals(content, shapingText, StringComparison.Ordinal)) shapes = GetShapes(size);
            else
            {
                if (alternateShapes == null || !alternateShapes.Size.Equals(size) || !string.Equals(alternateText, content, StringComparison.Ordinal))
                {
                    alternateText = content;
                    alternateShapes = new TextShapes(size, null, false,
                        content.Replace("\r", string.Empty).Split('\n').Select(line => BuildLine(line, size, null)).ToArray());
                }
                shapes = alternateShapes;
            }
            var cap = GetCaps(size); double width = 0, descent = 0;
            foreach (var line in shapes.Lines)
            {
                Rect ink = line.Glyph.Bounds;
                width = Math.Max(width, Math.Max(line.Formatted.WidthIncludingTrailingWhitespace, ink.IsEmpty ? 0 : ink.Width));
                if (!ink.IsEmpty) descent = Math.Max(descent, Math.Max(0, ink.Bottom - cap.Formatted.Baseline));
            }
            return (width, cap.Formatted.Baseline - cap.Glyph.Bounds.Top, descent, shapes.Lines.Length);
        }

        private Geometry PositionedGlyph(RenderLayout layout, int index)
        {
            if (layout.Positioned[index] is { } cached) return cached;
            var origin = layout.Origins[index];
            Geometry glyph;
            if (origin == new Point()) glyph = layout.Shapes.Lines[index].Glyph;
            else if(IsNetworkRate(Text))
            {
                glyph=layout.Shapes.Lines[index].Glyph.Clone();glyph.Transform=new TranslateTransform(origin.X,origin.Y);glyph.Freeze();
            }
            else
            {
                // Keep the original outline construction at its exact origin;
                // no transformed replacement can change material sampling.
                glyph = layout.Shapes.Formatted[index].BuildGeometry(origin); geometryBuilds++;
                if (glyph.CanFreeze) glyph.Freeze();
            }
            return layout.Positioned[index] = glyph;
        }

        internal object TextLayoutCacheForAudit() => new { geometryBuilds, shapeVariants = shapeVariants.Count,
            capVariants = capVariants.Count, hasAlternate = alternateShapes != null, hasLayout = renderLayout != null, currentText = shapingText };
        private LayoutInputs CurrentLayoutInputs() => new(shapingRevision, ActualWidth, ActualHeight, TextFontSize, LineAdvance,
            FitToBox, Ellipsis, CenterCapToBaseline, CenterSymbolsOnCapReference, CompactLineBox, UseGlyphGeometry, InkAlignment, InkVerticalAlignment);
        internal bool TextRenderInputsChanged()
        {
            EnsureShapingInputs();
            return renderLayout == null || renderLayout.Key != CurrentLayoutInputs();
        }
        internal void ResetTextLayoutCacheForAudit() => ClearTextLayoutCache();
        private void ClearTextLayoutCache()
        {
            shapeVariants.Clear(); capVariants.Clear(); alternateShapes = null; alternateText = null; renderLayout = null;
        }

        protected override Geometry? GetLayoutClip(Size layoutSlotSize) =>
            FitToBox && Effect is System.Windows.Media.Effects.DropShadowEffect ? null : base.GetLayoutClip(layoutSlotSize);

        protected override Size MeasureOverride(Size availableSize)
        {
        using var callbackTiming = new UiCallbackTiming("OpticallyCenteredText.Measure");
            double? trim = CompactLineBox && Ellipsis && double.IsFinite(availableSize.Width) && availableSize.Width > 0
                ? availableSize.Width : null;
            var shapes = GetShapes(TextFontSize, trim);
            double advance = LineAdvance > 0 ? LineAdvance : TextFontSize * 0.76;
            double width = 0;
            double height = Math.Max(TextFontSize * 1.18, advance * Math.Max(1, shapes.Lines.Length));
            foreach (var line in shapes.Lines)
            {
                Rect ink = line.Glyph.Bounds;
                if (!ink.IsEmpty) width = Math.Max(width, ink.Width);
                if(CompactLineBox&&shapes.Lines.Length==1&&!ink.IsEmpty)
                {
                    var cap=GetCaps(TextFontSize);
                    double top=cap.Glyph.Bounds.Top;
                    double overflow=Math.Max(0,Math.Max(top-ink.Top,ink.Bottom-cap.Formatted.Baseline));
                    // Keep cap-to-baseline centering and enough symmetric room for actual accents/descenders.
                    height=cap.Formatted.Baseline-top+2*overflow+2;
                }
            }
            width += 2;
            if (!double.IsInfinity(availableSize.Width)) width = Math.Min(width, availableSize.Width);
            if (!double.IsInfinity(availableSize.Height)) height = Math.Min(height, availableSize.Height);
            return new Size(Math.Max(0, width), Math.Max(0, height));
        }

        protected override void OnRender(DrawingContext drawingContext)
        {
        using var callbackTiming = new UiCallbackTiming("OpticallyCenteredText.Render");
            base.OnRender(drawingContext);
            if (!TryGetRenderLayout(out FormattedText[] formatted, out Rect inkBounds,
                    out double advance, out double offsetY)) return;
            var layout = renderLayout!;

            for (int index = 0; index < formatted.Length; index++)
            {
                var origin = layout.Origins[index];
                if(LabelTypography.GetTextEffect(this) is {} typography)
                {
                    var glyph=PositionedGlyph(layout,index);
                    double strength=typography.EffectFor(MusicFinish.IsLight);
                    string mode=typography.ModeFor(MusicFinish.IsLight);
                    if(mode=="Soft shadow"&&strength>0)
                    {
                        // Layered translucent silhouettes soften the shadow without a
                        // bitmap cache or moving the text's cap/baseline reference.
                        for(int layer=3;layer>=1;layer--)
                        {
                            drawingContext.PushTransform(new TranslateTransform(.45*layer*strength,.6*layer*strength));
                            drawingContext.DrawGeometry(Attenuate(new SolidColorBrush(Color.FromArgb((byte)(35*strength),0,0,0))),null,glyph);
                            drawingContext.Pop();
                        }
                    }
                    if(mode=="Outline"&&strength>0)
                    {
                        // Two contrast edges keep the contour visible on black powder and light metal alike.
                        drawingContext.DrawGeometry(null,new Pen(Attenuate(Brushes.WhiteSmoke),3*strength){LineJoin=PenLineJoin.Round},glyph);
                        var edge=Brushes.Black;
                        drawingContext.DrawGeometry(null,new Pen(Attenuate(edge),1.5*strength){LineJoin=PenLineJoin.Round},glyph);
                    }
                    drawingContext.DrawGeometry(Attenuate(TextBrush),null,glyph);
                }
                else if(SkinMetalText&&(SkinTextStyle??IconAppearance.Style)!="original")
                {
                    RenderOptions.SetBitmapScalingMode(this,BitmapScalingMode.HighQuality);
                    var glyph=PositionedGlyph(layout,index);
                    if(!glyph.Bounds.IsEmpty) SkinTextMaterial.Draw(drawingContext,this,glyph,SkinTextStyle??IconAppearance.Style);
                }                else if (InsetMetalText || LabelTypography.GetTextEffect(this)?.EffectFor(MusicFinish.IsLight)>0)
                {
                    var macroStyle=LabelTypography.GetTextEffect(this);
                    bool dark=macroStyle!=null?!MusicFinish.IsLight:DarkMetalInset;
                    var glyph = PositionedGlyph(layout,index);
                    drawingContext.DrawGeometry(Attenuate(new SolidColorBrush(dark?Color.FromRgb(202,206,208):InsetOnBrightMetal?Color.FromRgb(47,51,53):Color.FromRgb(225,231,233))),null,glyph);
                    // Bevel stays inside the live glyph silhouette; measured ink does not move.
                    drawingContext.PushClip(glyph);
                    var edge = dark
                        ?new LinearGradientBrush(Color.FromArgb(240,8,10,12),Color.FromArgb(70,12,15,17),90)
                        :new LinearGradientBrush(Color.FromArgb(210,12,15,17),Color.FromArgb(205,245,248,249),90);
                    double strength=macroStyle?.EffectFor(MusicFinish.IsLight)??(double.IsFinite(InsetStrength)?Math.Clamp(InsetStrength,0,2):1);
                    if(strength>0)drawingContext.DrawGeometry(null,new Pen(Attenuate(edge),(dark?2.4:1.15)*strength),glyph);
                    drawingContext.Pop();
                }
                else if(UseGlyphGeometry||IsNetworkRate(Text)) drawingContext.DrawGeometry(Attenuate(TextBrush),null,PositionedGlyph(layout,index));
                else
                {
                    var line=layout.Shapes.Lines[index];var brush=Attenuate(TextBrush);
                    if(!ReferenceEquals(line.Foreground,brush)){formatted[index].SetForegroundBrush(brush);line.Foreground=brush;}
                    drawingContext.DrawText(formatted[index], origin);
                }
            }
        }

        internal Rect GetRenderedInkBounds()
        {
            if (!TryGetRenderLayout(out FormattedText[] formatted, out _,
                    out double advance, out double offsetY)) return Rect.Empty;
            var layout = renderLayout!;
            if (layout.RenderedInkKnown) return layout.RenderedInk;

            Rect renderedInk = Rect.Empty;
            for (int index = 0; index < formatted.Length; index++)
            {
                Rect lineInk = layout.Shapes.Lines[index].Glyph.Bounds;
                if (lineInk.IsEmpty) continue;
                Rect positioned = PositionedGlyph(layout,index).Bounds;
                renderedInk.Union(positioned);
            }
            layout.RenderedInkKnown = true; layout.RenderedInk = renderedInk; return renderedInk;
        }

        internal Rect GetCenteringReferenceBounds()
        {
            if(!TryGetRenderLayout(out _,out var reference,out _,out double offset))return Rect.Empty;
            reference.Offset(0,offset);return reference;
        }

        public static readonly DependencyProperty FitToBoxProperty=DependencyProperty.Register(nameof(FitToBox),typeof(bool),typeof(OpticallyCenteredText),new FrameworkPropertyMetadata(false,FrameworkPropertyMetadataOptions.AffectsRender));
        public bool FitToBox {get=>(bool)GetValue(FitToBoxProperty);set=>SetValue(FitToBoxProperty,value);}

        private bool TryGetRenderLayout(out FormattedText[] formatted,
            out Rect inkBounds, out double advance, out double offsetY)
        {
            EnsureShapingInputs();
            string[] lines = shapingLines;
            formatted = Array.Empty<FormattedText>();
            inkBounds = Rect.Empty;
            advance = LineAdvance > 0 ? LineAdvance : TextFontSize * 0.76;
            offsetY = 0;
            if (lines.Length == 0 || ActualWidth <= 0 || ActualHeight <= 0) return false;
            var key = CurrentLayoutInputs();
            if (renderLayout is { } current && current.Key == key)
            {
                formatted = current.Shapes.Formatted; inkBounds = current.Reference;
                advance = current.Advance; offsetY = current.Offset; return true;
            }
            double renderedSize=TextFontSize;
            if(FitToBox&&lines.Length==1)
            {
                var natural=GetShapes(TextFontSize).Lines[0].Glyph.Bounds;
                if(!natural.IsEmpty&&natural.Width>0&&natural.Height>0)
                {
                    double fit=Math.Min(1,Math.Min(Math.Max(1,ActualWidth-8)/natural.Width,Math.Max(1,ActualHeight-8)/natural.Height));
                    renderedSize*=fit;advance*=fit;
                }
            }
            var shapes = GetShapes(renderedSize, Ellipsis ? ActualWidth : null);
            formatted = shapes.Formatted;
            for (int index = 0; index < lines.Length; index++)
            {
                Rect lineInk = shapes.Lines[index].Glyph.Bounds;
                if (index != 0 && !lineInk.IsEmpty)
                {
                    // Preserve the exact legacy multi-line outline calculation.
                    lineInk = formatted[index].BuildGeometry(new Point(0, index * advance)).Bounds; geometryBuilds++;
                }
                if (!lineInk.IsEmpty) inkBounds.Union(lineInk);
            }
            if (inkBounds.IsEmpty) return false;
            if (CenterCapToBaseline && (CenterSymbolsOnCapReference||(Text??string.Empty).Any(char.IsLetterOrDigit)) && !TextFontFamily.Source.Contains("MDL2",StringComparison.OrdinalIgnoreCase))
            {
                // A stable font reference: descenders in the displayed string do not move its baseline.
                var caps = GetCaps(renderedSize);
                double top = caps.Glyph.Bounds.Top;
                inkBounds = new Rect(inkBounds.X, top, inkBounds.Width,
                    caps.Formatted.Baseline + (lines.Length - 1) * advance - top);
            }
            offsetY = (InkVerticalAlignment==VerticalAlignment.Bottom?ActualHeight-inkBounds.Height:InkVerticalAlignment==VerticalAlignment.Top?0:(ActualHeight-inkBounds.Height)/2)-inkBounds.Y;
            var origins = new Point[shapes.Lines.Length];
            for(int index=0;index<origins.Length;index++)
                origins[index]=new Point(shapes.Lines[index].Glyph.Bounds.IsEmpty ? 0 : InkX(shapes.Lines[index].Glyph.Bounds), index * advance + offsetY);
            renderLayout = new RenderLayout(key, shapes, inkBounds, advance, offsetY, origins);
            return true;
        }
    }
}


