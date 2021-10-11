using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace rct2_palette_preview
{
	/// <summary>
	/// Interaction logic for MainWindow.xaml
	/// </summary>
	public partial class MainWindow : Window
	{
		public static RoutedUICommand OpenPalette = new();
		public static RoutedUICommand SavePalette = new();

		DispatcherTimer animationTimer;
		BitmapSource[] animationFrames;
		int animationFrame;

		byte[] pixels;
		int width;
		int height;
		double dpiX;
		double dpiY;

		Color[] palette;

		public MainWindow()
		{
			InitializeComponent();

			animationTimer = new DispatcherTimer();
			animationTimer.Interval = TimeSpan.FromSeconds(1.0 / 15.0);
			animationTimer.Tick += AnimationTimer_Tick;
			animationTimer.Start();
		}

		private void OpenImage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				var dlg = new OpenFileDialog();
				dlg.Filter = "Images|*.png;*.bmp|All Files|*.*";
				if (dlg.ShowDialog() != true)
					return;

				var img = new BitmapImage(new Uri(dlg.FileName));
				if (img.Format == PixelFormats.Indexed8
					&& MessageBox.Show("This image already has a palette. Would you like to use that palette to load the image? If you choose No, the default palette detection method will be used.", "Use Existing Palette?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
					Load8BitImage(img);
				else
					LoadFullColorImage(img);

				ImageName.Content = dlg.FileName;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void Load8BitImage(BitmapImage img)
		{
			var pixels = new byte[img.PixelWidth * img.PixelHeight];
			img.CopyPixels(pixels, img.PixelWidth, 0);

			this.pixels = pixels;
			this.width = img.PixelWidth;
			this.height = img.PixelHeight;
			this.dpiX = img.DpiX;
			this.dpiY = img.DpiY;
			LoadImage();
		}

		private void LoadFullColorImage(BitmapImage img)
		{
			var colors = GetColorData(img);
			int paletteIndex;
			for (paletteIndex = Array.IndexOf(colors, Colors.White, 255); paletteIndex >= 0; paletteIndex = Array.IndexOf(colors, Colors.White, paletteIndex + 1))
			{
				var foundPalette = true;
				for (int i = 0; i < 9; i++)
				{
					if (colors[paletteIndex - 254 + i] != Colors.Black || colors[paletteIndex - 9 + i] != Colors.Black)
					{
						foundPalette = false;
						break;
					}
				}

				if (foundPalette)
				{
					paletteIndex = paletteIndex - 255;
					break;
				}
			}

			if (paletteIndex < 0)
			{
				MessageBox.Show("The Palette Screenshot Pattern is not in this image.\r\n\r\nUse the palettescreenshotpattern.js OpenRCT2 plugin included with this program to avoid this message.", "Palette Not Detected", MessageBoxButton.OK, MessageBoxImage.Warning);
				// TODO: load palette manually
				return;
			}

			var paletteMap = new Dictionary<Color, byte>();
			bool showPaletteWarning = false;
			for (int i = 0; i < 256; i++)
			{
				var color = colors[paletteIndex + i];
				if (paletteMap.ContainsKey(color))
				{
					if (!InPaletteRange(i))
						continue;
					else if (!InPaletteRange(paletteMap[color]))
						paletteMap[color] = (byte)i;
					else
						showPaletteWarning = true;
				}
				else
				{
					paletteMap.Add(color, (byte)i);
				}

				static bool InPaletteRange(int i) => i >= 10 && i < 243;
			}
			
			if (showPaletteWarning)
				MessageBox.Show("There are some duplicate colors in this image's palette. These colors may be rendered incorrectly.\r\n\r\nUse the liminal.json OpenRCT2 palette included with this program to avoid this message.", "Duplicate Colors Detected", MessageBoxButton.OK, MessageBoxImage.Warning);

			byte[] pixels = new byte[colors.Length];
			bool showPixelWarning = false;
			for (int i = 0; i < pixels.Length; i++)
			{
				if (!paletteMap.TryGetValue(colors[i], out pixels[i]))
				{
					pixels[i] = 1;
					showPixelWarning = true;
				}
			}
			if (showPixelWarning)
				MessageBox.Show("This image has some colors that are not in its palette. These colors will not be rendered.", "Missing Colors Detected", MessageBoxButton.OK, MessageBoxImage.Warning);

			this.pixels = pixels;
			this.width = img.PixelWidth;
			this.height = img.PixelHeight;
			this.dpiX = img.DpiX;
			this.dpiY = img.DpiY;
			LoadImage();
		}

		private void LoadImage()
		{
			if (pixels == null || palette == null)
				return;

			var animationFrames = new BitmapSource[15];
			for (int i = 0; i < 15; i++)
			{
				var framePalette = new Color[256];
				Array.Fill(framePalette, Colors.Black);

				for (int j = 0; j < 220; j++)
				{
					framePalette[j + 10] = palette[j];
				}
				for (int j = 0; j < 3; j++)
				{
					framePalette[j + 240] = palette[j + 230];
				}
				for (int j = 0; j < 5; j++)
				{
					framePalette[j + 230] = palette[(j * 3 - i + 15) % 15 + 236];
				}
				for (int j = 0; j < 5; j++)
				{
					framePalette[j + 235] = palette[(j * 3 - i + 15) % 15 + 281];
				}
				framePalette[255] = Colors.White;

				animationFrames[i] = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Indexed8, new BitmapPalette(framePalette), pixels, width);
			}

			this.animationFrames = animationFrames;
		}

		private void OpenPalette_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				var dlg = new OpenFileDialog();
				dlg.Filter = "Images|*.png;*.bmp|DAT Files|*.dat|JSON Files|*.json|All Files|*.*";
				if (dlg.ShowDialog() != true)
					return;

				switch (Path.GetExtension(dlg.FileName).ToLower())
				{
					case ".dat":
						LoadDatPalette(dlg.FileName);
						break;
					case ".png":
					case ".bmp":
						LoadBitmapPalette(dlg.FileName);
						break;
					case ".json":
						LoadJsonPalette(dlg.FileName);
						break;
					default:
						MessageBox.Show($"The file extension {Path.GetExtension(dlg.FileName)} is not supported.", "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
						return;
				}

				PaletteName.Content = dlg.FileName;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void LoadDatPalette(string fileName)
		{
			var file = File.ReadAllBytes(fileName);
			if (file[16] != 0x01)
			{
				MessageBox.Show($"This DAT file uses the encoding {file[16]} which is not supported.", "Unsupported Encoding", MessageBoxButton.OK, MessageBoxImage.Error);
				return;
			}

			var block = file.AsSpan(21, BitConverter.ToInt32(file, 17));
			var decodedBytesList = new List<byte>();
			for (int i = 0; i < block.Length;)
			{
				if (block[i] > 0x7F)
				{
					decodedBytesList.AddRange(Enumerable.Repeat(block[i + 1], 257 - block[i]));
					i += 2;
				}
				else
				{
					decodedBytesList.AddRange(block.Slice(i + 1, block[i] + 1).ToArray());
					i += block[i] + 2;
				}
			}
			var decodedBytes = decodedBytesList.ToArray();

			int endOfStringTable = decodedBytesList.IndexOf(0xFF) + 1;
			int imageDirectoryLength = BitConverter.ToInt32(decodedBytes, endOfStringTable);
			var palette = GetColors(decodedBytes.AsSpan(endOfStringTable + 8 + imageDirectoryLength * 16));
			LoadPalette(palette);
		}

		private void LoadBitmapPalette(string fileName)
		{
			var img = new BitmapImage(new Uri(fileName));
			LoadPalette(GetColorData(img));
		}

		private void LoadJsonPalette(string fileName)
		{
			var palettes = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(fileName))["properties"]["palettes"];
			var palette = new string[] { "general", "waves-0", "waves-1", "waves-2", "sparkles-0", "sparkles-1", "sparkles-2" }
				.SelectMany(s => palettes[s]["colours"].Select(c => (Color)ColorConverter.ConvertFromString(c.Value<string>()))).ToArray();
			LoadPalette(palette);
		}

		private void LoadPalette(Color[] palette)
		{
			this.palette = palette;
			LoadImage();
		}

		private void SavePalette_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				if (palette == null)
					return;

				var dlg = new SaveFileDialog();
				dlg.Filter = "JSON files|*.json";
				if (dlg.ShowDialog() != true)
					return;

				var paletteStrings = palette.Select(c => "#" + c.ToString().Substring(3)).ToArray();

				var id = Path.GetFileNameWithoutExtension(dlg.FileName);
				JObject json = new JObject();
				json.Add("id", id);
				json.Add("authors", new JArray("Unknown"));
				json.Add("version", "1.0");
				json.Add("originalId", id);
				json.Add("sourceGame", "custom");
				json.Add("objectType", "water");
				var palettes = new JObject();
				palettes.Add("general", new JObject() { { "index", 10 }, { "colours", new JArray(paletteStrings.AsSpan(0, 236).ToArray()) } });
				palettes.Add("waves-0", new JObject() { { "index", 16 }, { "colours", new JArray(paletteStrings.AsSpan(236, 15).ToArray()) } });
				palettes.Add("waves-1", new JObject() { { "index", 32 }, { "colours", new JArray(paletteStrings.AsSpan(251, 15).ToArray()) } });
				palettes.Add("waves-2", new JObject() { { "index", 48 }, { "colours", new JArray(paletteStrings.AsSpan(266, 15).ToArray()) } });
				palettes.Add("sparkles-0", new JObject() { { "index", 80 }, { "colours", new JArray(paletteStrings.AsSpan(281, 15).ToArray()) } });
				palettes.Add("sparkles-1", new JObject() { { "index", 96 }, { "colours", new JArray(paletteStrings.AsSpan(296, 15).ToArray()) } });
				palettes.Add("sparkles-2", new JObject() { { "index", 112 }, { "colours", new JArray(paletteStrings.AsSpan(311, 15).ToArray()) } });
				json.Add("properties", new JObject() { { "allowDucks", true }, { "palettes", palettes } });
				json.Add("strings", new JObject() { { "name", new JObject() { { "en-GB", id } } } });

				File.WriteAllText(dlg.FileName, json.ToString());
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private Color[] GetColorData(BitmapImage img)
		{
			var formatConvertedImg = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
			var colorData = new byte[formatConvertedImg.PixelWidth * formatConvertedImg.PixelHeight * 3];
			formatConvertedImg.CopyPixels(colorData, formatConvertedImg.PixelWidth * 3, 0);
			return GetColors(colorData);
		}

		private Color[] GetColors(Span<byte> colorData)
		{
			var colors = new Color[colorData.Length / 3];
			for (int i = 0; i < colors.Length; i++)
			{
				colors[i] = Color.FromRgb(colorData[i * 3 + 2], colorData[i * 3 + 1], colorData[i * 3]);
			}
			return colors;
		}

		private void AnimationTimer_Tick(object sender, EventArgs e)
		{
			if (animationFrames == null)
				return;

			animationFrame++;
			animationFrame %= animationFrames.Length;

			PreviewImage.Source = animationFrames[animationFrame];
		}
	}
}
