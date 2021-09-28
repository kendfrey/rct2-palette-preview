using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
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

		private void OpenImage_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dlg = new OpenFileDialog();
				dlg.Filter = "Images|*.png;*.bmp|All Files|*.*";
				if (dlg.ShowDialog() != true)
					return;

				var img = new BitmapImage(new Uri(dlg.FileName));
				if (img.Format == PixelFormats.Indexed8)
					Load8BitImage(img);
				else
					MessageBox.Show($"This image uses the format {img.Format} which is not supported.", "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
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
					framePalette[j + 240] = palette[j + 220];
				}
				for (int j = 0; j < 5; j++)
				{
					framePalette[j + 230] = palette[(j * 3 - i + 15) % 15 + 223];
				}
				for (int j = 0; j < 5; j++)
				{
					framePalette[j + 235] = palette[(j * 3 - i + 15) % 15 + 238];
				}
				framePalette[255] = Colors.White;

				animationFrames[i] = BitmapSource.Create(width, height, dpiX, dpiY, PixelFormats.Indexed8, new BitmapPalette(framePalette), pixels, width);
			}

			this.animationFrames = animationFrames;
		}

		private void OpenPalette_Click(object sender, RoutedEventArgs e)
		{
			try
			{
				var dlg = new OpenFileDialog();
				dlg.Filter = "Images|*.png;*.bmp|DAT Files|*.dat|All Files|*.*";
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
					default:
						MessageBox.Show($"The file extension {Path.GetExtension(dlg.FileName)} is not supported.", "Unsupported Format", MessageBoxButton.OK, MessageBoxImage.Error);
						break;
				}
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
			LoadPalette(decodedBytes.AsSpan(endOfStringTable + 8 + imageDirectoryLength * 16));
		}

		private void LoadBitmapPalette(string fileName)
		{
			var img = new BitmapImage(new Uri(fileName));
			LoadPalette(GetColorData(img));
		}

		private void LoadPalette(Span<byte> colorData)
		{
			var palette = new List<Color>(253);

			for (int i = 0; i < 220; i++)
			{
				palette.Add(ReadColor(colorData, i));
			}
			for (int i = 0; i < 3; i++)
			{
				palette.Add(ReadColor(colorData, i + 230));
			}
			for (int i = 0; i < 15; i++)
			{
				palette.Add(ReadColor(colorData, i + 236));
			}
			for (int i = 0; i < 15; i++)
			{
				palette.Add(ReadColor(colorData, i + 281));
			}

			this.palette = palette.ToArray();
			LoadImage();
		}

		private byte[] GetColorData(BitmapImage img)
		{
			var formatConvertedImg = new FormatConvertedBitmap(img, PixelFormats.Bgr24, null, 0);
			var colorData = new byte[formatConvertedImg.PixelWidth * formatConvertedImg.PixelHeight * 3];
			formatConvertedImg.CopyPixels(colorData, formatConvertedImg.PixelWidth * 3, 0);
			return colorData;
		}

		private Color ReadColor(Span<byte> colorData, int startIndex)
		{
			return Color.FromRgb(colorData[startIndex * 3 + 2], colorData[startIndex * 3 + 1], colorData[startIndex * 3]);
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
