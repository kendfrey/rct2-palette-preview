using Microsoft.Win32;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
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

		int animationStyle;

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

				OpenImage(new BitmapImage(new Uri(dlg.FileName)), dlg.FileName);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void PasteImage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				if (Clipboard.GetImage() is BitmapSource img)
					OpenImage(img, "<pasted image>");
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void PasteImage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = Clipboard.ContainsImage();
		}

		private void OpenImage(BitmapSource img, string name)
		{
			(byte[] Pixels, int Width, int Height, double DpiX, double DpiY)? result;
			if (img.Format == PixelFormats.Indexed8
				&& MessageBox.Show("This image already has a palette. Would you like to use that palette to load the image? If you choose No, the default palette detection method will be used.", "Use Existing Palette?", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes)
				result = Load8BitImage(img);
			else
				result = LoadFullColorImage(img);

			if (result != null)
			{
				(pixels, width, height, dpiX, dpiY) = result.Value;
				ImageName.Content = name;
				RenderImage();
			}
		}

		private (byte[] Pixels, int Width, int Height, double DpiX, double DpiY)? Load8BitImage(BitmapSource img)
		{
			var pixels = new byte[img.PixelWidth * img.PixelHeight];
			img.CopyPixels(pixels, img.PixelWidth, 0);

			return (pixels, img.PixelWidth, img.PixelHeight, img.DpiX, img.DpiX);
		}

		private (byte[] Pixels, int Width, int Height, double DpiX, double DpiY)? LoadFullColorImage(BitmapSource img)
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

			IEnumerable<(Dictionary<Color, byte> PaletteMap, bool HasDuplicates)> paletteMapsToTry;
			if (paletteIndex < 0)
			{
				MessageBox.Show("The Palette Screenshot Pattern is not in this image. Please select a palette to load the image with.\r\n\r\nUse the palettescreenshotpattern.js OpenRCT2 plugin included with this program to avoid this message.", "Palette Not Detected", MessageBoxButton.OK, MessageBoxImage.Warning);

				var result = LoadPalette();
				if (result == null)
					return null;

				paletteMapsToTry = Enumerable.Range(0, 3).SelectMany(i => Enumerable.Range(0, 3).Select(j => MakePaletteMap(MakeFramePalette(result.Value.Palette, i, j), 0)));
			}
			else
			{
				paletteMapsToTry = new[] { MakePaletteMap(colors, paletteIndex) };
			}

			int bestScore = int.MaxValue; // Lower is better
			byte[] bestPixels = null;
			bool showPaletteWarning = false;
			foreach (var paletteMap in paletteMapsToTry)
			{
				byte[] pixels = new byte[colors.Length];
				int score = 0;
				for (int i = 0; i < pixels.Length; i++)
				{
					if (!paletteMap.PaletteMap.TryGetValue(colors[i], out pixels[i]))
					{
						pixels[i] = 1;
						score++;
					}
				}

				if (score < bestScore)
				{
					bestScore = score;
					bestPixels = pixels;
					showPaletteWarning = paletteMap.HasDuplicates;

					if (score == 0)
						break;
				}
			}
			
			if (showPaletteWarning)
				MessageBox.Show("There are some duplicate colors in this image's palette. These colors may be rendered incorrectly.\r\n\r\nUse the liminal.json OpenRCT2 palette included with this program to avoid this message.", "Duplicate Colors Detected", MessageBoxButton.OK, MessageBoxImage.Warning);

			if (bestScore != 0)
				MessageBox.Show("This image has some colors that are not in its palette. These colors will not be rendered.", "Missing Colors Detected", MessageBoxButton.OK, MessageBoxImage.Warning);

			return (bestPixels, img.PixelWidth, img.PixelHeight, img.DpiX, img.DpiX);
		}

		private void RenderImage()
		{
			if (pixels == null || palette == null)
				return;

			var animationFrames = new BitmapSource[15];
			for (int i = 0; i < 15; i++)
			{
				animationFrames[i] = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Indexed8, new BitmapPalette(MakeFramePalette(palette, i, animationStyle)), pixels, width);
			}

			this.animationFrames = animationFrames;
		}

		static readonly Color[] chainColor1 = new[] { Color.FromRgb(47, 47, 47), Color.FromRgb(39, 39, 43), Color.FromRgb(31, 35, 39) };
		static readonly Color[] chainColor2 = new[] { Color.FromRgb(87, 71, 47), Color.FromRgb(67, 55, 35), Color.FromRgb(63, 47, 27) };

		private Color[] MakeFramePalette(Color[] palette, int frame, int animationStyle)
		{
			var framePalette = new Color[256];
			Array.Fill(framePalette, Colors.Black);

			for (int i = 0; i < 220; i++)
			{
				framePalette[i + 10] = palette[i];
			}
			for (int i = 0; i < 3; i++)
			{
				framePalette[i + 240] = palette[i + 230];
			}
			for (int i = 0; i < 5; i++)
			{
				framePalette[i + 230] = palette[(i * 3 - frame + 15) % 15 + animationStyle * 15 + 236];
			}
			for (int i = 0; i < 5; i++)
			{
				framePalette[i + 235] = palette[(i * 3 - frame + 15) % 15 + animationStyle * 15 + 281];
			}
			for (int i = 0; i < 3; i++)
			{
				framePalette[i + 243] = chainColor1[animationStyle];
			}
			framePalette[243 + frame % 3] = chainColor2[animationStyle];
			framePalette[255] = Colors.White;
			return framePalette;
		}

		private (Dictionary<Color, byte> PaletteMap, bool HasDuplicates) MakePaletteMap(Color[] palette, int paletteIndex)
		{
			var paletteMap = new Dictionary<Color, byte>();
			bool hasDuplicates = false;
			for (int i = 0; i < 256; i++)
			{
				var color = palette[paletteIndex + i];
				if (paletteMap.ContainsKey(color))
				{
					if (!InPaletteRange(i))
						continue;
					else if (!InPaletteRange(paletteMap[color]))
						paletteMap[color] = (byte)i;
					else
						hasDuplicates = true;
				}
				else
				{
					paletteMap.Add(color, (byte)i);
				}

				static bool InPaletteRange(int i) => i >= 10 && i < 243;
			}
			return (paletteMap, hasDuplicates);
		}

		private void SaveImage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				if (animationFrames == null)
					return;

				var dlg = new SaveFileDialog();
				dlg.Filter = "Images|*.png";
				if (dlg.ShowDialog() != true)
					return;

				using (var file = File.OpenWrite(dlg.FileName))
				{
					var encoder = new PngBitmapEncoder();
					encoder.Frames.Add(BitmapFrame.Create(animationFrames[0]));
					encoder.Save(file);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void SaveImage_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = animationFrames != null;
		}

		private void CopyImage_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				if (animationFrames == null)
					return;

				Clipboard.SetImage(animationFrames[0]);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private void OpenPalette_Executed(object sender, ExecutedRoutedEventArgs e)
		{
			try
			{
				var result = LoadPalette();
				if (result != null)
				{
					palette = result.Value.Palette;
					PaletteName.Content = result.Value.Name;
					RenderImage();
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
			}
		}

		private (Color[] Palette, string Name)? LoadPalette()
		{
			var dlg = new OpenFileDialog();
			dlg.Filter = "Images|*.png;*.bmp|DAT Files|*.dat|JSON Files|*.json|All Files|*.*";
			if (dlg.ShowDialog() != true)
				return null;

			switch (Path.GetExtension(dlg.FileName).ToLower())
			{
				case ".dat":
					return (LoadDatPalette(dlg.FileName), dlg.FileName);
				case ".png":
				case ".bmp":
					return (LoadBitmapPalette(dlg.FileName), dlg.FileName);
				case ".json":
					return (LoadJsonPalette(dlg.FileName), dlg.FileName);
				default:
					MessageBox.Show($"The file extension {Path.GetExtension(dlg.FileName)} is not supported.", "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
					return null;
			}
		}

		private Color[] LoadDatPalette(string fileName)
		{
			var file = File.ReadAllBytes(fileName);
			if (file[16] != 0x01)
			{
				MessageBox.Show($"This DAT file uses the encoding {file[16]} which is not supported.", "Unsupported Encoding", MessageBoxButton.OK, MessageBoxImage.Error);
				return null;
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
			return GetColors(decodedBytes.AsSpan(endOfStringTable + 8 + imageDirectoryLength * 16));
		}

		private Color[] LoadBitmapPalette(string fileName)
		{
			var img = new BitmapImage(new Uri(fileName));
			return GetColorData(img);
		}

		private Color[] LoadJsonPalette(string fileName)
		{
			var palettes = JsonConvert.DeserializeObject<JObject>(File.ReadAllText(fileName))["properties"]["palettes"];
			return new string[] { "general", "waves-0", "waves-1", "waves-2", "sparkles-0", "sparkles-1", "sparkles-2" }
				.SelectMany(s => palettes[s]["colours"].Select(c => (Color)ColorConverter.ConvertFromString(c.Value<string>()))).ToArray();
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

		private void SavePalette_CanExecute(object sender, CanExecuteRoutedEventArgs e)
		{
			e.CanExecute = palette != null;
		}

		private Color[] GetColorData(BitmapSource img)
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

		private void AnimationStyle_Click(object sender, RoutedEventArgs e)
		{
			foreach (var item in ((sender as MenuItem).Parent as MenuItem).Items.Cast<MenuItem>())
			{
				item.IsChecked = item == sender;
			}

			var newAnimationStyle = int.Parse((sender as MenuItem).Tag as string);
			if (newAnimationStyle == animationStyle)
				return;

			animationStyle = newAnimationStyle;
			RenderImage();
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
