using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;

namespace Ekrano_valdymas;
internal sealed class AboutWindow:SettingsWindow
{
 internal const string SupportEmail="blaki.support@gmail.com";
 internal string DisplayLanguage {get;private set;}
 public AboutWindow(string? language=null)
 {
  DisplayLanguage=language??LanguageSwitch.CurrentLanguage;
  Title="About BlakiDeck";Width=740;Height=700;ResizeMode=ResizeMode.CanResize;WindowStartupLocation=WindowStartupLocation.CenterOwner;ShowInTaskbar=false;
  WindowStyle=WindowStyle.None;AllowsTransparency=true;Background=Brushes.Transparent;Foreground=Brushes.White;FontFamily=new FontFamily("Segoe UI");FontSize=16;
  var panel=new DockPanel();
  System.Windows.Shell.WindowChrome.SetWindowChrome(this,new System.Windows.Shell.WindowChrome{CaptionHeight=0,ResizeBorderThickness=new Thickness(6),GlassFrameThickness=new Thickness(0),CornerRadius=new CornerRadius(14),UseAeroCaptionButtons=false});
  DialogWorkArea.Attach(this,540,360,deferDuringNativeMoveResize:true);
  Content=new Border{CornerRadius=new CornerRadius(14),Background=DocumentAppearance.Background,BorderBrush=DocumentAppearance.Border,BorderThickness=new Thickness(1),Child=panel};
  var header=new Border{Background=Brushes.Transparent,Height=78,Padding=new Thickness(24,18,24,12)};
  var headerContent=new DockPanel();header.Child=headerContent;DockPanel.SetDock(header,Dock.Top);panel.Children.Add(header);
  var close=MacroUi.Button("Close",(_,_)=>Close());close.Width=100;close.Height=44;close.Margin=new Thickness(16,0,0,0);close.IsCancel=true;DockPanel.SetDock(close,Dock.Right);headerContent.Children.Add(close);
  var choice=new LanguageSwitch(DisplayLanguage){VerticalAlignment=VerticalAlignment.Center};DockPanel.SetDock(choice,Dock.Right);headerContent.Children.Add(choice);
  var title=new OpticallyCenteredText{Text="BlakiDeck",TextFontFamily=new FontFamily("Segoe UI"),TextFontSize=28,TextFontWeight=FontWeights.SemiBold,TextBrush=Brushes.White,Height=42,InkAlignment=HorizontalAlignment.Left};headerContent.Children.Add(title);
  var body=new StackPanel();
  var version=new TextBlock{Name="AboutVersion",FontSize=14,Foreground=Brushes.LightGray,Margin=new Thickness(0,0,0,16)};body.Children.Add(version);
  var legalSummary=new TextBlock{TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,16,0,8)};
  var legalLinks=new WrapPanel{Margin=new Thickness(0,0,0,8)};
  var license=MacroUi.Button("License",(_,_)=>new HelpWindow("License"){Owner=this}.ShowDialog());
  var privacy=MacroUi.Button("Privacy",(_,_)=>new HelpWindow("Privacy"){Owner=this}.ShowDialog());
  var notices=MacroUi.Button("Third-party notices",(_,_)=>new HelpWindow("Notices"){Owner=this}.ShowDialog());
  foreach(var button in new[]{license,privacy,notices}){button.Margin=new Thickness(0,0,8,6);legalLinks.Children.Add(button);}
  var paragraphs=new[]{
    "BlakiDeck is a modular desktop dashboard built around tabs and active workspaces. Choose from one to eleven individually configurable tabs with modules, or replace a tab slot with a shortcut, sensor, macro or clock. Designed for a touchscreen monitor, it works just as well with a mouse.",
    "The dashboard keeps its proportions as you resize it. Use the main dashboard on its own, collapse it to show only tabs and shortcuts, combine it with the browser, or use the browser alone. You can position it at the top or bottom of your monitor.",
    "The main dashboard has three sections. Choose a module for each section. Shortcuts and System Monitor also offer layouts spanning two or three sections. A wide music player layout is available with several themed skins.",
    "Available modules include Windows shortcuts, web shortcuts, Airthings air-quality monitoring (dedicated Airthings hardware is required), OBS controls, Philips Hue controls (a Hue Bridge is required), a music player for local files or streaming through supported music applications, modular system monitoring, macros and weather.",
    "Customize tab layouts and skins, clock styles, module fonts, text colors and effects. Some settings are shared across the application. Display controls offer brightness and color-temperature adjustments where supported by the monitor and driver, as well as wallpaper selection. The player also lets you customize the lighting of its buttons, volume knob and power button.",
    "The idea for BlakiDeck came to me when I tried Codex, an AI-powered programming tool. I started building something that felt more convenient to use while working, gaming or listening to music. Over time, it grew into a fairly substantial application: I kept wanting more features, more convenience, better looks and greater flexibility. My programming knowledge is limited to the basics, so please do not expect professional expertise, although I have paid a lot of attention to the small details :)",
    "Created by Blakiukas."
  };
  var prose=new TextBox{Name="AboutSelectableText",IsReadOnly=true,TextWrapping=TextWrapping.Wrap,AcceptsReturn=true,BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=Brushes.White,Margin=new Thickness(0,8,0,8),VerticalScrollBarVisibility=ScrollBarVisibility.Disabled,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled};body.Children.Add(prose);
  var related=new TextBlock{Name="AboutBlakiBridge",TextWrapping=TextWrapping.Wrap,Margin=new Thickness(0,8,0,16)};
  var relatedDescription=new System.Windows.Documents.Run();related.Inlines.Add(relatedDescription);
  var bridgeLink=new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("https://blakiukas.itch.io/blakibridge")){NavigateUri=new Uri("https://blakiukas.itch.io/blakibridge"),Foreground=Brushes.LightSkyBlue};
  bridgeLink.RequestNavigate+=(_,e)=>{try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(e.Uri.AbsoluteUri){UseShellExecute=true});}catch{MessageBox.Show(this,DisplayLanguage=="lt"?"Nepavyko atidaryti nuorodos. Adresas: https://blakiukas.itch.io/blakibridge":"Could not open the link. Address: https://blakiukas.itch.io/blakibridge",Title);}e.Handled=true;};
  related.Inlines.Add(bridgeLink);body.Children.Add(related);
  var support=new StackPanel{Margin=new Thickness(14)};
  support.Children.Add(new TextBox{Text="Questions, problems or suggestions? Get in touch — I'll do my best to help.",TextWrapping=TextWrapping.Wrap,IsReadOnly=true,BorderThickness=new Thickness(0),Padding=new Thickness(0),Background=Brushes.Transparent,Foreground=Brushes.White});
  var line=new DockPanel{Margin=new Thickness(0,12,0,0)};
  var copy=MacroUi.Button("Copy",(_,_)=>{});copy.Width=90;copy.MinHeight=34;copy.Margin=new Thickness(12,0,0,0);copy.Click+=(_,_)=>{try{Clipboard.SetText(SupportEmail);copy.Content=DisplayLanguage=="lt"?"Nukopijuota":"Copied";}catch{copy.Content=DisplayLanguage=="lt"?"Kartoti":"Try again";}};DockPanel.SetDock(copy,Dock.Right);line.Children.Add(copy);
  line.Children.Add(new TextBox{Text=SupportEmail,IsReadOnly=true,BorderThickness=new Thickness(0),Background=Brushes.Transparent,Foreground=Brushes.White,VerticalContentAlignment=VerticalAlignment.Center,FontSize=16});support.Children.Add(line);
  var supportFrame=new Border{Background=Brushes.Transparent,BorderThickness=new Thickness(1),CornerRadius=new CornerRadius(6),Child=support};
  supportFrame.SetResourceReference(Border.BorderBrushProperty,"ActionAccentBrush");
  body.Children.Add(supportFrame);
  body.Children.Add(legalSummary);
  body.Children.Add(legalLinks);
  void Translate()
  {
   DisplayLanguage=choice.SelectedLanguage;bool lt=DisplayLanguage=="lt";
   Title=lt?"Apie BlakiDeck":"About BlakiDeck";title.Text=Title;close.Content=lt?"Uždaryti":"Close";
   version.Text=(lt?"Versija ":"Version ")+typeof(AboutWindow).Assembly.GetName().Version?.ToString(3);
   legalSummary.Text=lt?"Nemokama asmeniniam naudojimui ir verslui. Atviro kodo programa pagal MIT licenciją. Parama savanoriška; visos funkcijos prieinamos nemokamai.":"Free for personal and business use. Open-source application under the MIT License. Donations are optional; all features are available free of charge.";
   license.Content=lt?"Licencija":"License";privacy.Content=lt?"Privatumas":"Privacy";notices.Content=lt?"Komponentų licencijos":"Third-party notices";
   relatedDescription.Text=lt?"Kita mano programa – BlakiBridge. Ji padeda teisingai perkelti pelės žymeklį tarp skirtingo dydžio ir raiškos monitorių.\n":"My other application, BlakiBridge, helps the mouse pointer move correctly between monitors of different sizes and resolutions.\n";
   prose.Text=string.Join(Environment.NewLine+Environment.NewLine,lt?AboutText.Lithuanian:paragraphs);
   ((TextBox)support.Children[0]).Text=lt?AboutText.LithuanianSupport:"Questions, problems or suggestions? Get in touch — I'll do my best to help.";
   copy.Content=lt?"Kopijuoti":"Copy";
   if(IsLoaded)Dispatcher.BeginInvoke(new Action(FitContent),System.Windows.Threading.DispatcherPriority.Loaded);
  }
  choice.SelectionChanged+=(_,_)=>Translate();Translate();
  panel.Children.Add(new ScrollViewer{Content=body,Margin=new Thickness(24,0,24,24),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,PanningMode=PanningMode.VerticalOnly});
  bool OnClose(DependencyObject? source)
  {
   for(var node=source;node!=null&&node!=header;node=VisualTreeHelper.GetParent(node))
    if(node is System.Windows.Controls.Primitives.ButtonBase or ComboBox)return true;
   return false;
  }
  header.MouseLeftButtonDown+=(_,e)=>{if(e.StylusDevice==null&&!OnClose(e.OriginalSource as DependencyObject)&&e.LeftButton==MouseButtonState.Pressed){e.Handled=true;DragMove();}};
  TouchDevice? draggingTouch=null;Point previousTouch=default;
  header.TouchDown+=(_,e)=>{
   if(draggingTouch!=null||OnClose(e.OriginalSource as DependencyObject))return;
   previousTouch=header.PointToScreen(e.GetTouchPoint(header).Position);
   if(e.TouchDevice.Capture(header))draggingTouch=e.TouchDevice;
   e.Handled=true;
  };
  header.TouchMove+=(_,e)=>{
   if(e.TouchDevice!=draggingTouch)return;
   var current=header.PointToScreen(e.GetTouchPoint(header).Position);var dpi=VisualTreeHelper.GetDpi(this);
   Left+=(current.X-previousTouch.X)/dpi.DpiScaleX;Top+=(current.Y-previousTouch.Y)/dpi.DpiScaleY;
   previousTouch=current;e.Handled=true;
  };
  header.TouchUp+=(_,e)=>{if(e.TouchDevice!=draggingTouch)return;draggingTouch=null;e.TouchDevice.Capture(null);e.Handled=true;};
  header.LostTouchCapture+=(_,e)=>{if(e.TouchDevice==draggingTouch)draggingTouch=null;};
  void FitContent(){
   // Open tall enough for the actual wrapped text, bounded by this monitor's work area.
   // Keep subsequent manual resizing independent of content size.
   body.Measure(new Size(Math.Max(1,ActualWidth-50),double.PositiveInfinity));
   double bottom=Top+ActualHeight;
   Height=Math.Clamp(Math.Ceiling(body.DesiredSize.Height)+108,MinHeight,MaxHeight);
   Top=bottom-Height;
  }
  ContentRendered+=(_,_)=>Dispatcher.BeginInvoke(new Action(FitContent),System.Windows.Threading.DispatcherPriority.Loaded);
 }
}
