using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Threading;
using AetherLauncher.Controls;
using AetherLauncher.Services;
using AetherLauncher.ViewModels;

namespace AetherLauncher;

public partial class MainWindow : Window
{
    
    private static readonly string[] FooterNotes =
    {
        "Games, launch keys and settings stay on this PC - nothing is uploaded.",
        "Roblox's own logs and cookie store are read only. Launch keys are stored DPAPI-encrypted.",
        "Flags only ever touch the ClientSettings folder of the installed client. Roblox ignores flags outside its allowlist.",
        "Everything lives in %LOCALAPPDATA%\\AetherLauncher. No account data leaves this machine.",
        "Auth tickets and cookies are masked in this log before a line is written.",
    };

    private readonly MainViewModel _viewModel;

    public MainWindow(string? launchUri)
    {
        InitializeComponent();

        _viewModel = new MainViewModel();
        DataContext = _viewModel;

        ApplyCursor();
        PreviewKeyDown += OnPreviewKeyDown;
        _viewModel.PropertyChanged += OnViewModelPropertyChanged;

        
        _viewModel.Places.CollectionChanged += (_, e) => PopNewRow(GamesList, e);
        _viewModel.Recents.CollectionChanged += (_, e) => PopNewRow(RecentsList, e);
        _viewModel.Accounts.CollectionChanged += (_, e) => PopNewRow(AccountsList, e);

        SourceInitialized += (_, _) =>
        {
            TryRoundWindowCorners();

            
            
            var handle = new WindowInteropHelper(this).Handle;
            HwndSource.FromHwnd(handle)?.AddHook(WindowProc);
        };
        App.ExternalLaunchRequested += OnExternalLaunchRequested;
        Log.Lines.CollectionChanged += OnLogLinesChanged;

        Loaded += async (_, _) =>
        {
            MoveNavGlide(animate: false);

            
            Anim.Fade(Shell, 0, 1, 300);
            Anim.Slide(Shell, 10, 0, 340, 0, _viewModel.Settings.PlayfulMode);

            try
            {
                await _viewModel.InitializeAsync(launchUri);
            }
            catch (Exception ex)
            {
                Log.Error($"Startup failed: {ex.Message}");
                _viewModel.Status = "Startup failed - see the log.";
            }
        };

        Closing += (_, _) =>
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            App.ExternalLaunchRequested -= OnExternalLaunchRequested;
            Log.Lines.CollectionChanged -= OnLogLinesChanged;
            _viewModel.Dispose();
        };

        NavHome.IsChecked = true; 
    }

    
    internal void SelectPage(int index)
    {
        var tab = index switch
        {
            0 => NavHome,
            1 => NavAccounts,
            2 => NavEngine,
            3 => NavSettings,
            4 => NavLog,
            _ => null,
        };

        if (tab is not null) tab.IsChecked = true;
    }

    
    internal double NavUnderlineX => NavGlideShift.X;

    
    internal void ScrollSettingsToEnd() => PageSettings.ScrollToEnd();

    private void NavTab_Checked(object sender, RoutedEventArgs e)
    {
        if (sender is not RadioButton tab || tab.Tag is not string tag || !int.TryParse(tag, out var index)) return;
        if (_viewModel is null || PageHome is null) return; 

        FrameworkElement[] pages = { PageHome, PageAccounts, PageEngine, PageSettings, PageLog };
        for (var page = 0; page < pages.Length; page++)
            pages[page].Visibility = page == index ? Visibility.Visible : Visibility.Collapsed;

        if (index >= 0 && index < FooterNotes.Length) _viewModel.FooterNote = FooterNotes[index];

        
        Dispatcher.BeginInvoke(() => MoveNavGlide(animate: IsLoaded), DispatcherPriority.Loaded);

        if (index >= 0 && index < pages.Length)
        {
            
            Anim.SlideX(pages[index], 14, 0, 320, 0, _viewModel.Settings.PlayfulMode);
            Anim.PageEntrance(pages[index], _viewModel.Settings.PlayfulMode);
        }
    }

    
    private void MoveNavGlide(bool animate)
    {
        var tab = NavHome.IsChecked == true ? NavHome
            : NavAccounts.IsChecked == true ? NavAccounts
            : NavEngine.IsChecked == true ? NavEngine
            : NavSettings.IsChecked == true ? NavSettings
            : NavLog;

        if (tab.ActualWidth <= 0 || NavBar.ActualWidth <= 0) return;

        double target;
        try
        {
            var offset = tab.TransformToAncestor(NavBar).Transform(new Point(0, 0));
            target = offset.X + ((tab.ActualWidth - NavGlide.Width) / 2);
        }
        catch (InvalidOperationException)
        {
            return; 
        }

        NavGlide.Opacity = 1;
        if (!animate)
        {
            NavGlideShift.BeginAnimation(TranslateTransform.XProperty, null);
            NavGlideShift.X = target;
            return;
        }

        var from = NavGlideShift.HasAnimatedProperties ? NavGlideShift.X : target;
        Anim.Glide(NavGlide, from, target, 360, _viewModel.Settings.PlayfulMode);
    }

    
    private void HeroCard_MouseEnter(object sender, MouseEventArgs e)
    {
        if (!_viewModel.Settings.PlayfulMode || HeroCard.ActualWidth <= 0) return;

        var width = HeroShine.Width;
        HeroShineShift.BeginAnimation(TranslateTransform.XProperty, null);
        HeroShineShift.X = -width;
        Anim.Fade(HeroShine, 0, 0.9, 140);

        var sweep = new DoubleAnimation(HeroCard.ActualWidth, TimeSpan.FromMilliseconds(720))
        {
            From = -width,
            EasingFunction = Anim.Out(),
        };

        sweep.Completed += (_, _) => Anim.Fade(HeroShine, 0.9, 0, 260);
        HeroShineShift.BeginAnimation(TranslateTransform.XProperty, sweep);
    }

    
    private void ApplyCursor()
    {
        try
        {
            if (!_viewModel.Settings.CustomCursor)
            {
                Cursor = Cursors.Arrow;
                return;
            }

            var resource = Application.GetResourceStream(new Uri("pack://application:,,,/Assets/cursor.cur"));
            if (resource is null)
            {
                Log.Warn("The cursor asset was not found - the Windows pointer stays.");
                return;
            }

            using var stream = resource.Stream;
            Cursor = new Cursor(stream);
        }
        catch (Exception ex)
        {
            Log.Warn($"The custom pointer could not be loaded: {ex.Message}");
        }
    }

    
    
    
    
    

    private Point _placeDragStart;
    private PlaceItem? _placeDragItem;

    private void GamesList_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        _placeDragStart = e.GetPosition(GamesList);
        _placeDragItem = RowAt(_placeDragStart)?.Item;
    }

    private void GamesList_MouseMove(object sender, MouseEventArgs e)
    {
        if (_placeDragItem is null || e.LeftButton != MouseButtonState.Pressed) return;

        var position = e.GetPosition(GamesList);

        
        if (Math.Abs(position.Y - _placeDragStart.Y) < 8) return;

        DragDrop.DoDragDrop(GamesList, _placeDragItem, DragDropEffects.Move);
        _placeDragItem = null;
    }

    private void GamesList_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(PlaceItem)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void GamesList_Drop(object sender, DragEventArgs e)
    {
        if (_viewModel is null || e.Data.GetData(typeof(PlaceItem)) is not PlaceItem item) return;

        if (RowAt(e.GetPosition(GamesList)) is not { } target || ReferenceEquals(target.Item, item)) return;

        _viewModel.MovePlace(item, target.Item, before: !target.LowerHalf);
        e.Handled = true;

        
        Dispatcher.BeginInvoke(() =>
        {
            if (GamesList.ItemContainerGenerator.ContainerFromItem(item) is FrameworkElement moved)
                Anim.Pulse(moved, 1.03, 260);
        }, DispatcherPriority.Loaded);
    }

    
    private (PlaceItem Item, bool LowerHalf)? RowAt(Point position)
    {
        foreach (var entry in GamesList.Items)
        {
            if (entry is not PlaceItem item) continue;
            if (GamesList.ItemContainerGenerator.ContainerFromItem(item) is not ListBoxItem row) continue;

            var top = row.TranslatePoint(new Point(0, 0), GamesList).Y;
            if (position.Y < top || position.Y > top + row.ActualHeight) continue;

            return (item, position.Y > top + (row.ActualHeight / 2));
        }

        return null;
    }

    private void PopNewRow(ItemsControl list, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action != NotifyCollectionChangedAction.Add || e.NewItems is not { Count: 1 } items) return;

        var item = items[0];

        
        Dispatcher.BeginInvoke(() =>
        {
            if (list.ItemContainerGenerator.ContainerFromItem(item) is not FrameworkElement row) return;
            Anim.Pop(row, open: true, milliseconds: _viewModel.Settings.PlayfulMode ? 300 : 200);
        }, DispatcherPriority.Loaded);
    }

    private void OnViewModelPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        switch (e.PropertyName)
        {
            case nameof(MainViewModel.Status):
                
                Anim.Fade(StatusText, 0.35, 1, 240);
                Anim.SlideX(StatusText, 6, 0, 240);
                break;

            case nameof(MainViewModel.HeroTitle):
                
                if (_viewModel.Settings.PlayfulMode) Anim.Pulse(HeroIcon, 1.07, 320);
                Anim.Fade(HeroName, 0.2, 1, 260);
                Anim.SlideX(HeroName, 7, 0, 280, 0, _viewModel.Settings.PlayfulMode);
                Anim.Fade(HeroSubLine, 0.2, 1, 300);
                break;

            case nameof(MainViewModel.CustomCursor):
                ApplyCursor();
                break;

            case nameof(MainViewModel.LayoutOpen):
                ShowLayout(_viewModel.LayoutOpen);
                break;

            case nameof(MainViewModel.AddAccountOpen):
                
                
                if (!_viewModel.AddAccountOpen) CookieBox.Password = "";
                break;
        }
    }

    
    private void ShowLayout(bool open)
    {
        if (open)
        {
            LayoutOverlay.Visibility = Visibility.Visible;
            Anim.Fade(LayoutOverlay, 0, 1, 190);
            Anim.Pop(LayoutCard, open: true, milliseconds: 300);
            return;
        }

        Anim.Pop(LayoutCard, open: false, milliseconds: 170);

        var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190))
        {
            From = 1,
            EasingFunction = Anim.Out(),
        };

        
        
        fade.Completed += (_, _) =>
        {
            if (!_viewModel.LayoutOpen) LayoutOverlay.Visibility = Visibility.Collapsed;
        };

        LayoutOverlay.BeginAnimation(UIElement.OpacityProperty, fade);
    }

    private void LayoutOverlay_Click(object sender, MouseButtonEventArgs e)
    {
        if (_viewModel.ShortcutCapture)
        {
            _viewModel.CancelShortcutCapture();
            return;
        }

        _viewModel.CloseLayoutCommand.Execute(null);
    }

    private void LayoutCard_Click(object sender, MouseButtonEventArgs e) => e.Handled = true;

    
    
    
    
    private void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var pressed = e.Key == Key.System ? e.SystemKey : e.Key;

        
        if (_viewModel.ShortcutCapture)
        {
            if (pressed == Key.Escape)
            {
                _viewModel.CancelShortcutCapture();
                e.Handled = true;
                return;
            }

            
            if (Shortcuts.IsModifierKey(pressed))
            {
                e.Handled = true;
                return;
            }

            
            
            _viewModel.TryApplyShortcut(Keyboard.Modifiers, pressed);
            e.Handled = true;
            return;
        }

        if (Shortcuts.Matches(_viewModel.Settings.OverlayShortcut, Keyboard.Modifiers, pressed))
        {
            
            
            if (Keyboard.FocusedElement is TextBox && Keyboard.Modifiers == ModifierKeys.None)
            {
                
            }
            else
            {
                if (_viewModel.Settings.OverlayEnabled) _viewModel.OpenLayoutCommand.Execute(null);
                e.Handled = true;
                return;
            }
        }

        if (pressed == Key.Escape && _viewModel.LayoutOpen)
        {
            _viewModel.CloseLayoutCommand.Execute(null);
            e.Handled = true;
            return;
        }

        var control = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;

        if (control && e.Key is >= Key.D1 and <= Key.D5)
        {
            SelectPage(e.Key - Key.D1);
            e.Handled = true;
            return;
        }

        if (control && e.Key == Key.Enter)
        {
            if (_viewModel.LaunchCommand.CanExecute(null)) _viewModel.LaunchCommand.Execute(null);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.F5 && _viewModel.RefreshInstallCommand.CanExecute(null))
        {
            _viewModel.RefreshInstallCommand.Execute(null);
            e.Handled = true;
        }
    }

    private void MinButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void MaxButton_Click(object sender, RoutedEventArgs e)
        => WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

    
    private async void OnExternalLaunchRequested(string uri)
    {
        try
        {
            if (string.Equals(uri, SingleInstance.ShowRequest, StringComparison.Ordinal))
            {
                BringToFront();
                return;
            }

            if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
            Show();
            Activate();
            await _viewModel.HandleExternalLaunchAsync(uri);
        }
        catch (Exception ex)
        {
            Log.Error($"External launch failed: {ex.Message}");
        }
    }

    
    private void BringToFront()
    {
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;

        Show();
        Activate();

        var handle = new WindowInteropHelper(this).Handle;
        if (handle != IntPtr.Zero) SetForegroundWindow(handle);
    }

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr window);

    private void OnLogLinesChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.Action is NotifyCollectionChangedAction.Add) LogBox.ScrollToEnd();
    }

    private void CookieBox_PasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox box) _viewModel.NewCookie = box.Password;
    }

    private async void SignInWithRoblox_Click(object sender, RoutedEventArgs e)
    {
        await ShowRobloxLoginAsync();
    }

    private async void AccountLogin_Click(object sender, RoutedEventArgs e)
    {
        await ShowRobloxLoginAsync();
    }

    
    
    
    
    private async Task ShowRobloxLoginAsync()
    {
        if (_viewModel.Busy) return;

        try
        {
            var login = new RobloxLoginWindow { Owner = this };
            var accepted = login.ShowDialog();

            if (accepted == true && !string.IsNullOrWhiteSpace(login.Cookie))
            {
                await _viewModel.AddAccountWithCookieAsync(login.Cookie);
                return;
            }

            
            
            _viewModel.AddAccountOpen = true;
        }
        catch (Exception ex)
        {
            _viewModel.AddAccountError = $"Roblox sign-in could not be opened: {ex.Message}";
            Log.Error($"Roblox sign-in could not be opened: {ex.Message}");
        }
    }

    
    private void TryRoundWindowCorners()
    {
        try
        {
            var handle = new WindowInteropHelper(this).Handle;
            var preference = 2; 
            DwmSetWindowAttribute(handle, 33 , ref preference, sizeof(int));
        }
        catch
        {
            
        }
    }

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr window, int attribute, ref int value, int size);

    private const int WmGetMinMaxInfo = 0x0024;

    private static IntPtr WindowProc(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (message != WmGetMinMaxInfo) return IntPtr.Zero;

        var monitor = MonitorFromWindow(hwnd, MonitorDefaultToNearest);
        if (monitor == IntPtr.Zero) return IntPtr.Zero;

        var monitorInfo = new MonitorInfo { Size = Marshal.SizeOf<MonitorInfo>() };
        if (!GetMonitorInfo(monitor, ref monitorInfo)) return IntPtr.Zero;

        var info = Marshal.PtrToStructure<MinMaxInfo>(lParam);
        var work = monitorInfo.WorkArea;
        var screen = monitorInfo.Monitor;

        info.MaxPosition.X = work.Left - screen.Left;
        info.MaxPosition.Y = work.Top - screen.Top;
        info.MaxSize.X = work.Right - work.Left;
        info.MaxSize.Y = work.Bottom - work.Top;

        Marshal.StructureToPtr(info, lParam, fDeleteOld: true);
        handled = true;
        return IntPtr.Zero;
    }

    private const int MonitorDefaultToNearest = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MinMaxInfo
    {
        public NativePoint Reserved;
        public NativePoint MaxSize;
        public NativePoint MaxPosition;
        public NativePoint MinTrackSize;
        public NativePoint MaxTrackSize;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MonitorInfo
    {
        public int Size;
        public NativeRect Monitor;
        public NativeRect WorkArea;
        public int Flags;
    }

    [DllImport("user32.dll")]
    private static extern IntPtr MonitorFromWindow(IntPtr window, int flags);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern bool GetMonitorInfo(IntPtr monitor, ref MonitorInfo info);
}
