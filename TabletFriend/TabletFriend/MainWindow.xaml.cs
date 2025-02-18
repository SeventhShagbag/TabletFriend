using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using TabletFriend.Docking;
using TabletFriend.TabletMode;
using WpfAppBar;
using System.Windows.Forms;
using WinFormsApp = System.Windows.Forms.Application; 

namespace TabletFriend
{
    public partial class MainWindow : Window, INotifyPropertyChanged
    {
        private LayoutManager _layout;
        private ThemeManager _theme;
        private LayoutListManager _layoutList;
        private ThemeListManager _themeList;
        private AutomaticLayoutSwitcher _layoutSwitcher;
        private TrayManager _tray;
        private FileManager _file;

        public event PropertyChangedEventHandler PropertyChanged;

		private void DebugMonitorInfo()
		{
			var screens = System.Windows.Forms.Screen.AllScreens;
			for (int i = 0; i < screens.Length; i++)
			{
				var screen = screens[i];
				Console.WriteLine($"Monitor {i}: {screen.DeviceName}, WorkingArea: {screen.WorkingArea}");
			}
		}

        private void OnPropertyChanged(string property)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
        }

		private void MoveToMonitor(int monitorIndex)
		{
			var screens = System.Windows.Forms.Screen.AllScreens;
			if (monitorIndex >= 0 && monitorIndex < screens.Length)
			{
				var targetScreen = screens[monitorIndex];
				var workingArea = targetScreen.WorkingArea;

				Console.WriteLine($"Moving to Monitor {monitorIndex}: {targetScreen.DeviceName}, WorkingArea: {workingArea}");

				// Adjust window position
				this.Left = workingArea.Left + 100;  // Adjust position inside the screen
				this.Top = workingArea.Top + 100;
			}
			else
			{
				Console.WriteLine("Invalid monitor index.");
			}
		}

        public MainWindow()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            System.Diagnostics.PresentationTraceSources.DataBindingSource.Switch.Level = System.Diagnostics.SourceLevels.Critical;

            var focusMonitor = new AppFocusMonitor();
            SystemEvents.DisplaySettingsChanged += OnSizeChanged;
            Directory.SetCurrentDirectory(AppState.CurrentDirectory);

            InitializeComponent();
            
            // Set initial window size
            this.Width = 300;
            this.Height = 600;
            
            Topmost = true;
            MouseDown += OnMouseDown;

            this.Loaded += (s, e) => 
            {
                if (AppState.Settings.DockingMode != DockingMode.None)
                {
                    System.Windows.Application.Current.Dispatcher.BeginInvoke(
                        new Action(() => OnDockingChanged(new object[] { AppState.Settings.DockingMode })),
                        System.Windows.Threading.DispatcherPriority.Loaded
                    );
                }
            };

            _file = new FileManager();
            ToggleManager.Init();

            _theme = new ThemeManager();
            _layout = new LayoutManager();
            Settings.Load();

            Installer.TryInstall();
            _ = UpdateChecker.Check();

            _layoutList = new LayoutListManager();
            _themeList = new ThemeListManager();
            ContextMenu = new System.Windows.Controls.ContextMenu();

            OnUpdateLayoutList();

            // Only use layout switching, not docking
            _layoutSwitcher = new AutomaticLayoutSwitcher(focusMonitor);
            _tray = new TrayManager(this, _layoutList, _themeList, focusMonitor);

            if (AppState.Settings.AddToAutostart)
            {
                AutostartManager.SetAutostart();
            }
            else
            {
                AutostartManager.ResetAutostart();
            }

            EventBeacon.Subscribe(Events.ToggleMinimize, OnToggleMinimize);
            EventBeacon.Subscribe(Events.Maximize, OnMaximize);
            EventBeacon.Subscribe(Events.Minimize, OnMinimize);
            EventBeacon.Subscribe(Events.UpdateLayoutList, OnUpdateLayoutList);
            EventBeacon.Subscribe(Events.ChangeLayout, OnUpdateLayoutList);
            EventBeacon.Subscribe(Events.DockingChanged, OnDockingChanged);
            EventBeacon.Subscribe(Events.LayoutChanged, OnLayoutChanged);
        }

        private void OnSizeChanged(object sender, EventArgs eventArgs)
        {
            UiFactory.CreateUi(AppState.CurrentLayout, this);
        }

        private double _maxOpacity;
        public double MaxOpacity
        {
            get => _maxOpacity;
            set
            {
                _maxOpacity = value;
                OnPropertyChanged(nameof(MaxOpacity));
            }
        }

        private double _minOpacity;
        public double MinOpacity
        {
            get => _minOpacity;
            set
            {
                _minOpacity = value;
                OnPropertyChanged(nameof(MinOpacity));
            }
        }

        private void OnUpdateLayoutList(object[] obj = null)
        {
            System.Windows.Application.Current.Dispatcher.Invoke(
                () =>
                {
                    ContextMenu.Items.Clear();
                    DockingMenuFactory.CreateDockingMenu(ContextMenu);

                    ContextMenu.Items.Add(new Separator());
                    var items = _layoutList.GetClonedItems();
                    foreach (var item in items)
                    {
                        ContextMenu.Items.Add(item);
                    }
                }
            );
        }

        private void OnMouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && AppState.Settings.DockingMode == DockingMode.None)
            {
                DragMove();
            }
        }

        public readonly System.Windows.Media.Animation.DoubleAnimation FadeOut = new System.Windows.Media.Animation.DoubleAnimation
        {
            From = 1.0,
            To = 0.5,
            Duration = new Duration(TimeSpan.FromSeconds(0.2))
        };

        private void OnDockingChanged(params object[] args)
        {
            var side = (DockingMode)args[0];
            
            // Get the screen that the window is currently on
            var currentScreen = GetCurrentScreen();
            
            if (currentScreen != null)
            {
                Console.WriteLine($"Starting dock process on screen: {currentScreen.DeviceName}");
                
                // Remove any existing AppBar
                AppBarFunctions.SetAppBar(this, DockingMode.None);

                // Update settings
                AppState.Settings.DockingMode = side;
                
                // Set initial position and size before docking
                var workingArea = currentScreen.WorkingArea;
                
                // Force window to be visible during positioning
                var wasHidden = (Visibility != Visibility.Visible);
                if (wasHidden)
                {
                    Visibility = Visibility.Visible;
                }

                // Set size based on docking side
                if (side == DockingMode.Left || side == DockingMode.Right)
                {
                    this.Width = Math.Min(300, workingArea.Width * 0.2);
                    this.Height = workingArea.Height;
                    // Force the window to take up full height
                    this.MinHeight = workingArea.Height;
                    this.MaxHeight = workingArea.Height;
                }
                else if (side == DockingMode.Top || side == DockingMode.Bottom)
                {
                    this.Width = workingArea.Width;
                    this.Height = Math.Min(200, workingArea.Height * 0.2);
                    // Reset height constraints
                    this.MinHeight = 0;
                    this.MaxHeight = double.PositiveInfinity;
                }
                else
                {
                    // Reset size constraints when not docked
                    this.MinHeight = 0;
                    this.MaxHeight = double.PositiveInfinity;
                    this.Width = 300;
                    this.Height = 600;
                }

                // Force layout update
                this.UpdateLayout();

                // Position window on current screen
                switch (side)
                {
                    case DockingMode.Left:
                        this.Left = workingArea.Left;
                        this.Top = workingArea.Top;
                        break;
                    case DockingMode.Right:
                        this.Left = workingArea.Right - this.ActualWidth;
                        this.Top = workingArea.Top;
                        break;
                    case DockingMode.Top:
                        this.Left = workingArea.Left;
                        this.Top = workingArea.Top;
                        break;
                    case DockingMode.Bottom:
                        this.Left = workingArea.Left;
                        this.Top = workingArea.Bottom - this.ActualHeight;
                        break;
                    default:
                        // For no docking, center on current screen
                        this.Left = workingArea.Left + (workingArea.Width - this.ActualWidth) / 2;
                        this.Top = workingArea.Top + (workingArea.Height - this.ActualHeight) / 2;
                        break;
                }

                // Create UI after position is set
                UiFactory.CreateUi(AppState.CurrentLayout, this);

                if (Visibility == Visibility.Visible && side != DockingMode.None)
                {
                    // Register as AppBar with force flag
                    AppBarFunctions.SetAppBar(this, side, true);
                    
                    // Double-check size after docking
                    if (side == DockingMode.Left || side == DockingMode.Right)
                    {
                        this.Height = workingArea.Height;
                        this.Top = workingArea.Top;
                    }
                }

                // Apply opacity
                ApplyOpacity(side);
                
                EventBeacon.SendEvent(Events.UpdateSettings);
                
                // Debug output
                DebugScreenInfo();
            }
        }

        private void ApplyOpacity(DockingMode side)
		{
			if (side != DockingMode.None)
			{
				MinOpacity = AppState.CurrentLayout.MaxOpacity;
				MaxOpacity = AppState.CurrentLayout.MaxOpacity;
				BeginAnimation(OpacityProperty, null);
				Opacity = AppState.CurrentLayout.MaxOpacity;
			}
			else
			{
				MinOpacity = AppState.CurrentLayout.MinOpacity;
				MaxOpacity = AppState.CurrentLayout.MaxOpacity;
				BeginAnimation(OpacityProperty, null);
				Opacity = AppState.CurrentLayout.MaxOpacity;
				BeginAnimation(OpacityProperty, FadeOut);
			}
		}

		private System.Windows.Forms.Screen GetCurrentScreen()
		{
			// Get all corners of the window for more reliable screen detection
			var points = new[]
			{
				new System.Drawing.Point((int)this.Left, (int)this.Top),
				new System.Drawing.Point((int)(this.Left + this.ActualWidth), (int)this.Top),
				new System.Drawing.Point((int)this.Left, (int)(this.Top + this.ActualHeight)),
				new System.Drawing.Point((int)(this.Left + this.ActualWidth), (int)(this.Top + this.ActualHeight))
			};

			foreach (var point in points)
			{
				var screen = System.Windows.Forms.Screen.FromPoint(point);
				if (screen != null)
				{
					return screen;
				}
			}

			return System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point((int)this.Left, (int)this.Top));
		}

        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);

            var helper = new WindowInteropHelper(this);
            SetWindowLong(
                helper.Handle,
                GWL_EXSTYLE,
                GetWindowLong(helper.Handle, GWL_EXSTYLE) | WS_EX_NOACTIVATE
            );

            // Only send initial docking event if not already docked
            if (AppState.Settings.DockingMode == DockingMode.None)
            {
                EventBeacon.SendEvent(Events.DockingChanged, AppState.Settings.DockingMode);
            }
        }

        private static bool _firstToggle = true;

        private void OnToggleMinimize(object[] obj)
        {
            if (Visibility == Visibility.Collapsed || Visibility == Visibility.Hidden)
            {
                Visibility = Visibility.Visible;
                if (!_firstToggle)
                {
                    AppBarFunctions.SetAppBar(this, AppState.Settings.DockingMode);
                }
                else
                {
                    Thread.Sleep(500);
                    AppBarFunctions.SetAppBar(this, AppState.Settings.DockingMode);
                    Thread.Sleep(100);
                    AppBarFunctions.SetAppBar(this, DockingMode.None);
                    Thread.Sleep(100);
                    AppBarFunctions.SetAppBar(this, AppState.Settings.DockingMode);
                    _firstToggle = false;
                }
            }
            else
            {
                AppBarFunctions.SetAppBar(this, DockingMode.None);
                Visibility = Visibility.Hidden;
            }
        }

        private void OnMinimize(object[] obj)
        {
            if (Visibility == Visibility.Visible)
            {
                AppBarFunctions.SetAppBar(this, DockingMode.None);
                Visibility = Visibility.Hidden;
            }
        }

        private void OnMaximize(object[] obj)
        {
            if (Visibility == Visibility.Collapsed || Visibility == Visibility.Hidden)
            {
                Visibility = Visibility.Visible;
                AppBarFunctions.SetAppBar(this, AppState.Settings.DockingMode);
            }
        }

        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_NOACTIVATE = 0x08000000;

        [DllImport("user32.dll")]
        public static extern IntPtr SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        public static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        private void DebugScreenInfo()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            foreach (var screen in screens)
            {
                Console.WriteLine($"Screen: {screen.DeviceName}");
                Console.WriteLine($"  Bounds: {screen.Bounds}");
                Console.WriteLine($"  Working Area: {screen.WorkingArea}");
                Console.WriteLine($"  Primary: {screen.Primary}");
            }
            
            var currentScreen = GetCurrentScreen();
            Console.WriteLine($"Current window position: Left={this.Left}, Top={this.Top}");
            Console.WriteLine($"Current screen: {currentScreen?.DeviceName}");
        }

        // Add this method to handle layout changes without redocking
        private void OnLayoutChanged(params object[] args)
        {
            if (args != null && args.Length > 0)
            {
                var currentDockingMode = AppState.Settings.DockingMode;
                UiFactory.CreateUi(AppState.CurrentLayout, this);
                
                // Maintain current docking state
                if (currentDockingMode != DockingMode.None && Visibility == Visibility.Visible)
                {
                    AppBarFunctions.SetAppBar(this, currentDockingMode, true);
                }
            }
        }
    }
}
