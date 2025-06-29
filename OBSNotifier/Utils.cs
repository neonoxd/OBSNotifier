﻿using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace OBSNotifier
{
    public static partial class Utils
    {
        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        public static string GetCurrentWindowTitle()
        {
            IntPtr handle = GetForegroundWindow();
            if (handle == IntPtr.Zero)
                return string.Empty;

            try
            {
                GetWindowThreadProcessId(handle, out uint processId);
                Process proc = Process.GetProcessById((int)processId);
                return proc.ProcessName;
            }
            catch 
            {
                return string.Empty;
            }
        }

        /// <summary>
        /// Get the translated string by ID
        /// </summary>
        /// <param name="id"></param>
        /// <returns></returns>
        public static string Tr(string id, ResourceDictionary specificResDict = null)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException($"{nameof(id)} must not be null or an empty string");

            var is_spec_dict = specificResDict != null;
            ResourceDictionary dict = is_spec_dict ? specificResDict : Application.Current.Resources;

            if (dict.Contains(id))
            {
                var obj = dict[id];
                if (obj.GetType() != typeof(string))
                {
                    App.Log($"Resource with ID \"{id}\" is not a string." + (is_spec_dict ? " Using a specific dictionary" : ""));
                    return $"[{id}]";
                }

                return (string)obj;
            }

            App.Log($"Resource ID ({id}) not found." + (is_spec_dict ? " Using a specific dictionary" : ""));
            return $"[{id}]";
        }

        /// <summary>
        /// Get the translated string by ID and apply formatting to it.
        /// If formatting fails, fallback to the default language and apply formatting to it.
        /// </summary>
        /// <param name="id"></param>
        /// <param name="args"></param>
        /// <returns></returns>
        public static string TrFormat(string id, params object[] args)
        {
            var loc_str = Tr(id);

            try
            {
                return string.Format(loc_str, args);
            }
            catch (Exception ex)
            {
                App.Log($"The localized string ({id}) cannot be formatted for {Thread.CurrentThread.CurrentUICulture}");
                App.Log(ex);
            }

            // Second try to fallback
            var dicts = Application.Current.Resources.MergedDictionaries.Where((i) => i.Source != null && i.Source.OriginalString.StartsWith("Localization/lang.")).ToArray();
            loc_str = Tr(id, dicts[0]);

            return string.Format(loc_str, args);
        }

        public enum AnchorPoint
        {
            TopLeft = 0, TopRight = 1, BottomRight = 2, BottomLeft = 3,
            Center = 4,
        }

        /// <summary>
        /// Simple invoke wrapper
        /// </summary>
        /// <param name="disp"></param>
        /// <param name="act"></param>
        public static DispatcherOperation InvokeAction(this DispatcherObject disp, Action act)
        {
            return disp.Dispatcher.BeginInvoke(act);
        }

        public static string EncryptString(string plainText)
        {
            if (plainText == null) throw new ArgumentNullException("plainText");
            if (plainText == string.Empty) return "";
            var data = Encoding.Unicode.GetBytes(plainText);
            return Convert.ToBase64String(data);
        }

        public static string DecryptString(string encrypted)
        {
            if (encrypted == null) throw new ArgumentNullException("encrypted");
            if (encrypted == string.Empty) return "";
            byte[] data = Convert.FromBase64String(encrypted);
            return Encoding.Unicode.GetString(data);
        }

        /// <summary>
        /// Fix the position of the window that goes beyond the borders of the window's screen
        /// </summary>
        /// <param name="wnd"></param>
        public static void FixWindowLocation(Window wnd)
        {
            FixWindowLocation(wnd, WPFScreens.GetScreenFrom(wnd));
        }

        /// <summary>
        /// Fix the position of a window that goes beyond the boundaries of the <paramref name="screen"/>
        /// </summary>
        /// <param name="wnd"></param>
        /// <param name="screen"></param>
        public static void FixWindowLocation(Window wnd, WPFScreens screen)
        {
            var rect = screen.WorkingArea;

            if (rect.Size.Width < wnd.Width || rect.Size.Height < wnd.Height)
            {
                if (rect.Size.Width < wnd.Width)
                    wnd.Left = rect.Left;
                if (rect.Size.Height < wnd.Height)
                    wnd.Top = rect.Top;
                return;
            }

            if (wnd.Left < rect.Left)
                wnd.Left = rect.Left;
            if (wnd.Top < rect.Top)
                wnd.Top = rect.Top;

            if (wnd.Left + wnd.Width > rect.Right)
                wnd.Left = rect.Right - wnd.Width;
            if (wnd.Top + wnd.Height > rect.Bottom)
                wnd.Top = rect.Bottom - wnd.Height;
        }

        /// <summary>
        /// Returns the current <see cref="WPFScreens"/> selected in the application settings
        /// </summary>
        /// <returns></returns>
        public static WPFScreens GetCurrentNotificationScreen()
        {
            foreach (var s in WPFScreens.AllScreens())
            {
                if (s.DeviceName == Settings.Instance.DisplayID)
                    return s;
            }
            return WPFScreens.Primary;
        }

        /// <summary>
        /// Returns the bounds of the <see cref="WPFScreens"/> based on the selected settings
        /// </summary>
        /// <returns></returns>
        public static Rect GetCurrentDisplayBounds(WPFScreens screen)
        {
            return Settings.Instance.CurrentModuleSettings.UseSafeDisplayArea ? screen.WorkingArea : screen.DeviceBounds;
        }

        /// <summary>
        /// Returns the location of the window inside the currently selected notification screen.
        /// The location of the window will depend on the selected <see cref="AnchorPoint"/> and the offset from it.
        /// </summary>
        /// <param name="anchor"></param>
        /// <param name="size"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static Point GetWindowPosition(AnchorPoint anchor, Size size, Point offset)
        {
            return GetWindowPosition(GetCurrentNotificationScreen(), anchor, size, offset);
        }

        /// <summary>
        /// Returns the location of the window inside the screen.
        /// The location of the window will depend on the selected <see cref="AnchorPoint"/> and the offset from it.
        /// </summary>
        /// <param name="screen"></param>
        /// <param name="anchor"></param>
        /// <param name="size"></param>
        /// <param name="offset"></param>
        /// <returns></returns>
        public static Point GetWindowPosition(WPFScreens screen, AnchorPoint anchor, Size size, Point offset)
        {
            if (screen == null)
                return new Point();

            var rect = GetCurrentDisplayBounds(screen);

            var boundsPos = rect.TopLeft;
            var boundsPosEnd = new Point(rect.BottomRight.X - size.Width, rect.BottomRight.Y - size.Height);
            var offsetX = (rect.Width - size.Width) * offset.X;
            var offsetY = (rect.Height - size.Height) * offset.Y;

            switch (anchor)
            {
                case AnchorPoint.TopLeft:
                    return new Point(boundsPos.X + offsetX, boundsPos.Y + offsetY);
                case AnchorPoint.TopRight:
                    return new Point(boundsPosEnd.X - offsetX, boundsPos.Y + offsetY);
                case AnchorPoint.BottomRight:
                    return new Point(boundsPosEnd.X - offsetX, boundsPosEnd.Y - offsetY);
                case AnchorPoint.BottomLeft:
                    return new Point(boundsPos.X + offsetX, boundsPosEnd.Y - offsetY);
                case AnchorPoint.Center:
                    return new Point(boundsPos.X + offsetX, boundsPos.Y + offsetY);
            }
            return new Point();
        }

        /// <summary>
        /// Get a trimmed file path
        /// </summary>
        /// <param name="path"></param>
        /// <param name="chars"></param>
        /// <returns></returns>
        public static string GetShortPath(string path, uint chars)
        {
            if (string.IsNullOrEmpty(path) || chars <= 0)
                return "";

            string short_name;
            if (path.Length > chars)
            {
                short_name = path.Substring(path.Length - (int)chars);
                var slash_pos = short_name.IndexOf(Path.DirectorySeparatorChar);
                if (slash_pos == -1)
                    slash_pos = short_name.IndexOf(Path.AltDirectorySeparatorChar);

                if (slash_pos == -1)
                    short_name = "..." + short_name;
                else
                    short_name = "..." + short_name.Substring(slash_pos);
            }
            else
            {
                short_name = path;
            }
            return short_name;
        }

        /// <summary>
        /// Load WPF Image from resources or file path
        /// </summary>
        /// <param name="path">Image path</param>
        /// <param name="assembly">Assembly to perform search in relative path</param>
        /// <returns>Loaded image</returns>
        public static BitmapImage GetBitmapImage(string path, System.Reflection.Assembly assembly = null)
        {
            BitmapImage img = new BitmapImage();
            img.BeginInit();

            if (assembly != null)
                img.UriSource = new Uri(Path.Combine(Path.GetDirectoryName(assembly.Location), path));
            else
                img.UriSource = new Uri(path);

            img.EndInit();

            return img;
        }
    }
}
