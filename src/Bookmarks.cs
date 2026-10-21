using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Net;
using System.Windows.Forms;

namespace QuickSearch
{
    // One bookmark on the row: site icon + name. A real Button underneath, so screen readers and clicks work as usual.
    class BookmarkButton : Button
    {
        public Color Fill, HoverFill;
        bool hover;

        public BookmarkButton()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            FlatStyle = FlatStyle.Flat;
            TabStop = false; // keeps focus in the search box; Ctrl+1-9 are the keyboard route
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.Clear(Parent.BackColor);
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int r = Height / 3 * 2, pad = Height / 3, icon = Height * 8 / 15;
            using (var path = new GraphicsPath())
            using (var brush = new SolidBrush(hover ? HoverFill : Fill))
            {
                path.AddArc(0, 0, r, r, 180, 90);
                path.AddArc(Width - 1 - r, 0, r, r, 270, 90);
                path.AddArc(Width - 1 - r, Height - 1 - r, r, r, 0, 90);
                path.AddArc(0, Height - 1 - r, r, r, 90, 90);
                path.CloseFigure();
                g.FillPath(brush, path);
            }
            var box = new Rectangle(pad, (Height - icon) / 2, icon, icon);
            if (Image != null)
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.DrawImage(Image, box);
            }
            else // no icon yet (or offline): first letter in a circle
            {
                using (var brush = new SolidBrush(Color.FromArgb(0x60, ForeColor))) g.FillEllipse(brush, box);
                TextRenderer.DrawText(g, Text.Substring(0, 1).ToUpperInvariant(), Font, box, Parent.BackColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            }
            var text = new Rectangle(box.Right + pad / 2 + 1, 0, Width - box.Right - pad - pad / 2, Height);
            TextRenderer.DrawText(g, Text, Font, text, ForeColor,
                TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
        }
    }

    // Site icons for bookmarks: downloaded once from Google's icon service, then read from disk.
    static class SiteIcons
    {
        static readonly string Folder = Path.Combine(Path.GetDirectoryName(Config.DefaultPath), "icons");
        static readonly Dictionary<string, Image> cache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        // Calls back on the UI thread with the icon: straight away if it's saved, otherwise after it downloads.
        // No callback if it can't be had (offline, odd host); the button keeps its letter.
        public static void Get(string link, Action<Image> done)
        {
            Uri uri;
            if (!Uri.TryCreate(link, UriKind.Absolute, out uri) || uri.Host.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0) return;
            var host = uri.Host;
            Image img;
            if (cache.TryGetValue(host, out img)) { done(img); return; }
            var file = Path.Combine(Folder, host + ".png");
            try
            {
                if (File.Exists(file)) { done(cache[host] = Decode(File.ReadAllBytes(file))); return; }
            }
            catch (Exception) { } // broken cache file: download it again below

            var web = new WebClient();
            web.DownloadDataCompleted += (s, e) => // raised back on the UI thread
            {
                web.Dispose();
                if (e.Error != null || e.Cancelled) return;
                try
                {
                    img = Decode(e.Result);
                    Directory.CreateDirectory(Folder);
                    File.WriteAllBytes(file, e.Result);
                    done(cache[host] = img);
                }
                catch (Exception) { }
            };
            web.DownloadDataAsync(new Uri("https://www.google.com/s2/favicons?sz=64&domain=" + Uri.EscapeDataString(host)));
        }

        static Image Decode(byte[] bytes)
        {
            using (var ms = new MemoryStream(bytes))
            using (var tmp = Image.FromStream(ms))
                return new Bitmap(tmp); // copy, so the image doesn't depend on the stream staying open
        }
    }
}
