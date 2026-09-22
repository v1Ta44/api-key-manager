using System.Windows;
using ApiKeyManager.Wpf.Services;

namespace ApiKeyManager.Wpf.Views;

public partial class ImportModeDialog : Window
{
    public ImportChoice Choice { get; private set; } = ImportChoice.Cancel;

    public ImportModeDialog(int incomingCount, int currentCount)
    {
        InitializeComponent();

        TxtSummary.Text = $"备份含 {incomingCount} 条，当前库 {currentCount} 条";

        BtnOk.Click += (_, _) =>
        {
            Choice = OptReplace.IsChecked == true ? ImportChoice.Replace
                   : OptMerge.IsChecked == true ? ImportChoice.Merge
                   : ImportChoice.Cancel;
            DialogResult = true;
        };

        // 双击卡片直接确认，减少一步点击
        OptReplace.MouseDoubleClick += (_, _) => { OptReplace.IsChecked = true; BtnOk.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };
        OptMerge.MouseDoubleClick += (_, _) => { OptMerge.IsChecked = true; BtnOk.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent)); };

        Loaded += (_, _) => OptMerge.Focus();
    }
}
