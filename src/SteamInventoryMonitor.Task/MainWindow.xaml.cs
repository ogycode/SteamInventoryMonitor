﻿using Newtonsoft.Json;
using SteamInventoryMonitor.Model;
using System;
using System.IO;
using System.Windows;
using System.Windows.Input;
using System.Linq;
using System.Threading.Tasks;
using System.Net;
using SteamInventoryMonitor.Models;
using System.Windows.Threading;
using System.Collections.Generic;
using Verloka.HelperLib.Settings;

namespace SteamInventoryMonitor.Task
{
    public partial class MainWindow : Window
    {
        public bool IsPreview { get; private set; }
        public bool IsSilence { get; private set; }
        public TaskObject TO { get; private set; }
        public List<TaskItem> Finded { get; private set; }

        DispatcherTimer timer;
        List<Item> itemAssetsBuffer;
        System.Windows.Forms.NotifyIcon notifyIcon;

        RegSettings rs;

        public MainWindow(bool IsPreview, bool IsSilence = false)
        {
            rs = new RegSettings("SteamInventoryMonitor");
            App.NOTIFICATION_DELAY_S = rs.GetValue("NOTIFICATION_DELAY_S", 5);

            InitializeComponent();

            timer = new DispatcherTimer();
            timer.Tick += TimerTick;
            timer.Interval = new TimeSpan(0, 0, rs.GetValue("UpdateTimerDelay", 60));

            this.IsPreview = IsPreview;
            this.IsSilence = IsSilence;

            SetupNotifyIcon();
            SetupCollections();

            if (!IsPreview)
                timer.Start();
            else
                Title += " [Preview mode]";
        }

        public void SetupHomeButtonIsVisible(bool visible) => btnHome.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;
        public void SetupViewMode(int modeNumber)
        {
            switch (modeNumber)
            {
                case 1:
                default:
                    Width = 800;
                    Height = 480;
                    frame.Navigate(new Uri("Views/TaskLine.xaml", UriKind.Relative));
                    break;
                case 2:
                    Width = 800;
                    Height = 480;
                    frame.Navigate(new Uri("Views/NotificationLine.xaml", UriKind.Relative));
                    break;
            }
        }
        public bool Pred(int value, int argument, int method)
        {
            switch (method)
            {
                case 0 when value == argument:
                    return true;
                case 1 when value != argument:
                    return true;
                case 2 when value > argument:
                    return true;
                case 3 when value >= argument:
                    return true;
                case 4 when value < argument:
                    return true;
                case 5 when value <= argument:
                    return true;
                default:
                    return false;
            }
        }

        void SetupCollections()
        {
            Finded = new List<TaskItem>();
            itemAssetsBuffer = new List<Item>();

            TO = File.Exists(App.TASK) ? JsonConvert.DeserializeObject<TaskObject>(File.ReadAllText(App.TASK)) : new TaskObject();
            TO.Updated += TOUpdated;
        }
        void SetupNotifyIcon()
        {
            System.Windows.Forms.ContextMenu contextMenu = new System.Windows.Forms.ContextMenu();
            contextMenu.MenuItems.Add("Show Sindow", (sh, eh) => Show());
            contextMenu.MenuItems.Add("Open Task Line", (sh, eh) => SetupViewMode(1));
            contextMenu.MenuItems.Add("Open Notifications", (sh, eh) => SetupViewMode(2));
            contextMenu.MenuItems.Add("-");
            contextMenu.MenuItems.Add("Exit", (sh, eh) => Close());

            notifyIcon = new System.Windows.Forms.NotifyIcon
            {
                Text = "Steam Inventory Monitor: Task",
                Icon = new System.Drawing.Icon($"{Directory.GetCurrentDirectory()}/Icons/chevron.ico"),
                Visible = !IsPreview,
                ContextMenu = contextMenu
            };

            notifyIcon.DoubleClick += NotifyIconDoubleClick;
        }
        decimal RandomNumber(Random rnd, int precision, int scale)
        {
            if (rnd == null)
                throw new ArgumentNullException("randomNumberGenerator");
            if (!(precision >= 1 && precision <= 28))
                throw new ArgumentOutOfRangeException("precision", precision, "Precision must be between 1 and 28.");
            if (!(scale >= 0 && scale <= precision))
                throw new ArgumentOutOfRangeException("scale", precision, "Scale must be between 0 and precision.");

            Decimal d = 0m;
            for (int i = 0; i < precision; i++)
            {
                int r = rnd.Next(0, 10);
                d = d * 10m + r;
            }
            for (int s = 0; s < scale; s++)
            {
                d /= 10m;
            }
            return d;
        }
        Task<bool> UpdateInformation()
        {
            return System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                if (TO == null || TO.IsEmpty)
                    return false;

                List<TaskItem> finded = new List<TaskItem>();

                var items = TO.Items.Concat(TO.ItemsNF);

                //group by ID64
                var id64 = from own in items
                           group own by own.OwnerID64 into ownGroup
                           select from i in ownGroup
                                  select i;

                foreach (var owner in id64)
                {
                    //group by AppID
                    var appid = from invs in owner
                                group invs by invs.AppId into invGroup
                                select from i in invGroup
                                       select i;


                    //parse Inventories
                    foreach (var inv in appid)
                    {
                        var d = LoadInventory(owner.First().OwnerID64, inv.First().AppId, inv.First().AppContext);
                        d.Wait();

                        foreach (var item in items)
                        {
                            var classIdItems = from i in itemAssetsBuffer
                                               where i.classid == item.ClassId
                                               select i;

                            if (Pred(classIdItems.Count(), item.CompareArgument, item.CompareMethod))
                                finded.Add(item);

                        }

                        itemAssetsBuffer.Clear();
                    }
                }

                Finded.Clear();
                Finded.AddRange(finded);

                return Finded.Count > 0;
            });
        }
        Task<bool> LoadInventory(string id64, string appid, int appcontext, string lid = "", int count = 5000)
        {
            return System.Threading.Tasks.Task.Factory.StartNew(() =>
            {
                string lastid = lid;
                string tail = string.IsNullOrWhiteSpace(lastid) ? string.Empty : $"&start_assetid={lastid}";

                string str = $"http://steamcommunity.com/inventory/{id64}/{appid}/{appcontext}?l={App.LANGUAGE}&count={count}{tail}";

                using (WebClient wc = new WebClient())
                {
                    Inventory inv = JsonConvert.DeserializeObject<Inventory>(wc.DownloadString(str));

                    if (inv.IsSuccess)
                    {
                        itemAssetsBuffer.AddRange(inv.Assets);

                        if (inv.IsNext)
                        {
                            var d = LoadInventory(id64, appid, appcontext, inv.LastAssteId);
                            d.Wait();
                        }

                        return true;
                    }
                }
                return false;
            });
        }

        #region Window Events
        private void DragWindow(object sender, MouseButtonEventArgs e)
        {
            try { DragMove(); }
            catch { }
        }
        private void btnCloseWinodwClick(object sender, RoutedEventArgs e)
        {
            if (IsPreview)
                Close();
            else
                Hide();
        }
        #endregion

        private void windowLoaded(object sender, RoutedEventArgs e) => SetupViewMode(1);
        private void NotifyIconDoubleClick(object sender, EventArgs e) => Show();
        private async void TOUpdated()
        {
            if (File.Exists(App.TASK))
                File.Delete(App.TASK);

            using (StreamWriter sw = File.CreateText(App.TASK))
                await sw.WriteLineAsync(JsonConvert.SerializeObject(TO));
        }
        private async void TimerTick(object sender, EventArgs e)
        {
            if (await UpdateInformation())
            {
                btnNotification.Content = Char.ConvertFromUtf32(0xE7E7);

                NotificationWindow nw = new NotificationWindow();
                nw.Clicked += NwClicked;

                Random rnd = new Random((int)DateTime.Now.Ticks);

                nw.NotificationTitle = Finded.Count > 1 ? $"Items: [{Finded.Count}]" : Finded[0].Name;
                nw.NotificationMsg = Finded.Count > 1 ?  "Your items was found! Enjoy, Dear!" : "Your item was found! Enjoy, Dear!";
                nw.NotificationIcon = Finded.Count > 1 ?  Finded[rnd.Next(Finded.Count)].IconUrl : Finded[0].IconUrl;

                nw.Show();
            }
            else
                btnNotification.Content = Char.ConvertFromUtf32(0xEC42);


            timer.Start();
        }

        private void NwClicked()
        {
            Show();
            SetupViewMode(2);
        }
        private void windowClosing(object sender, System.ComponentModel.CancelEventArgs e) => notifyIcon.Visible = false;
        private void btnNotificationClick(object sender, RoutedEventArgs e)
        {
            Show();
            SetupViewMode(2);
        }
        private void btnHomeClick(object sender, RoutedEventArgs e)
        {
            Show();
            SetupViewMode(1);
        }
    }
}
