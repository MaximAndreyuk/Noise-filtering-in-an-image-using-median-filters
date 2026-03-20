using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using System.Windows.Input;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.IO;
using System.Windows.Controls;

namespace ImageFilterApp
{
    public partial class MainWindow : Window
    {
        private BitmapSource _orig, _noisy, _restored, _classicRestored, _weightedRestored;

        private double _scale = 1.0;
        private const double STEP = 1.1, MIN_SCALE = 0.1, MAX_SCALE = 1000.0;
        private bool _busy = false;
        private bool _autoFit = true;
        private bool _firstLoad = true;

        public MainWindow()
        {
            InitializeComponent();
            this.KeyDown += MainWindow_KeyDown;
            this.SizeChanged += MainWindow_SizeChanged;
            UpdateImageInfo();
            sliderNoise.Value = 10;
        }

        private void MainWindow_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_autoFit && displayImage.Source != null && !_busy)
            {
                FitImageToView();
            }
        }

        private void MainWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.O && !_busy) rbOriginal.IsChecked = true;
            else if (e.Key == Key.N && !_busy) rbNoisy.IsChecked = true;
            else if (e.Key == Key.R && !_busy) rbRestored.IsChecked = true;
            else if (e.Key == Key.Add || e.Key == Key.OemPlus) ZoomIn();
            else if (e.Key == Key.Subtract || e.Key == Key.OemMinus) ZoomOut();
            else if (e.Key == Key.F && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                _autoFit = !_autoFit;
                if (_autoFit) FitImageToView();
                txtStatus.Text = _autoFit ? "Автомасштаб вкл (Ctrl+F)" : "Автомасштаб выкл (Ctrl+F)";
            }
        }

        #region Загрузка
        private void btnLoad_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog { Filter = "Изображения|*.bmp;*.jpg;*.jpeg;*.png;*.gif;*.tiff|Все файлы|*.*" };
            if (dlg.ShowDialog() != true) return;

            SetProcessingState(true, "Загрузка...");
            Task.Run(() =>
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(dlg.FileName);
                bmp.EndInit();
                bmp.Freeze();

                Dispatcher.Invoke(() =>
                {
                    _orig = bmp;
                    _noisy = null;
                    _classicRestored = null;
                    _weightedRestored = null;
                    _restored = null;

                    rbOriginal.IsChecked = true;

                    if (_autoFit || _firstLoad)
                    {
                        FitImageToView();
                        _firstLoad = false;
                    }

                    UpdateDisplayImage();
                    UpdateImageInfo();
                    SetProcessingState(false, $"Загружено: {Path.GetFileName(dlg.FileName)}");
                });
            });
        }

        private void FitImageToView()
        {
            if (displayImage.Source == null || scrollViewer == null) return;

            double vw = scrollViewer.ViewportWidth;
            double vh = scrollViewer.ViewportHeight;

            if (vw <= 0 || vh <= 0)
            {
                vw = scrollViewer.ActualWidth;
                vh = scrollViewer.ActualHeight;
                if (vw <= 0 || vh <= 0) return;
            }

            double iw = displayImage.Source.Width;
            double ih = displayImage.Source.Height;

            double newScale = Math.Min(vw / iw, vh / ih) * 0.95;
            newScale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, newScale));

            _scale = newScale;
            imageScale.ScaleX = imageScale.ScaleY = _scale;
            txtZoom.Text = $"{_scale * 100:F0}%";

            Dispatcher.BeginInvoke(new Action(() =>
            {
                scrollViewer.ScrollToHorizontalOffset(Math.Max(0, (iw * _scale - vw) / 2));
                scrollViewer.ScrollToVerticalOffset(Math.Max(0, (ih * _scale - vh) / 2));
            }));
        }
        #endregion

        #region Сохранение
        private void btnSave_Click(object sender, RoutedEventArgs e)
        {
            BitmapSource img = null;
            string type = "";

            if (rbOriginal.IsChecked == true && _orig != null)
            {
                img = _orig;
                type = "оригинал";
            }
            else if (rbNoisy.IsChecked == true && _noisy != null)
            {
                img = _noisy;
                type = $"зашумленное ({sliderNoise.Value:0}%)";
            }
            else if (rbRestored.IsChecked == true && _restored != null)
            {
                if (_restored == _classicRestored)
                    type = "восстановленное (классический)";
                else if (_restored == _weightedRestored)
                    type = "восстановленное (взвешенный)";
                else
                    type = "восстановленное";
                img = _restored;
            }
            else if (_orig != null)
            {
                img = _orig;
                type = "оригинал";
            }

            if (img == null)
            {
                ShowWarning("Нет изображения для сохранения!");
                return;
            }

            var dlg = new SaveFileDialog
            {
                Filter = "PNG|*.png|JPEG|*.jpg;*.jpeg|BMP|*.bmp|Все файлы|*.*",
                DefaultExt = "png",
                FileName = $"ImageFilter_{type}_{DateTime.Now:yyyyMMdd_HHmmss}"
            };

            if (dlg.ShowDialog() == true)
            {
                try
                {
                    SetProcessingState(true, $"Сохранение {type}...");

                    BitmapEncoder enc = GetEncoder(dlg.FileName);

                    if (img.Format == PixelFormats.Bgra32)
                    {
                        enc.Frames.Add(BitmapFrame.Create(img));
                    }
                    else
                    {
                        var converted = new FormatConvertedBitmap(img, PixelFormats.Bgra32, null, 0);
                        enc.Frames.Add(BitmapFrame.Create(converted));
                    }

                    using (var fs = new FileStream(dlg.FileName, FileMode.Create))
                    {
                        enc.Save(fs);
                    }

                    SetProcessingState(false, $"{type} сохранен: {Path.GetFileName(dlg.FileName)}");
                }
                catch (Exception ex)
                {
                    SetProcessingState(false, $"Ошибка сохранения");
                    MessageBox.Show($"Ошибка: {ex.Message}", "Ошибка",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private BitmapEncoder GetEncoder(string fn)
        {
            string ext = Path.GetExtension(fn).ToLower();
            if (ext == ".jpg" || ext == ".jpeg")
                return new JpegBitmapEncoder { QualityLevel = 95 };
            else if (ext == ".bmp")
                return new BmpBitmapEncoder();
            else
                return new PngBitmapEncoder();
        }
        #endregion

        #region Шум
        private async void btnAddNoise_Click(object sender, RoutedEventArgs e)
        {
            if (_orig == null) { ShowWarning("Сначала загрузите изображение!"); return; }

            var oldScale = _scale;
            var oldH = scrollViewer.HorizontalOffset;
            var oldV = scrollViewer.VerticalOffset;

            double lvl = sliderNoise.Value / 100.0;
            SetProcessingState(true, $"Добавление шума {sliderNoise.Value:0}%...");
            progressBar.Value = 0;

            var progress = new Progress<int>(v => progressBar.Value = v);
            var srcData = GetPixelData(_orig);
            var res = await Task.Run(() => AddSaltAndPepperNoise(srcData, lvl, progress));

            _noisy = CreateBitmapSource(res);
            _restored = null;
            _classicRestored = null;
            _weightedRestored = null;

            rbNoisy.IsChecked = true;

            if (!_autoFit)
            {
                _scale = oldScale;
                imageScale.ScaleX = imageScale.ScaleY = _scale;
                txtZoom.Text = $"{_scale * 100:F0}%";

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    scrollViewer.ScrollToHorizontalOffset(oldH);
                    scrollViewer.ScrollToVerticalOffset(oldV);
                }));
            }
            else
            {
                FitImageToView();
            }

            UpdateDisplayImage();
            SetProcessingState(false, $"Шум {sliderNoise.Value:0}% добавлен");
            progressBar.Value = 100;
        }

        private PixelData AddSaltAndPepperNoise(PixelData data, double lvl, IProgress<int> progress)
        {
            byte[] pixels = (byte[])data.Pixels.Clone();
            Random rnd = new Random();
            int total = data.Width * data.Height;
            int noisePixels = (int)(total * lvl);

            if (lvl > 0.5)
            {
                int processed = 0, interval = total / 100;
                for (int i = 0; i < total; i++)
                {
                    if (rnd.NextDouble() < lvl)
                    {
                        int pos = i * 4;
                        if (rnd.NextDouble() < 0.5)
                            for (int j = 0; j < 3; j++) pixels[pos + j] = 255;
                        else
                            for (int j = 0; j < 3; j++) pixels[pos + j] = 0;
                    }
                    if (++processed % interval == 0) progress?.Report(processed * 100 / total);
                }
            }
            else
            {
                for (int i = 0; i < noisePixels; i++)
                {
                    int pos = rnd.Next(total) * 4;
                    if (rnd.NextDouble() < 0.5)
                        for (int j = 0; j < 3; j++) pixels[pos + j] = 255;
                    else
                        for (int j = 0; j < 3; j++) pixels[pos + j] = 0;
                    if (i % 1000 == 0) progress?.Report(i * 100 / noisePixels);
                }
            }
            return new PixelData { Pixels = pixels, Width = data.Width, Height = data.Height, Stride = data.Stride };
        }
        #endregion

        #region Фильтры
        private int GetMaskSize()
        {
            if (rbMask3x3.IsChecked == true) return 3;
            if (rbMask5x5.IsChecked == true) return 5;
            if (rbMask7x7.IsChecked == true) return 7;
            return 3;
        }

        private async void btnMedianFilter_Click(object sender, RoutedEventArgs e)
            => await ApplyFilter("классический", false);

        private async void btnWeightedMedianFilter_Click(object sender, RoutedEventArgs e)
            => await ApplyFilter("взвешенный", true);

        private async Task ApplyFilter(string name, bool weighted)
        {
            BitmapSource src = null;
            string srcType = "";

            if (rbOriginal.IsChecked == true && _orig != null)
            {
                src = _orig;
                srcType = "оригинала";
            }
            else if (rbNoisy.IsChecked == true && _noisy != null)
            {
                src = _noisy;
                srcType = "зашумленного";
            }
            else if (rbRestored.IsChecked == true)
            {
                if (_restored != null)
                    src = _restored;
                else if (_noisy != null)
                    src = _noisy;
                else if (_orig != null)
                    src = _orig;

                srcType = "текущего";
            }
            else if (_orig != null)
            {
                src = _orig;
                srcType = "оригинала";
            }

            if (src == null)
            {
                ShowWarning("Сначала загрузите изображение!");
                return;
            }

            var oldScale = _scale;
            var oldH = scrollViewer.HorizontalOffset;
            var oldV = scrollViewer.VerticalOffset;

            int mask = GetMaskSize();
            SetProcessingState(true, $"Применение {name} фильтра к {srcType} (маска {mask}x{mask})...");
            progressBar.Value = 0;

            var progress = new Progress<int>(v => progressBar.Value = v);
            var data = GetPixelData(src);
            double lvl = sliderNoise.Value / 100.0;

            PixelData res = weighted
                ? await Task.Run(() => lvl > 0.3
                    ? HighNoiseFilter(data, progress, mask)
                    : WeightedFilter(data, progress, mask))
                : await Task.Run(() => ClassicFilter(data, progress, mask));

            if (weighted)
                _weightedRestored = CreateBitmapSource(res);
            else
                _classicRestored = CreateBitmapSource(res);

            _restored = weighted ? _weightedRestored : _classicRestored;
            rbRestored.IsChecked = true;

            if (!_autoFit)
            {
                _scale = oldScale;
                imageScale.ScaleX = imageScale.ScaleY = _scale;
                txtZoom.Text = $"{_scale * 100:F0}%";

                Dispatcher.BeginInvoke(new Action(() =>
                {
                    scrollViewer.ScrollToHorizontalOffset(oldH);
                    scrollViewer.ScrollToVerticalOffset(oldV);
                }));
            }
            else
            {
                FitImageToView();
            }

            UpdateDisplayImage();
            SetProcessingState(false, $"{name} фильтр (маска {mask}x{mask}) применен к {srcType}");
            progressBar.Value = 100;
        }

        private PixelData ClassicFilter(PixelData data, IProgress<int> progress, int mask)
        {
            byte[] inp = data.Pixels, outp = new byte[inp.Length];
            Array.Copy(inp, outp, inp.Length);
            int w = data.Width, h = data.Height, stride = data.Stride;
            int r = mask / 2;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    var (b, g, rr) = GetNeighborsClassic(inp, x, y, w, h, stride, r);
                    if (b.Count > 0)
                    {
                        b.Sort(); g.Sort(); rr.Sort();
                        int pos = y * stride + x * 4, idx = b.Count / 2;
                        outp[pos] = b[idx];
                        outp[pos + 1] = g[idx];
                        outp[pos + 2] = rr[idx];
                    }
                }
                if (y % 10 == 0) progress?.Report(y * 100 / h);
            });
            return new PixelData { Pixels = outp, Width = w, Height = h, Stride = stride };
        }

        private PixelData WeightedFilter(PixelData data, IProgress<int> progress, int mask)
        {
            byte[] inp = data.Pixels, outp = new byte[inp.Length];
            Array.Copy(inp, outp, inp.Length);
            int w = data.Width, h = data.Height, stride = data.Stride;
            int r = mask / 2;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int pos = y * stride + x * 4;
                    if (!IsNoise(inp[pos], inp[pos + 1], inp[pos + 2]))
                    {
                        outp[pos] = inp[pos];
                        outp[pos + 1] = inp[pos + 1];
                        outp[pos + 2] = inp[pos + 2];
                        continue;
                    }

                    var (b, g, rr) = GetNeighborsWeighted(inp, x, y, w, h, stride, r);
                    if (b.Count > 0)
                    {
                        b.Sort(); g.Sort(); rr.Sort();
                        int idx = b.Count / 2;
                        outp[pos] = b[idx];
                        outp[pos + 1] = g[idx];
                        outp[pos + 2] = rr[idx];
                    }
                }
                if (y % 10 == 0) progress?.Report(y * 100 / h);
            });
            return new PixelData { Pixels = outp, Width = w, Height = h, Stride = stride };
        }

        private PixelData HighNoiseFilter(PixelData data, IProgress<int> progress, int mask)
        {
            byte[] inp = data.Pixels, outp = new byte[inp.Length];
            Array.Copy(inp, outp, inp.Length);
            int w = data.Width, h = data.Height, stride = data.Stride;
            int r = mask / 2;

            Parallel.For(0, h, y =>
            {
                for (int x = 0; x < w; x++)
                {
                    int pos = y * stride + x * 4, good = 0;
                    var (allB, allG, allR) = (new List<byte>(), new List<byte>(), new List<byte>());

                    for (int ky = -r; ky <= r; ky++)
                        for (int kx = -r; kx <= r; kx++)
                        {
                            int nx = x + kx, ny = y + ky;
                            if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                            int p = ny * stride + nx * 4;
                            byte b = inp[p], g = inp[p + 1], rr = inp[p + 2];
                            bool noise = IsNoise(b, g, rr);
                            if (!noise) good++;

                            if (kx == 0 || ky == 0)
                            {
                                int weight = (noise ? 1 : 3) * (Math.Abs(kx) + Math.Abs(ky) == 0 ? 3 : 1);
                                AddValue(allB, allG, allR, b, g, rr, weight);
                            }
                        }

                    if (good >= 3 || allB.Count > 0)
                    {
                        allB.Sort(); allG.Sort(); allR.Sort();
                        int idx = allB.Count / 2;
                        outp[pos] = allB[idx];
                        outp[pos + 1] = allG[idx];
                        outp[pos + 2] = allR[idx];
                    }
                }
                if (y % 10 == 0) progress?.Report(y * 100 / h);
            });
            return new PixelData { Pixels = outp, Width = w, Height = h, Stride = stride };
        }

        private (List<byte> B, List<byte> G, List<byte> R) GetNeighborsClassic(byte[] pix, int x, int y, int w, int h, int stride, int r)
        {
            var (b, g, rr) = (new List<byte>(), new List<byte>(), new List<byte>());

            for (int ky = -r; ky <= r; ky++)
                for (int kx = -r; kx <= r; kx++)
                {
                    int nx = x + kx, ny = y + ky;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                    int pos = ny * stride + nx * 4;
                    b.Add(pix[pos]);
                    g.Add(pix[pos + 1]);
                    rr.Add(pix[pos + 2]);
                }

            return (b, g, rr);
        }

        private (List<byte> B, List<byte> G, List<byte> R) GetNeighborsWeighted(byte[] pix, int x, int y, int w, int h, int stride, int r)
        {
            var (b, g, rr) = (new List<byte>(), new List<byte>(), new List<byte>());

            for (int ky = -r; ky <= r; ky++)
                for (int kx = -r; kx <= r; kx++)
                {
                    if (kx != 0 && ky != 0) continue;

                    int nx = x + kx, ny = y + ky;
                    if (nx < 0 || nx >= w || ny < 0 || ny >= h) continue;

                    int pos = ny * stride + nx * 4;
                    byte bv = pix[pos], gv = pix[pos + 1], rv = pix[pos + 2];

                    int weight = 1;

                    if (kx == 0 && ky == 0)
                        weight = 3 * (r + 1);
                    else if (Math.Abs(kx) + Math.Abs(ky) == 1)
                        weight = 2 * r;
                    else
                        weight = r;

                    if (!IsNoise(bv, gv, rv))
                        weight += 2;

                    AddValue(b, g, rr, bv, gv, rv, weight);
                }

            return (b, g, rr);
        }

        private bool IsNoise(byte b, byte g, byte r)
        {
            if ((b == 255 && g == 255 && r == 255) || (b == 0 && g == 0 && r == 0)) return true;
            int avg = (b + g + r) / 3;
            if (avg < 10 || avg > 245)
            {
                int maxDiff = Math.Max(Math.Abs(b - g), Math.Max(Math.Abs(b - r), Math.Abs(g - r)));
                if (maxDiff < 30) return true;
            }
            return false;
        }

        private void AddValue(List<byte> b, List<byte> g, List<byte> r, byte bv, byte gv, byte rv, int weight)
        {
            for (int i = 0; i < weight; i++)
            {
                b.Add(bv);
                g.Add(gv);
                r.Add(rv);
            }
        }
        #endregion

        #region Работа с пикселями
        private PixelData GetPixelData(BitmapSource src)
        {
            if (src == null) return null;
            int w = src.PixelWidth, h = src.PixelHeight, stride = w * 4;
            byte[] pixels = new byte[h * stride];
            src.CopyPixels(pixels, stride, 0);
            return new PixelData { Pixels = pixels, Width = w, Height = h, Stride = stride };
        }

        private BitmapSource CreateBitmapSource(PixelData data) =>
            data?.Pixels == null ? null : BitmapSource.Create(data.Width, data.Height, 96, 96,
                PixelFormats.Bgra32, null, data.Pixels, data.Stride);

        private class PixelData { public byte[] Pixels; public int Width, Height, Stride; }
        #endregion

        #region Отображение
        private void ViewMode_Checked(object sender, RoutedEventArgs e) => UpdateDisplayImage();

        private void UpdateDisplayImage()
        {
            BitmapSource src = null;
            string mode = "";

            if (rbOriginal.IsChecked == true)
            {
                src = _orig;
                mode = "оригинал";
            }
            else if (rbNoisy.IsChecked == true)
            {
                src = _noisy ?? _orig;
                mode = _noisy != null ? $"с шумом {sliderNoise.Value:0}%" : "оригинал (шум отсутствует)";
            }
            else if (rbRestored.IsChecked == true)
            {
                src = _restored ?? _noisy ?? _orig;
                if (_restored == _classicRestored) mode = "восстановленное (классический)";
                else if (_restored == _weightedRestored) mode = "восстановленное (взвешенный)";
                else if (_restored != null) mode = "восстановленное";
                else if (_noisy != null) mode = "с шумом (ожидает фильтрации)";
                else mode = "оригинал";
            }

            if (src != null)
            {
                displayImage.Source = src;
                txtImageInfo.Text = $"{src.PixelWidth} x {src.PixelHeight} px";
                txtStatus.Text = $"{mode} | {src.PixelWidth}x{src.PixelHeight} | {_scale * 100:F0}%";
            }
        }

        private void UpdateImageInfo() => UpdateDisplayImage();
        #endregion

        #region Масштаб
        private void displayImage_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (e.Delta > 0) ZoomIn(); else ZoomOut();
            e.Handled = true;
        }

        private void btnZoomIn_Click(object sender, RoutedEventArgs e) => ZoomIn();
        private void btnZoomOut_Click(object sender, RoutedEventArgs e) => ZoomOut();
        private void btnZoomReset_Click(object sender, RoutedEventArgs e) => ResetZoom();

        private void btnFitToView_Click(object sender, RoutedEventArgs e)
        {
            _autoFit = true;
            FitImageToView();
            txtStatus.Text = "Изображение подогнано под размер окна";
        }

        private void ZoomIn()
        {
            if (displayImage.Source != null)
            {
                _autoFit = false;
                SetScale(_scale * STEP);
            }
        }

        private void ZoomOut()
        {
            if (displayImage.Source != null)
            {
                _autoFit = false;
                SetScale(_scale / STEP);
            }
        }

        private void ResetZoom()
        {
            if (displayImage.Source != null)
            {
                _autoFit = false;
                SetScale(1.0);
            }
        }

        private void SetScale(double scale)
        {
            _scale = Math.Max(MIN_SCALE, Math.Min(MAX_SCALE, scale));
            imageScale.ScaleX = imageScale.ScaleY = _scale;
            txtZoom.Text = $"{_scale * 100:F0}%";
        }
        #endregion

        #region Вспомогательные
        private void SetProcessingState(bool busy, string status)
        {
            _busy = busy;
            txtStatus.Text = status;
            btnLoad.IsEnabled = btnSave.IsEnabled = btnAddNoise.IsEnabled = btnMedianFilter.IsEnabled = btnWeightedMedianFilter.IsEnabled = !busy;
            btnFitToView.IsEnabled = !busy;
            btnZoomIn.IsEnabled = !busy;
            btnZoomOut.IsEnabled = !busy;
            btnZoomReset.IsEnabled = !busy;
            if (busy) progressBar.Value = 0;
        }

        private void ShowWarning(string msg) => MessageBox.Show(msg, "Предупреждение", MessageBoxButton.OK, MessageBoxImage.Warning);
        #endregion
    }
}