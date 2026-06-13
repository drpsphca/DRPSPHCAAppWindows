using Microsoft.UI;
using Microsoft.UI.Composition;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Hosting;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Imaging;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.ApplicationModel;

namespace DRPS_PHCA
{
    public sealed partial class MainWindow : Window
    {
        private const string BaseUrl = "https://drpsphca.com/wp-json/wp/v2";
        private const string Token = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOjMsIm5hbWUiOiJkcnBzcGhjYWFwcCIsImlhdCI6MTc3Mzg5OTY4OCwiZXhwIjoxOTMxNTc5Njg4fQ.cBprQxphZjlj1rB5V-gE3Bid36LKznCWz1cvFn2flBQ";

        private static readonly HttpClient Http = new();
        private record PostData(int Id, string Title, string Excerpt, string Date, string ImageUrl, string[] Tags);

        private enum NavTab { Home, Blog, Newsletter }
        private NavTab _currentTab = NavTab.Home;
        private int _blogPage = 1;
        private bool _blogLoading = false;

        [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
        private static extern nint ExtractIconW(nint hInst, string lpszExeFileName, int nIconIndex);

        [DllImport("user32.dll")]
        private static extern bool DestroyIcon(nint hIcon);

        [GeneratedRegex("<.*?>", RegexOptions.Singleline)]
        private static partial Regex HtmlTagRegex();

        public MainWindow()
        {
            InitializeComponent();
            ExtendsContentIntoTitleBar = true;
            SetTitleBar(TitleBarDragArea);

            try
            {
                AppWindow.SetIcon(System.IO.Path.Combine(
                    Package.Current.InstalledLocation.Path,
                    "Public - DRPSPHCA.com.ico"));
            }
            catch
            {
                try
                {
                    var exePath = Environment.ProcessPath!;
                    var hIcon = ExtractIconW(0, exePath, 0);
                    if (hIcon != 0)
                    {
                        AppWindow.SetIcon(Win32Interop.GetIconIdFromIcon(hIcon));
                        DestroyIcon(hIcon);
                    }
                }
                catch { }
            }

            Http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", Token);
            var v = Package.Current.Id.Version;
            VersionText.Text = $"Version {v.Major}.{v.Minor}.{v.Build}.{v.Revision}";
            UpdateNavSelection(NavTab.Home);

            _ = LoadPostsAsync();
        }

        // ── Posts loading ─────────────────────────────────────────────────────

        private async Task LoadPostsAsync()
        {
            var newsletterPosts = await FetchPostsAsync("newsletter", 1);
            var blogPosts = await FetchPostsAsync("blog", 15);

            if (newsletterPosts.Length > 0)
                NewsletterContainer.Child = BuildNewsletterCard(newsletterPosts[0]);

            AddThreeColumnDefs(BlogGrid);
            AppendCardsToGrid(BlogGrid, blogPosts, BuildBlogCard);
        }

        private static void AddThreeColumnDefs(Grid grid)
        {
            if (grid.ColumnDefinitions.Count > 0) return;
            for (int i = 0; i < 3; i++)
                grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        }

        private static void AppendCardsToGrid(Grid grid, PostData[] posts, Func<PostData, Button> builder)
        {
            int existingCards = grid.Children.Count;
            for (int i = 0; i < posts.Length; i++)
            {
                int absIndex = existingCards + i;
                int row = absIndex / 3;
                int col = absIndex % 3;
                if (grid.RowDefinitions.Count <= row)
                    grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var card = builder(posts[i]);
                Grid.SetRow(card, row);
                Grid.SetColumn(card, col);
                grid.Children.Add(card);
            }
        }

        // ── Nav ───────────────────────────────────────────────────────────────

        private void NavHome_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Home);
        private void NavBlog_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Blog);
        private void NavNewsletter_Click(object sender, RoutedEventArgs e) => SwitchTab(NavTab.Newsletter);

        private void SwitchTab(NavTab tab)
        {
            if (_currentTab == tab) return;
            _currentTab = tab;
            UpdateNavSelection(tab);

            HomeView.Visibility       = tab == NavTab.Home       ? Visibility.Visible : Visibility.Collapsed;
            BlogView.Visibility       = tab == NavTab.Blog       ? Visibility.Visible : Visibility.Collapsed;
            NewsletterView.Visibility = tab == NavTab.Newsletter ? Visibility.Visible : Visibility.Collapsed;
            ArticleWebView.Visibility = Visibility.Collapsed;
            ArticleToolbar.Visibility = Visibility.Collapsed;

            if (tab == NavTab.Blog && BlogViewGrid.Children.Count == 0)
                _ = LoadBlogViewAsync();
            if (tab == NavTab.Newsletter && NewsletterViewGrid.Children.Count == 0)
                _ = LoadNewsletterViewAsync();
        }

        private void UpdateNavSelection(NavTab tab)
        {
            SetNavButton(NavHomeButton,       NavHomeText,       NavHomeIcon,       tab == NavTab.Home,       "#128FF1");
            SetNavButton(NavBlogButton,       NavBlogText,       NavBlogIcon,       tab == NavTab.Blog,       "#734CC2");
            SetNavButton(NavNewsletterButton, NavNewsletterText, NavNewsletterIcon, tab == NavTab.Newsletter, "#DA048F");
        }

        private static void SetNavButton(Button btn, TextBlock label, PathIcon icon, bool selected, string accentHex)
        {
            var accent      = ParseColor(accentHex);
            var accentBrush = new SolidColorBrush(accent);
            var whiteBrush  = new SolidColorBrush(Microsoft.UI.Colors.White);
            var clearBrush  = new SolidColorBrush(Microsoft.UI.Colors.Transparent);

            btn.Background   = selected ? accentBrush : clearBrush;
            btn.Foreground   = selected ? whiteBrush  : accentBrush;
            label.Foreground = selected ? whiteBrush  : accentBrush;
            icon.Foreground  = selected ? whiteBrush  : accentBrush;

            btn.PointerEntered -= NavButton_PointerEntered;
            btn.PointerExited  -= NavButton_PointerExited;

            if (!selected)
            {
                btn.Tag = accentHex;
                btn.PointerEntered += NavButton_PointerEntered;
                btn.PointerExited  += NavButton_PointerExited;
            }
        }

        private static void NavButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(Windows.UI.Color.FromArgb(25, 128, 128, 128));
        }

        private static void NavButton_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        }

        private void BackButton_PointerEntered(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(ParseColor("#062F50"));
        }

        private void BackButton_PointerExited(object sender, Microsoft.UI.Xaml.Input.PointerRoutedEventArgs e)
        {
            if (sender is Button btn)
                btn.Background = new SolidColorBrush(ParseColor("#128FF1"));
        }

        private static Windows.UI.Color ParseColor(string hex)
        {
            hex = hex.TrimStart('#');
            return Windows.UI.Color.FromArgb(255,
                Convert.ToByte(hex[0..2], 16),
                Convert.ToByte(hex[2..4], 16),
                Convert.ToByte(hex[4..6], 16));
        }

        private static bool ExeMatchesProcessArchitecture(string exePath)
        {
            try
            {
                if (!File.Exists(exePath)) return false;
                using var fs = File.OpenRead(exePath);
                using var br = new System.IO.BinaryReader(fs);
                // Check MZ header
                var mz = br.ReadUInt16();
                if (mz != 0x5A4D) return false; // 'MZ'
                fs.Seek(0x3C, SeekOrigin.Begin);
                int peOffset = br.ReadInt32();
                fs.Seek(peOffset, SeekOrigin.Begin);
                var pe = br.ReadInt32();
                if (pe != 0x00004550) return false; // 'PE\0\0'
                ushort machine = br.ReadUInt16();
                const ushort IMAGE_FILE_MACHINE_I386 = 0x014c;
                const ushort IMAGE_FILE_MACHINE_AMD64 = 0x8664;
                const ushort IMAGE_FILE_MACHINE_ARM64 = 0xAA64;
                if (Environment.Is64BitProcess)
                    return machine == IMAGE_FILE_MACHINE_AMD64 || machine == IMAGE_FILE_MACHINE_ARM64;
                return machine == IMAGE_FILE_MACHINE_I386;
            }
            catch
            {
                return false;
            }
        }

        // ── Blog view ─────────────────────────────────────────────────────────

        private async Task LoadBlogViewAsync()
        {
            if (_blogLoading) return;
            _blogLoading = true;
            AddThreeColumnDefs(BlogViewGrid);
            var posts = await FetchPostsAsync("blog", 15, _blogPage);
            AppendCardsToGrid(BlogViewGrid, posts, BuildBlogCard);
            _blogLoading = false;
        }

        private async void BlogLoadMore_Click(object sender, RoutedEventArgs e)
        {
            if (_blogLoading) return;
            _blogPage++;
            await LoadBlogViewAsync();
        }

        // ── Newsletter view ───────────────────────────────────────────────────

        private async Task LoadNewsletterViewAsync()
        {
            AddThreeColumnDefs(NewsletterViewGrid);
            var posts = await FetchPostsAsync("newsletter", 9);
            AppendCardsToGrid(NewsletterViewGrid, posts, BuildBlogCard);
        }

        // ── Data fetching ─────────────────────────────────────────────────────

        private static async Task<PostData[]> FetchPostsAsync(string categorySlug, int count, int page = 1)
        {
            try
            {
                var catResp = await Http.GetStringAsync($"{BaseUrl}/categories?slug={categorySlug}&_fields=id");
                using var catDoc = JsonDocument.Parse(catResp);
                if (catDoc.RootElement.GetArrayLength() == 0) return Array.Empty<PostData>();
                int catId = catDoc.RootElement[0].GetProperty("id").GetInt32();

                var postsResp = await Http.GetStringAsync(
                    $"{BaseUrl}/posts?categories={catId}&per_page={count}&page={page}&_embed=wp:featuredmedia,wp:term&_fields=id,title,excerpt,date,_links,_embedded");
                using var postsDoc = JsonDocument.Parse(postsResp);

                var results = new List<PostData>();
                foreach (var post in postsDoc.RootElement.EnumerateArray())
                {
                    int id = post.GetProperty("id").GetInt32();
                    string title   = DecodeHtml(post.GetProperty("title").GetProperty("rendered").GetString() ?? "");
                    string excerpt = DecodeHtml(post.GetProperty("excerpt").GetProperty("rendered").GetString() ?? "");
                    string date    = post.GetProperty("date").GetString() ?? "";
                    if (DateTime.TryParse(date, out var dt))
                        date = dt.ToString("MMMM d, yyyy");

                    string imgUrl = "";
                    if (post.TryGetProperty("_embedded", out var embedded) &&
                        embedded.TryGetProperty("wp:featuredmedia", out var media) &&
                        media.GetArrayLength() > 0 &&
                        media[0].TryGetProperty("source_url", out var src))
                        imgUrl = src.GetString() ?? "";

                    var tags = new List<string>();
                    if (post.TryGetProperty("_embedded", out var emb2) &&
                        emb2.TryGetProperty("wp:term", out var termGroups))
                    {
                        foreach (var group in termGroups.EnumerateArray())
                            foreach (var term in group.EnumerateArray())
                                if (term.TryGetProperty("taxonomy", out var tax) &&
                                    tax.GetString() == "post_tag" &&
                                    term.TryGetProperty("name", out var tagName))
                                    tags.Add(DecodeHtml(tagName.GetString() ?? ""));
                    }

                    results.Add(new PostData(id, title, excerpt, date, imgUrl, tags.ToArray()));
                }
                return results.ToArray();
            }
            catch { return Array.Empty<PostData>(); }
        }

        private static string DecodeHtml(string html) =>
            string.IsNullOrEmpty(html) ? "" :
            WebUtility.HtmlDecode(HtmlTagRegex().Replace(html, "")).Trim();

        // ── Card builders ─────────────────────────────────────────────────────

        private static void AttachHoverAnimation(FrameworkElement element)
        {
            var compositor = ElementCompositionPreview.GetElementVisual(element).Compositor;

            element.PointerEntered += (_, _) =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                visual.CenterPoint = new Vector3((float)(element.ActualWidth / 2), (float)(element.ActualHeight / 2), 0);
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(1f, new Vector3(1.03f, 1.03f, 1f));
                anim.Duration = TimeSpan.FromMilliseconds(150);
                visual.StartAnimation("Scale", anim);
            };

            element.PointerExited += (_, _) =>
            {
                var visual = ElementCompositionPreview.GetElementVisual(element);
                var anim = compositor.CreateVector3KeyFrameAnimation();
                anim.InsertKeyFrame(1f, new Vector3(1f, 1f, 1f));
                anim.Duration = TimeSpan.FromMilliseconds(150);
                visual.StartAnimation("Scale", anim);
            };
        }

        private Button BuildNewsletterCard(PostData post)
        {
            var imgBrush = new ImageBrush
            {
                Stretch = Stretch.UniformToFill,
                AlignmentX = AlignmentX.Center,
                AlignmentY = AlignmentY.Center
            };
            if (!string.IsNullOrEmpty(post.ImageUrl))
                imgBrush.ImageSource = new BitmapImage(new Uri(post.ImageUrl));

            var imgBorder = new Border
            {
                Width = 300,
                CornerRadius = new CornerRadius(7, 0, 0, 7),
                Background = string.IsNullOrEmpty(post.ImageUrl)
                    ? (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"]
                    : imgBrush
            };

            var body = new StackPanel { Padding = new Thickness(20), Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
            body.Children.Add(new TextBlock { Text = post.Title, Style = (Style)Application.Current.Resources["SubtitleTextBlockStyle"], TextWrapping = TextWrapping.Wrap });
            body.Children.Add(new TextBlock { Text = post.Date, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], Margin = new Thickness(0, 2, 0, 6) });
            body.Children.Add(new TextBlock { Text = post.Excerpt, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap, MaxLines = 4, TextTrimming = TextTrimming.WordEllipsis });

            var inner = new Grid();
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            inner.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            Grid.SetColumn(imgBorder, 0);
            Grid.SetColumn(body, 1);
            inner.Children.Add(imgBorder);
            inner.Children.Add(body);

            var cardBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = inner
            };

            var btn = new Button
            {
                Content = cardBorder,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                try { await OpenArticleAsync(post.Id); }
                finally { btn.IsEnabled = true; }
            };
            btn.Loaded += (_, _) => AttachHoverAnimation(btn);
            return btn;
        }

        private static Border BuildTagChip(string tag) => new()
        {
            Background = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"],
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            Child = new TextBlock
            {
                Text = tag,
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontSize = 11,
                Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"]
            }
        };

        private Button BuildBlogCard(PostData post)
        {
            var img = new Image { Stretch = Stretch.UniformToFill, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center };
            if (!string.IsNullOrEmpty(post.ImageUrl))
                img.Source = new BitmapImage(new Uri(post.ImageUrl));

            var imgBorder = new Border
            {
                Height = 150,
                CornerRadius = new CornerRadius(7, 7, 0, 0),
                Background = (Brush)Application.Current.Resources["ControlAltFillColorSecondaryBrush"],
                Child = img
            };

            var body = new StackPanel { Padding = new Thickness(12), Spacing = 4 };
            body.Children.Add(new TextBlock { Text = post.Title, Style = (Style)Application.Current.Resources["BodyStrongTextBlockStyle"], TextWrapping = TextWrapping.Wrap, MaxLines = 2, TextTrimming = TextTrimming.WordEllipsis });
            body.Children.Add(new TextBlock { Text = post.Date, Style = (Style)Application.Current.Resources["CaptionTextBlockStyle"], Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"] });
            body.Children.Add(new TextBlock { Text = post.Excerpt, Style = (Style)Application.Current.Resources["BodyTextBlockStyle"], Foreground = (Brush)Application.Current.Resources["TextFillColorSecondaryBrush"], TextWrapping = TextWrapping.Wrap, MaxLines = 3, TextTrimming = TextTrimming.WordEllipsis });

            if (post.Tags.Length > 0)
            {
                var tagsPanel = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Margin = new Thickness(0, 6, 0, 0) };
                foreach (var tag in post.Tags)
                    tagsPanel.Children.Add(BuildTagChip(tag));
                body.Children.Add(tagsPanel);
            }

            var stack = new StackPanel();
            stack.Children.Add(imgBorder);
            stack.Children.Add(body);

            var cardBorder = new Border
            {
                Background = (Brush)Application.Current.Resources["CardBackgroundFillColorDefaultBrush"],
                BorderBrush = (Brush)Application.Current.Resources["CardStrokeColorDefaultBrush"],
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Child = stack
            };

            var btn = new Button
            {
                Content = cardBorder,
                Padding = new Thickness(0),
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
                BorderThickness = new Thickness(0),
                HorizontalAlignment = HorizontalAlignment.Stretch,
                HorizontalContentAlignment = HorizontalAlignment.Stretch,
                VerticalContentAlignment = VerticalAlignment.Stretch
            };
            btn.Click += async (_, _) =>
            {
                btn.IsEnabled = false;
                try { await OpenArticleAsync(post.Id); }
                finally { btn.IsEnabled = true; }
            };
            btn.Loaded += (_, _) => AttachHoverAnimation(btn);
            return btn;
        }

        // ── Article ───────────────────────────────────────────────────────────

        private void AnimateNavBar(bool slideOut)
        {
            var visual     = ElementCompositionPreview.GetElementVisual(NavBar);
            var compositor = visual.Compositor;

            float targetY = slideOut
                ? -(float)(NavBar.ActualHeight + NavBar.Margin.Top + 4)
                : 0f;

            var easing = compositor.CreateCubicBezierEasingFunction(new Vector2(0.4f, 0f), new Vector2(0.2f, 1f));
            var anim   = compositor.CreateScalarKeyFrameAnimation();
            anim.InsertKeyFrame(0f, slideOut ? 0f : targetY);
            anim.InsertKeyFrame(1f, slideOut ? targetY : 0f, easing);
            anim.Duration = TimeSpan.FromMilliseconds(slideOut ? 240 : 300);

            if (!slideOut)
            {
                visual.Offset = new Vector3(visual.Offset.X, targetY, 0);
                NavBar.Visibility = Visibility.Visible;
            }

            var batch = compositor.CreateScopedBatch(CompositionBatchTypes.Animation);
            visual.StartAnimation("Offset.Y", anim);
            batch.End();

            if (slideOut)
                batch.Completed += (_, _) => NavBar.Visibility = Visibility.Collapsed;
        }

        private async Task OpenArticleAsync(int postId)
        {
            AnimateNavBar(slideOut: true);
            HomeView.Visibility       = Visibility.Collapsed;
            BlogView.Visibility       = Visibility.Collapsed;
            NewsletterView.Visibility = Visibility.Collapsed;
            ArticleToolbar.Visibility = Visibility.Visible;
            ArticleWebView.Visibility = Visibility.Visible;

            if (ArticleWebView.CoreWebView2 == null)
            {
                try
                {
                    await ArticleWebView.EnsureCoreWebView2Async();
                }
                catch (Exception ex)
                {
                    // Attempt fallback: create environment pointing to local Edge installation(s)
                    Exception? lastEx = ex;
                    var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
                    var programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
                    // Prefer the folder matching the current process architecture to avoid BadImageFormatException
                    // Common locations for browser/runtime: Edge and Edge WebView2 runtime
                    var edgeApp64 = Path.Combine(programFiles, "Microsoft", "Edge", "Application");
                    var edgeApp32 = Path.Combine(programFilesX86, "Microsoft", "Edge", "Application");
                    var edgeWebView64 = Path.Combine(programFiles, "Microsoft", "EdgeWebView", "Application");
                    var edgeWebView32 = Path.Combine(programFilesX86, "Microsoft", "EdgeWebView", "Application");

                    var probePaths = Environment.Is64BitProcess
                        ? new[] { edgeWebView64, edgeApp64, edgeWebView32, edgeApp32 }
                        : new[] { edgeWebView32, edgeApp32, edgeWebView64, edgeApp64 };

                    var probeLogs = new List<string>();
                    foreach (var p in probePaths)
                    {
                        try
                        {
                            if (!Directory.Exists(p)) continue;
                            var exePath = Path.Combine(p, "msedge.exe");
                            // If the msedge.exe in this folder doesn't match the process architecture, skip it
                            bool exeMatches = ExeMatchesProcessArchitecture(exePath);
                            string? avail = null;
                            try { avail = Microsoft.Web.WebView2.Core.CoreWebView2Environment.GetAvailableBrowserVersionString(p); } catch { avail = null; }

                            probeLogs.Add($"Path={p}; Exists={Directory.Exists(p)}; Exe={exePath}; ExeMatches={exeMatches}; AvailableVersion={avail ?? "(none)"}");

                            if (!exeMatches) continue;
                            if (string.IsNullOrEmpty(avail)) continue;

                            var env = await Microsoft.Web.WebView2.Core.CoreWebView2Environment.CreateWithOptionsAsync(p, null, null);
                            await ArticleWebView.EnsureCoreWebView2Async(env);
                            lastEx = null;
                            break;
                        }
                        catch (Exception ex2)
                        {
                            // If the runtime in the probed folder is the wrong architecture this can throw
                            // BadImageFormatException (0x800700C1). Record the exception and continue probing.
                            lastEx = ex2;
                            probeLogs.Add($"Path={p}; Exception={ex2.GetType().Name}: {ex2.Message}");
                        }
                    }

                    if (lastEx != null)
                    {
                        // Hide article UI and restore previous view
                        ArticleWebView.Visibility = Visibility.Collapsed;
                        ArticleToolbar.Visibility = Visibility.Collapsed;
                        HomeView.Visibility       = _currentTab == NavTab.Home       ? Visibility.Visible : Visibility.Collapsed;
                        BlogView.Visibility       = _currentTab == NavTab.Blog       ? Visibility.Visible : Visibility.Collapsed;
                        NewsletterView.Visibility = _currentTab == NavTab.Newsletter ? Visibility.Visible : Visibility.Collapsed;
                        AnimateNavBar(slideOut: false);

                        var downloadUrl = "https://developer.microsoft.com/en-us/microsoft-edge/webview2/#download-section";
                        var details = $"{lastEx.GetType().Name} (0x{lastEx.HResult:X8}): {lastEx.Message}\n\n" +
                                      $"ProcessIs64Bit={Environment.Is64BitProcess}; ProcessArchitecture={(Environment.Is64BitProcess ? "64-bit" : "32-bit")}\n" +
                                      "Probed paths:\n" + string.Join("\n", probeLogs) + "\n\n" +
                                      "Ensure the WebView2 runtime matching the app architecture is installed.\n" +
                                      downloadUrl;

                        var dlg = new ContentDialog
                        {
                            Title = "WebView2 Init Error",
                            Content = details,
                            PrimaryButtonText = "Download WebView2",
                            CloseButtonText = "OK"
                        };
                        if (Content is FrameworkElement fe) dlg.XamlRoot = fe.XamlRoot;
                        var res = await dlg.ShowAsync();
                        if (res == ContentDialogResult.Primary)
                        {
                            _ = Windows.System.Launcher.LaunchUriAsync(new Uri(downloadUrl));
                        }
                        return;
                    }
                }
            }

            string html;
            try
            {
                var resp = await Http.GetStringAsync($"{BaseUrl}/posts/{postId}?_fields=title,content,date");
                using var doc = JsonDocument.Parse(resp);
                string title   = doc.RootElement.GetProperty("title").GetProperty("rendered").GetString() ?? "";
                string content = doc.RootElement.GetProperty("content").GetProperty("rendered").GetString() ?? "";
                string date    = doc.RootElement.GetProperty("date").GetString() ?? "";
                if (DateTime.TryParse(date, out var dt))
                    date = dt.ToString("MMMM d, yyyy");
                html = BuildArticleHtml(title, date, content);
            }
            catch (Exception ex)
            {
                html = BuildArticleHtml("Error", "", $"<p>Failed to load post: {ex.Message}</p>");
            }

            if (ArticleWebView.CoreWebView2 == null)
            {
                // WebView2 isn't available despite earlier initialization — show error and return
                ArticleWebView.Visibility = Visibility.Collapsed;
                ArticleToolbar.Visibility = Visibility.Collapsed;
                HomeView.Visibility       = _currentTab == NavTab.Home       ? Visibility.Visible : Visibility.Collapsed;
                BlogView.Visibility       = _currentTab == NavTab.Blog       ? Visibility.Visible : Visibility.Collapsed;
                NewsletterView.Visibility = _currentTab == NavTab.Newsletter ? Visibility.Visible : Visibility.Collapsed;
                AnimateNavBar(slideOut: false);

                var dlg = new ContentDialog
                {
                    Title = "WebView2 Unavailable",
                    Content = "The WebView2 control failed to initialize.",
                    CloseButtonText = "OK"
                };
                if (Content is FrameworkElement fe) dlg.XamlRoot = fe.XamlRoot;
                await dlg.ShowAsync();
                return;
            }

            ArticleWebView.DispatcherQueue.TryEnqueue(() => { ArticleWebView.NavigateToString(html); });
        }

        private void ArticleWebView_CoreWebView2Initialized(WebView2 sender, object _)
        {
            sender.CoreWebView2.Settings.AreDevToolsEnabled = false;
            sender.CoreWebView2.NavigationStarting += OnArticleNavigationStarting;
        }

        private static void OnArticleNavigationStarting(
            Microsoft.Web.WebView2.Core.CoreWebView2 sender,
            Microsoft.Web.WebView2.Core.CoreWebView2NavigationStartingEventArgs e)
        {
            if (e.IsUserInitiated && e.Uri.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                e.Cancel = true;
                _ = Windows.System.Launcher.LaunchUriAsync(new Uri(e.Uri));
            }
        }

        private void BackButton_Click(object sender, RoutedEventArgs e)
        {
            ArticleWebView.Visibility = Visibility.Collapsed;
            ArticleToolbar.Visibility = Visibility.Collapsed;
            HomeView.Visibility       = _currentTab == NavTab.Home       ? Visibility.Visible : Visibility.Collapsed;
            BlogView.Visibility       = _currentTab == NavTab.Blog       ? Visibility.Visible : Visibility.Collapsed;
            NewsletterView.Visibility = _currentTab == NavTab.Newsletter ? Visibility.Visible : Visibility.Collapsed;
            AnimateNavBar(slideOut: false);
        }

        private static string BuildArticleHtml(string title, string date, string content) => $$"""
            <!DOCTYPE html>
            <html lang="en">
            <head>
            <meta charset="utf-8"/>
            <meta name="viewport" content="width=device-width, initial-scale=1"/>
            <style>
              :root { color-scheme: light dark; }
              * { box-sizing: border-box; margin: 0; padding: 0; }
              body {
                font-family: 'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif;
                font-size: 14px;
                line-height: 1.7;
                background: light-dark(#f3f3f3, #202020);
                color: light-dark(#1a1a1a, #ffffff);
                padding: 32px;
                max-width: 820px;
                margin: 0 auto;
              }
              h1 { font-size: 28px; font-weight: 600; margin-bottom: 6px; line-height: 1.3; }
              .meta { font-size: 12px; color: light-dark(#605e5c, #9d9d9d); margin-bottom: 28px; }
              .content img { max-width: 100%; height: auto; border-radius: 4px; margin: 12px 0; }
              .content p { margin: 0.6em 0; }
              .content h2 { font-size: 20px; font-weight: 600; margin: 1.4em 0 0.4em; }
              .content h3 { font-size: 17px; font-weight: 600; margin: 1.2em 0 0.4em; }
              a { color: #0078d4; text-decoration: none; }
              a:hover { text-decoration: underline; }
            </style>
            </head>
            <body>
              <h1>{{title}}</h1>
              <p class="meta">{{date}}</p>
              <div class="content">{{content}}</div>
            </body>
            </html>
            """;

        private void TermsButton_Click(object sender, RoutedEventArgs e) =>
            new WebWindow("Terms of Use", "https://legal.drpsphca.com/terms").Activate();

        private void PrivacyButton_Click(object sender, RoutedEventArgs e) =>
            new WebWindow("Privacy Policy", "https://legal.drpsphca.com/privacy").Activate();
    }
}
