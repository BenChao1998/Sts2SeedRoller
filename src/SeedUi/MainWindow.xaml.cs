using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using HandyControl.Controls;
using SeedUi.ViewModels;

namespace SeedUi;

public partial class MainWindow : HandyControl.Controls.Window
{
    public MainWindow()
    {
        InitializeComponent();
        // 初始化导航状态，修复首次打开时事件触发时机早于 InitializeComponent 的问题
        NavigateToPage(0);
    }

    private void NavigateToPage(int tabIndex)
    {
        // Update RadioButton selection states first
        if (NavConfig != null) NavConfig.IsChecked = tabIndex == 0;
        if (NavResult != null) NavResult.IsChecked = tabIndex == 1;
        if (NavLogs != null) NavLogs.IsChecked = tabIndex == 2;

        // Update page title
        if (PageTitle != null)
        {
            PageTitle.Text = tabIndex switch
            {
                0 => "配置",
                1 => "运行结果",
                2 => "运行日志",
                _ => PageTitle.Text
            };
        }

        // Update page visibility (both ScrollViewer and inner Grid)
        if (PageConfigScroll != null) PageConfigScroll.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PageResultScroll != null) PageResultScroll.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PageLogsScroll != null) PageLogsScroll.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (PageConfig != null) PageConfig.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PageResult != null) PageResult.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PageLogs != null) PageLogs.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
    }

    // 将 ListBox 的滚轮事件转发给外层 ScrollViewer，解决嵌套 ScrollViewer 滚轮冲突
    private void ResultsListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox) return;
        var scrollViewer = PageResultScroll;
        if (scrollViewer == null) return;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void NavConfig_Checked(object sender, RoutedEventArgs e)
    {
        NavigateToPage(0);
    }

    private void NavResult_Checked(object sender, RoutedEventArgs e)
    {
        NavigateToPage(1);
    }

    private void NavLogs_Checked(object sender, RoutedEventArgs e)
    {
        NavigateToPage(2);
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.NavigationRequested += OnNavigationRequested;
            if (vm.TryAutoLoadOnStartup)
            {
                vm.LoadDatasetCommand.Execute(null);
            }
        }
    }

    private void OnNavigationRequested(int pageIndex)
    {
        NavigateToPage(pageIndex);
    }

    private void OnResetConfig(object sender, RoutedEventArgs e)
    {
        if (DataContext is MainWindowViewModel vm)
        {
            vm.ResetConfig();
        }
    }

    private async void OnLoadConfig(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件|*.*",
            Title = "加载配置"
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.LoadConfigFromFileAsync(dialog.FileName);
            }
        }
    }

    private async void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件|*.*",
            Title = "保存配置",
            FileName = "config.json"
        };

        if (dialog.ShowDialog() == true)
        {
            if (DataContext is MainWindowViewModel vm)
            {
                await vm.SaveConfigToFileAsync(dialog.FileName);
            }
        }
    }
}
