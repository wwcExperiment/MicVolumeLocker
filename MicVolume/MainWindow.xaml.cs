using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using Microsoft.Win32;
using NAudio.Mixer;
using NAudio.Wave;
using Timer = System.Timers.Timer;

namespace MicVolumeLocker
{
    public class MicItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler? PropertyChanged;

        public int Index;

        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }

        private double volume;
        public string Volume { get => String.Format("{0:0.#}",volume); set { volume = double.Parse(value); } }
        public MicItem SetVolume(double val){volume= val;return this;}
        public double GetVolume()=>volume;
        public bool Locked { get; set; }
        public void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
    public static class Util
    {
        public static void RemoveAll<T>(this Collection<T> collection, Predicate<T> match)
        {
            if (match == null)
            {
                throw new ArgumentNullException("match");
            }

            collection.Where(entity => match(entity))
                      .ToList().ForEach(entity => collection.Remove(entity));
        }
    };
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {

        private ObservableCollection<MicItem> micItems = new ObservableCollection<MicItem>();
        Timer timer = new();
        private System.Windows.Forms.NotifyIcon m_notifyIcon;
        public MainWindow()
        {
            m_notifyIcon = new System.Windows.Forms.NotifyIcon();
            m_notifyIcon.BalloonTipText = "The app has been minimised. Click the tray icon to show.";
            m_notifyIcon.BalloonTipTitle = this.Title;
            m_notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            m_notifyIcon.Text = this.Title;
            m_notifyIcon.Icon = Resource1.Icon1;
            m_notifyIcon.Click += new EventHandler(m_notifyIcon_Click);
            InitializeComponent();
            timer.Interval = 10000;
            timer.Elapsed += (s, e) => { TimerScan(); };
            timer.Start();
            listBox.ItemsSource = micItems;
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                micItems.Add(new MicItem { Index = i, Name = caps.ProductName, Type = "mic", Locked = false }.SetVolume(GetMicVolume(i)));
            }
            TimerScan();
            checkBox.IsChecked = IsStartupEnabled();
            if (Environment.GetCommandLineArgs().Contains("-h"))
            {
                WindowState = WindowState.Minimized;
                OnStateChanged(this, null);
                CheckTrayIcon();
            }
        }
        private void TimerScan()
        {
            Dispatcher.Invoke(() =>
            {
                Title = "MicVolume Locker";
            });
            List<KeyValuePair<string, int>> mics = new();
            List<KeyValuePair<string, int>> mics2 = new();
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                var t = new KeyValuePair<string, int>(caps.ProductName, i);
                mics.Add(t);
                mics2.Add(t);
            }
            List<MicItem> notFound = new();
            foreach (var item in micItems)
            {
                if (item.Name == null) continue;
                var mic = (
                            from m in mics
                            where m.Key.StartsWith(item.Name)
                            select ((Func<KeyValuePair<string, int>, int>)((t) => { mics2.Remove(t); return t.Value; }))(m)
                           ).FirstOrDefault(-1);

                if (mic == -1)
                {
                    if (item.Locked)
                    { item.DisplayName = "missing"; }
                    else { notFound.Add(item); }
                }
                else
                {
                    item.DisplayName = "";
                    if (item.Locked)
                    {
                        double v = GetMicVolume(mic);
                        if (Math.Abs(v - item.GetVolume()) > 1)
                        {
                            SetMicVolume(mic, item.GetVolume());
                            Dispatcher.Invoke(() =>
                            {
                                Title += item.Name + " changed, reset to " + item.Volume;
                            });
                        }
                    }
                    else
                    {
                        item.SetVolume(GetMicVolume(mic));
                        Dispatcher.Invoke(() =>
                        {
                        });
                    }
                }
                Dispatcher.Invoke(() =>
                {
                    item.OnPropertyChanged("DisplayName");
                });
            }
            Dispatcher.Invoke(() =>
            {
                micItems.RemoveAll(notFound.Contains);
                foreach (var m in mics2)
                {
                    micItems.Add(new MicItem { Index = m.Value, Name = m.Key, Type = "mic", Locked = false }.SetVolume(GetMicVolume(m.Value)));
                }
            });
        }
        private static double GetMicVolume(int waveInDeviceNumber)
        {
            UnsignedMixerControl volumeControl = getControl(waveInDeviceNumber, MixerControlType.Volume);
            if (volumeControl != null)
            {
                return volumeControl.Percent;
            }
            return 0;
        }

        private static void SetMicVolume(int waveInDeviceNumber, double val)
        {
            UnsignedMixerControl volumeControl = getControl(waveInDeviceNumber, MixerControlType.Volume);
            if (volumeControl != null)
            {
                volumeControl.Percent = val;
            }
        }

        private static UnsignedMixerControl getControl(int waveInDeviceNumber, MixerControlType type)
        {
            var mixerLine = new MixerLine((IntPtr)waveInDeviceNumber,
                               0, MixerFlags.WaveIn);
            UnsignedMixerControl volumeControl = null;
            foreach (var control in mixerLine.Controls)
            {
                if (control.ControlType == type)
                {
                    volumeControl = control as UnsignedMixerControl;

                    break;
                }
            }

            return volumeControl;
        }



        void OnClose(object sender, CancelEventArgs args)
        {
            m_notifyIcon.Dispose();
            m_notifyIcon = null;
        }

        private WindowState m_storedWindowState = WindowState.Normal;
        void OnStateChanged(object sender, EventArgs args)
        {
            if (WindowState == WindowState.Minimized)
            {
                Hide();
            }
            else
                m_storedWindowState = WindowState;
        }
        void OnIsVisibleChanged(object sender, DependencyPropertyChangedEventArgs args)
        {
            CheckTrayIcon();
        }

        void m_notifyIcon_Click(object sender, EventArgs e)
        {
            Show();
            WindowState = m_storedWindowState;
        }
        void CheckTrayIcon()
        {
            ShowTrayIcon(!IsVisible);
        }

        void ShowTrayIcon(bool show)
        {
            if (m_notifyIcon != null)
                m_notifyIcon.Visible = show;
        }

        private void listBox_AutoGeneratingColumn(object? sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
        {
            var tc = e.Column as System.Windows.Controls.DataGridCheckBoxColumn;
            if (tc != null)
            {
                var b = tc.Binding as System.Windows.Data.Binding;

                b.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            }
        }

        private const string RegistryKeyPath = "SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run";

        public bool IsStartupEnabled()
        {
            string applicationName = GetApplicationName();

            using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath))
            {
                if (key != null)
                {
                    return key.GetValue(applicationName) != null;
                }
                else
                {
                    // Handle the case where the Registry key is not found
                    return false;
                }
            }
        }

        public void EnableStartup()
        {
            string applicationName = GetApplicationName();
            string applicationPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string currentUserSid = GetCurrentUserSid();

            using (RegistryKey key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, this.GetRegistryView()))
            {
                using (RegistryKey subKey = key.OpenSubKey(RegistryKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl))
                {
                    subKey.SetValue(applicationName, "\""+applicationPath+"\" -h", RegistryValueKind.String);

                    string[] names = subKey.GetValueNames();

                    Debug.WriteLine(subKey + " -- " + applicationPath);

                    foreach (string s in names)
                    {
                        Debug.WriteLine(" ->>> " + s);
                    }

                    subKey.Dispose();
                }
            }
        }

        public void DisableStartup()
        {
            string applicationName = GetApplicationName();
            string applicationPath = System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
            string currentUserSid = GetCurrentUserSid();

            try
            {
                RegistryKey myKey = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, this.GetRegistryView());
                {
                    using (RegistryKey subKey = myKey.OpenSubKey(RegistryKeyPath, RegistryKeyPermissionCheck.ReadWriteSubTree, RegistryRights.FullControl))
                    {
                        subKey.DeleteValue(applicationName, false);

                        string[] names = subKey.GetValueNames();

                        Debug.WriteLine(subKey);

                        foreach (string s in names)
                        {
                            Debug.WriteLine(" - " + s);
                        }

                        subKey.Close();
                    }
                }
            }
            catch (Exception ex)
            {
                // Handle or log the exception
                Console.WriteLine("Failed to disable startup: " + ex.Message);
            }
        }
        public string GetApplicationName()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            return assembly.GetName().Name;
        }

        public string GetApplicationPath()
        {
            Assembly assembly = Assembly.GetEntryAssembly();
            return Path.GetDirectoryName(assembly.Location);
        }

        private RegistryView GetRegistryView()
        {
            // Determine the registry view based on the application's target platform
            bool is64BitProcess = Environment.Is64BitProcess;
            return is64BitProcess ? RegistryView.Registry64 : RegistryView.Registry32;
        }

        private string GetCurrentUserSid()
        {
            try
            {
                // Get the current user's Windows identity
                WindowsIdentity windowsIdentity = WindowsIdentity.GetCurrent();

                if (windowsIdentity != null)
                {
                    // Get the user's SID
                    return windowsIdentity.User.Value;
                }
                else
                {
                    // Handle the case where the Windows identity is not available
                    return string.Empty; // or throw new Exception("Windows identity not available");
                }
            }
            catch (Exception ex)
            {
                // Handle or log any exceptions that may occur
                Console.WriteLine("Failed to retrieve current user's SID: " + ex.Message);
                return string.Empty; // or throw the exception if necessary
            }
        }

        private void checkBox_Changed(object sender, RoutedEventArgs e)
        {
            if (checkBox.IsChecked != IsStartupEnabled())
            {
                if (checkBox.IsChecked == true)
                {
                    EnableStartup();
                }
                else
                {
                    DisableStartup();
                }
            }
        }

    }

    
}
