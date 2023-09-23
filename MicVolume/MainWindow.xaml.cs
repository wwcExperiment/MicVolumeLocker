using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.DirectoryServices;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Timers;
using System.Windows;
using System.Windows.Data;
using System.Windows.Forms;
using System.Xml.Linq; 
using NAudio.Mixer;
using NAudio.Wave;
using Timer = System.Timers.Timer;

namespace MicVolumeLocker
{
    public class MicItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        public int Index;

        public string? Type { get; set; }
        public string? Name { get; set; }
        public string? DisplayName { get; set; }

        private double volume;
        public string Volume { get => String.Format("{0:0.#}",volume); set { volume = double.Parse(value); } }
        public MicItem SetVolume(double val){volume= val;return this;}
        public double GetVolume()=>volume;
        public bool Locked { get; set; }
        public void OnPropertyChanged([CallerMemberName] string name = null)
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
            m_notifyIcon.BalloonTipTitle = "The App";
            m_notifyIcon.BalloonTipIcon = ToolTipIcon.Info;
            m_notifyIcon.Text = "The App"; 
            m_notifyIcon.Icon = Resource1.Icon1;
            m_notifyIcon.Click += new EventHandler(m_notifyIcon_Click);
            InitializeComponent();
            timer.Interval = 10000;
            timer.Elapsed += (s, e) => { TimerScan(); };
            timer.Start();
            listBox.ItemsSource=micItems;
            for (int i = 0; i < WaveIn.DeviceCount; i++)
            {
                var caps = WaveIn.GetCapabilities(i);
                micItems.Add(new MicItem { Index = i, Name = caps.ProductName, Type="mic", Locked = false }.SetVolume(GetMicVolume(i)));
            } 
            TimerScan();
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
                    { item.DisplayName = "missing";  }
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
                        item.SetVolume( GetMicVolume(mic));
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

        private void CheckBox_Checked(object sender, RoutedEventArgs e)
        {
            //System.Windows.Controls.CheckBox checkBox = sender as System.Windows.Controls.CheckBox;
            //if (checkBox == null) { return; }
            //MicItem? micItem = checkBox.Tag as MicItem;
            //if (micItem == null) { return; } 
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
                //if (m_notifyIcon != null)
                //    m_notifyIcon.ShowBalloonTip(2000);
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

        private void listBox_AutoGeneratingColumn(object sender, System.Windows.Controls.DataGridAutoGeneratingColumnEventArgs e)
        {
            var tc = e.Column as System.Windows.Controls.DataGridCheckBoxColumn;
            if (tc != null)
            {
                var b = tc.Binding as System.Windows.Data.Binding;

                b.UpdateSourceTrigger = UpdateSourceTrigger.PropertyChanged;
            }
        }
    }

    
}
