using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using SeedUi.ViewModels;

namespace SeedUi;

public partial class MainWindow : HandyControl.Controls.Window
{
    public MainWindow()
    {
        InitializeComponent();
        Icon = BitmapFrame.Create(new Uri("pack://application:,,,/Assets/app.ico", UriKind.Absolute));
        NavigateToPage(0);
    }

    private void NavigateToPage(int tabIndex)
    {
        if (NavConfig != null) NavConfig.IsChecked = tabIndex == 0;
        if (NavSeedAnalysis != null) NavSeedAnalysis.IsChecked = tabIndex == 1;
        if (NavResult != null) NavResult.IsChecked = tabIndex == 2;
        if (NavLogs != null) NavLogs.IsChecked = tabIndex == 3;
        if (NavArchive != null) NavArchive.IsChecked = tabIndex == 4;
        if (NavViewer != null) NavViewer.IsChecked = tabIndex == 5;
        if (NavEventPools != null) NavEventPools.IsChecked = tabIndex == 6;

        if (PageTitle != null)
        {
            PageTitle.Text = tabIndex switch
            {
                0 => "配置",
                1 => "种子分析",
                2 => "运行结果",
                3 => "运行日志",
                4 => "铺种",
                5 => "查看器",
                6 => "事件池信息",
                _ => PageTitle.Text
            };
        }

        if (PageConfigScroll != null) PageConfigScroll.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PageSeedAnalysisScroll != null) PageSeedAnalysisScroll.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PageResultScroll != null) PageResultScroll.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (PageLogsScroll != null) PageLogsScroll.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (PageArchiveScroll != null) PageArchiveScroll.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (PageViewerScroll != null) PageViewerScroll.Visibility = tabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (PageEventPoolsScroll != null) PageEventPoolsScroll.Visibility = tabIndex == 6 ? Visibility.Visible : Visibility.Collapsed;

        if (PageConfig != null) PageConfig.Visibility = tabIndex == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (PageSeedAnalysis != null) PageSeedAnalysis.Visibility = tabIndex == 1 ? Visibility.Visible : Visibility.Collapsed;
        if (PageResult != null) PageResult.Visibility = tabIndex == 2 ? Visibility.Visible : Visibility.Collapsed;
        if (PageLogs != null) PageLogs.Visibility = tabIndex == 3 ? Visibility.Visible : Visibility.Collapsed;
        if (PageArchive != null) PageArchive.Visibility = tabIndex == 4 ? Visibility.Visible : Visibility.Collapsed;
        if (PageViewer != null) PageViewer.Visibility = tabIndex == 5 ? Visibility.Visible : Visibility.Collapsed;
        if (PageEventPools != null) PageEventPools.Visibility = tabIndex == 6 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void ResultsListBox_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox)
        {
            return;
        }

        var scrollViewer = PageResultScroll;
        if (scrollViewer == null)
        {
            return;
        }

        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 3.0);
        e.Handled = true;
    }

    private void NavConfig_Checked(object sender, RoutedEventArgs e) => NavigateToPage(0);

    private void NavSeedAnalysis_Checked(object sender, RoutedEventArgs e) => NavigateToPage(1);

    private void NavResult_Checked(object sender, RoutedEventArgs e) => NavigateToPage(2);

    private void NavLogs_Checked(object sender, RoutedEventArgs e) => NavigateToPage(3);

    private void NavArchive_Checked(object sender, RoutedEventArgs e) => NavigateToPage(4);

    private void NavViewer_Checked(object sender, RoutedEventArgs e) => NavigateToPage(5);

    private void NavEventPools_Checked(object sender, RoutedEventArgs e) => NavigateToPage(6);

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
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "加载配置"
        };

        if (dialog.ShowDialog() == true && DataContext is MainWindowViewModel vm)
        {
            await vm.LoadConfigFromFileAsync(dialog.FileName);
        }
    }

    private async void OnSaveConfig(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.SaveFileDialog
        {
            Filter = "JSON 配置文件 (*.json)|*.json|所有文件 (*.*)|*.*",
            Title = "保存配置",
            FileName = "config.json"
        };

        if (dialog.ShowDialog() == true && DataContext is MainWindowViewModel vm)
        {
            await vm.SaveConfigToFileAsync(dialog.FileName);
        }
    }
}
