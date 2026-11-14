using System.Windows;
using System.Windows.Controls;
namespace Ekrano_valdymas;
public partial class MainWindow
{
    private void MenuTextSizeChanged(object sender,RoutedPropertyChangedEventArgs<double> e)
    {
        if(updatingMenuScale||programUsageTitle==null)return;
        if(ReferenceEquals(sender,menuTextSlider))menuTextPercent=e.NewValue;else usageTextPercent=e.NewValue;
        ApplyMenuTextSizes();
    }
    private void ResetMenuTextSizes(object sender,RoutedEventArgs e)
    {menuTextPercent=100;usageTextPercent=100;ApplyMenuTextSizes();SaveWindowSettings();}
    private void ApplyMenuTextSizes()
    {
        if(programUsageTitle==null)return;
        double m=1.56*menuTextPercent/100,u=1.13*usageTextPercent/100;
        Application.Current.Resources["DashboardMenuFontSize"]=19.6*m;
        Application.Current.Resources["MenuActionFontSize"]=20*m;
        Application.Current.Resources["MenuCoffeeFontSize"]=16*m;
        if(ContextMenu!=null)ContextMenu.FontSize=19.6*m;
        updatingMenuScale=true;menuTextSlider.Value=menuTextPercent;usageTextSlider.Value=usageTextPercent;updatingMenuScale=false;
        menuTextValue.Text=$"{menuTextPercent:0}%";usageTextValue.Text=$"{usageTextPercent:0}%";
        foreach(var button in fitMonitorButtonsGrid.Children.OfType<Button>())
            if(button.Content is StackPanel content)foreach(var label in content.Children.OfType<OpticallyCenteredText>())label.TextFontSize=18*m;
        programUsageTitle.FontSize=12*u;
        menuUsageViewport.Width=574*u;
        // Keep the popup/track anchor unchanged while the thumb is held.
        // Commit content-sized geometry after release (wheel/keyboard commit immediately).
        if(menuTextSlider.IsMouseCaptureWithin||usageTextSlider.IsMouseCaptureWithin)return;
        ApplyMenuContentGeometry(m,u);
    }
    private void ApplyMenuContentGeometry(double m,double u)
    {
        FitSubmenuRows();
        var buttons=fitMonitorButtonsGrid.Children.OfType<Button>().ToArray();
        double[] widths=buttons.Select(button=>
        {
            var content=(FrameworkElement)button.Content;
            content.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
            return Math.Ceiling(content.DesiredSize.Width+button.Padding.Left+button.Padding.Right+button.Margin.Left+button.Margin.Right+8);
        }).ToArray();
        double TextWidth(string text,double size)=>new System.Windows.Media.FormattedText(text,System.Globalization.CultureInfo.CurrentUICulture,FlowDirection.LeftToRight,
            new System.Windows.Media.Typeface(new System.Windows.Media.FontFamily("Segoe UI"),FontStyles.Normal,FontWeights.SemiBold,FontStretches.Normal),size,System.Windows.Media.Brushes.White,
            System.Windows.Media.VisualTreeHelper.GetDpi(this).PixelsPerDip).WidthIncludingTrailingWhitespace;
        double closeWidth=TextWidth("Close program",20*m)+48;
        double coffeeWidth=TextWidth("Buy me a coffee",16*m)+66;
        double usageWidth=574*u+16;
        double topMinimum=widths.Sum()+closeWidth;
        double width=Math.Ceiling(Math.Max(topMinimum,usageWidth+coffeeWidth));
        double extra=(width-topMinimum)/5;
        for(int i=0;i<4;i++)fitMonitorButtonsGrid.ColumnDefinitions[i].Width=new GridLength(widths[i]+extra);
        double actionsWidth=widths.Sum()+4*extra;
        fitMonitorButtonsGrid.Width=actionsWidth;
        menuHeaderGrid.ColumnDefinitions[0].Width=new GridLength(actionsWidth);
        menuHeaderGrid.ColumnDefinitions[1].Width=new GridLength(width-actionsWidth);
        menuHeaderGrid.Width=telemetryDetailsGrid.Width=width;
        telemetryDetailsGrid.ColumnDefinitions[0].Width=new GridLength(width-coffeeWidth);
        telemetryDetailsGrid.ColumnDefinitions[1].Width=new GridLength(coffeeWidth);
        double rowHeight=Math.Max(32,Math.Ceiling(28*m+6));

        foreach(var button in buttons)button.Height=rowHeight;
        closeMenuItem.Height=rowHeight;
        foreach(var button in telemetryDetailsGrid.Children.OfType<Button>())button.Height=Math.Max(38,28*m+6);
        programUsageTitle.Height=Math.Ceiling(18*u);
        menuUsageViewport.Height=Math.Ceiling(24*u);
    }
    private void FitSubmenuRows()
    {
        double font=19.6*1.56*menuTextPercent/100;
        var reference=new OpticallyCenteredText{Text="Ag",TextFontFamily=ContextMenu.FontFamily,TextFontWeight=FontWeights.SemiBold,TextFontSize=font,CompactLineBox=true};
        reference.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));
        double height=Math.Ceiling(reference.DesiredSize.Height+2);
        Application.Current.Resources["UnifiedMenuRowHeight"]=height;
        void Visit(ItemsControl owner)
        {
            foreach(var item in owner.Items.OfType<MenuItem>())
            {
                item.MinHeight=height;
                if(item.Header is string){item.Height=height;item.Margin=new Thickness(0);}
                else if(!ReferenceEquals(item.Header,menuHeaderGrid)&&!ReferenceEquals(item.Header,telemetryDetailsGrid))item.Height=height;
                if(item.Header is Grid actionRow&&actionRow.ColumnDefinitions.Count==2&&!ReferenceEquals(actionRow,menuHeaderGrid)&&!ReferenceEquals(actionRow,telemetryDetailsGrid))
                    foreach(var button in actionRow.Children.OfType<Button>())button.Height=height-2;
                if(item.Header is Grid grid&&grid.ColumnDefinitions.Count==3)
                {
                    double label=0,action=font*3.4+12,control=170;
                    foreach(var child in grid.Children.OfType<FrameworkElement>())
                    {
                        int col=Grid.GetColumn(child);
                        if(col==0){if(child is OpticallyCenteredText optical)optical.InkAlignment=HorizontalAlignment.Left;child.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));label=Math.Max(label,child.DesiredSize.Width+18);}
                        if(col==1&&child is Button button){button.Height=height-4;button.MinWidth=0;button.Width=double.NaN;button.Measure(new Size(double.PositiveInfinity,double.PositiveInfinity));control=Math.Max(control,button.DesiredSize.Width);}
                        if(child is Slider slider)slider.Height=height-4;
                        if(child is TextBox text)text.Height=height-4;
                    }
                    grid.Height=height;grid.Width=label+control+action;
                    grid.ColumnDefinitions[0].Width=new GridLength(label);grid.ColumnDefinitions[1].Width=new GridLength(control);grid.ColumnDefinitions[2].Width=new GridLength(action);
                }
                Visit(item);
            }
        }
        Visit(ContextMenu);
        foreach(var other in moduleOptionMenus.Values.Concat(new[]{shortcutOptions,weatherOptions,webOptions,sensorOptions}).Where(x=>x!=null).Distinct())Visit(other!);
        void Align(params Grid[] rows)
        {
            double[] widths=Enumerable.Range(0,3).Select(i=>rows.Max(r=>r.ColumnDefinitions[i].Width.Value)).ToArray();
            foreach(var row in rows){row.Width=widths.Sum();for(int i=0;i<3;i++)row.ColumnDefinitions[i].Width=new GridLength(widths[i]);}
        }
        Align(backgroundTransparencyRowGrid,opacityRowGrid,dimLevelRowGrid);
        Align(menuSizeRowGrid,menuTextRow,usageTextRow);
        var displayRows=monitorControlsMenuItem.Items.OfType<MenuItem>().Select(i=>i.Header).OfType<Grid>().Where(g=>g.ColumnDefinitions.Count==3).ToArray();
        if(displayRows.Length>0)Align(displayRows);
        ApplyWeatherEditorMetrics();
        appearanceSectionSeparator.Width=backgroundTransparencyRowGrid.Width;
    }}