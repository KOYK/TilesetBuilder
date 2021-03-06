#region Copyright & License Information
/*
 * Copyright 2007-2015 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation. For more information,
 * see COPYING.
 */
#endregion

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using System.Xml;
using OpenRA.Graphics;

namespace OpenRA.TilesetBuilder
{
	public partial class FormBuilder : Form
	{
		string srcfile;
		int size;
		TerrainTypeInfo[] terrainType;

		void CreateNewTileset()
		{
			this.Show();
			using (var formNew = new FormNew())
				if (DialogResult.OK == formNew.ShowDialog())
				{
					srcfile = formNew.ImageFile;
					this.size = formNew.TileSize;

					var bitmap = new Bitmap(srcfile);

					if (!formNew.PaletteFromImage)
					{
						var terrainPalette = new ImmutablePalette(formNew.PaletteFile, new int[0]);
						bitmap.Palette = terrainPalette.AsSystemPalette();
					}

					InitializeSurface(bitmap);
				}
		}

		public FormBuilder(string src, string tsize, bool autoExport, string outputDir)
		{
			InitializeComponent();
			LoadTerrainDefinitions();

			size = int.Parse(tsize);

			if (src.Length > 0)
			{
				srcfile = src;
				var bitmap = new Bitmap(srcfile);
				InitializeSurface(bitmap);
			}
			else
				CreateNewTileset();

			if (autoExport)
			{
				Export(outputDir);
				Application.Exit();
			}
		}

		void LoadTileset()
		{
			try
			{
				var doc = new XmlDocument();
				doc.Load(Path.ChangeExtension(srcfile, "tsx"));

				foreach (var e in doc.SelectNodes("//name").OfType<XmlElement>())
					txtTilesetName.Text = e.GetAttribute("value");

				foreach (var e in doc.SelectNodes("//terrain").OfType<XmlElement>())
					surface1.TerrainTypes[int.Parse(e.GetAttribute("x")),
						int.Parse(e.GetAttribute("y"))] = int.Parse(e.GetAttribute("t"));

				foreach (var e in doc.SelectNodes("//template").OfType<XmlElement>())
					surface1.Templates.Add(new Template
					{
						Cells = e.SelectNodes("./cell").OfType<XmlElement>()
							.Select(f => new int2(int.Parse(f.GetAttribute("x")), int.Parse(f.GetAttribute("y"))))
							.ToDictionary(a => a, a => true)
					});
			}
			catch { }
		}

		void InitializeSurface(Bitmap bitmap)
		{
			surface1.TileSize = size;
			surface1.Image = bitmap;
			surface1.TilesPerRow = surface1.Image.Size.Width / surface1.TileSize;
			surface1.Image.SetResolution(96, 96); // people keep being noobs about DPI, and GDI+ cares.
			surface1.TerrainTypes = new int[surface1.Image.Width / size, surface1.Image.Height / size]; /* all passable by default */
			surface1.Templates = new List<Template>();
			surface1.Size = surface1.Image.Size;
			surface1.Enabled = true;
			LoadTileset();
		}

		void Save()
		{
			using (var w = XmlWriter.Create(Path.ChangeExtension(srcfile, "tsx"),
				new XmlWriterSettings { Indent = true, IndentChars = "  " }))
			{
				var tilesetName = txtTilesetName.Text;

				if (tilesetName.Length < 1)
					tilesetName = "Temperat";

				w.WriteStartDocument();

				w.WriteStartElement("tileset");
				w.WriteStartElement("name");
				w.WriteAttributeString("value", tilesetName);
				w.WriteEndElement();

				for (var i = 0; i <= surface1.TerrainTypes.GetUpperBound(0); i++)
					for (var j = 0; j <= surface1.TerrainTypes.GetUpperBound(1); j++)
						if (surface1.TerrainTypes[i, j] != 0)
						{
							w.WriteStartElement("terrain");
							w.WriteAttributeString("x", i.ToString());
							w.WriteAttributeString("y", j.ToString());
							w.WriteAttributeString("t", surface1.TerrainTypes[i, j].ToString());
							w.WriteEndElement();
						}

				foreach (var t in surface1.Templates)
				{
					w.WriteStartElement("template");

					foreach (var c in t.Cells.Keys)
					{
						w.WriteStartElement("cell");
						w.WriteAttributeString("x", c.X.ToString());
						w.WriteAttributeString("y", c.Y.ToString());
						w.WriteEndElement();
					}

					w.WriteEndElement();
				}

				w.WriteEndElement();
				w.WriteEndDocument();
			}
		}

		void LoadTerrainDefinitions()
		{
			var terrainDefinition = new Dictionary<string, TerrainTypeInfo>();
			var yaml = MiniYaml.DictFromFile("defaults.yaml");
			terrainDefinition = yaml["Terrain"].ToDictionary().Values.Select(y => new TerrainTypeInfo(y)).ToDictionary(t => t.Type);
			surface1.Icon = new Bitmap[terrainDefinition.Keys.Count];
			terrainType = new TerrainTypeInfo[terrainDefinition.Keys.Count];

			var title = this.Text;
			surface1.UpdateMouseTilePosition +=
				(x, y, tileNr) =>
				{
					this.Text = "{0} - {1} ({2,3}, {3,3}) tileNr: {4,3}".F(title, txtTilesetName.Text, x, y, tileNr);
				};

			surface1.Enabled = false;
			var i = 0;
			foreach (var deftype in terrainDefinition)
			{
				var icon = new Bitmap(16, 16);

				// Loop through the images pixels to reset color.
				for (var x = 0; x < icon.Width; x++)
				{
					for (var y = 0; y < icon.Height; y++)
					{
						var newColor = deftype.Value.Color;
						icon.SetPixel(x, y, newColor);
					}
				}

				surface1.Icon[i] = icon;
				terrainType[i] = deftype.Value;

				var terrainTypeButton = new ToolStripButton(deftype.Key, icon, TerrainTypeSelectorClicked);
				terrainTypeButton.ToolTipText = deftype.Key;
				terrainTypeButton.DisplayStyle = ToolStripItemDisplayStyle.ImageAndText;
				terrainTypeButton.Tag = i.ToString();
				terrainTypeButton.ImageAlign = ContentAlignment.MiddleLeft;
				terrainTypes.Items.Add(terrainTypeButton);
				i++;
			}
		}

		void TerrainTypeSelectorClicked(object sender, EventArgs e)
		{
			var tsb = (ToolStripButton)sender;
			surface1.InputMode = tsb.Tag as string;
			foreach (var innerTsb in tsb.Owner.Items.OfType<ToolStripButton>())
				innerTsb.Checked = false;
			tsb.Checked = true;
		}

		void SaveClicked(object sender, EventArgs e) { Save(); }
		void ShowOverlaysClicked(object sender, EventArgs e)
		{
			surface1.ShowTerrainTypes = ((ToolStripButton)sender).Checked;
			surface1.Invalidate();
		}

		void ExportClicked(object sender, EventArgs e)
		{
			Export("Tilesets");
		}

		void DumpClicked(object sender, EventArgs e)
		{
			ExportTemplateToTileNumberMapping();
		}

		string ExportPalette(List<Color> p, string file)
		{
			while (p.Count < Palette.Size) p.Add(Color.Black); // pad the palette out with extra blacks
			var paletteData = p.Take(Palette.Size).SelectMany(
				c => new byte[] { (byte)(c.R >> 2), (byte)(c.G >> 2), (byte)(c.B >> 2) }).ToArray();
			File.WriteAllBytes(file, paletteData);
			return file;
		}

		string ExportTemplate(Template t, int n, string suffix, string dir)
		{
			var tileSize = size;
			var filename = Path.Combine(dir, "{0}{1:00}{2}".F(txtTilesetName.Text, n, suffix));
			var totalTiles = t.Width * t.Height;

			var ms = new MemoryStream();
			using (var bw = new BinaryWriter(ms))
			{
				bw.Write((ushort)tileSize);
				bw.Write((ushort)tileSize);
				bw.Write((uint)totalTiles);
				bw.Write((ushort)t.Width);
				bw.Write((ushort)t.Height);
				bw.Write((uint)0);			// filesize placeholder
				bw.Flush();
				bw.Write((uint)ms.Position + 24);	// image start
				bw.Write((uint)0);			// 0 (32bits)
				bw.Write((uint)0x2c730f8c);		// magic?
				bw.Write((uint)0);			// flags start
				bw.Write((uint)0);			// walk start
				bw.Write((uint)0);			// index start

				var src = surface1.Image.Clone(new Rectangle(0, 0, surface1.Image.Width, surface1.Image.Height),
					surface1.Image.PixelFormat);

				var data = src.LockBits(new Rectangle(0, 0, src.Width, src.Height),
					ImageLockMode.ReadOnly, src.PixelFormat);

				unsafe
				{
					var p = (byte*)data.Scan0;

					for (var v = 0; v < t.Height; v++)
						for (var u = 0; u < t.Width; u++)
						{
							if (t.Cells.ContainsKey(new int2(u + t.Left, v + t.Top)))
							{
								var q = p + data.Stride * tileSize * (v + t.Top) + tileSize * (u + t.Left);
								for (var j = 0; j < tileSize; j++)
									for (var i = 0; i < tileSize; i++)
										bw.Write(q[i + j * data.Stride]);
							}
							else
								for (var x = 0; x < tileSize * tileSize; x++)
									bw.Write((byte)0);	/* TODO: don't fill with air */
						}
				}

				src.UnlockBits(data);

				bw.Flush();
				var indexStart = ms.Position;
				for (var v = 0; v < t.Height; v++)
					for (var u = 0; u < t.Width; u++)
						bw.Write(t.Cells.ContainsKey(new int2(u + t.Left, v + t.Top))
							? (byte)(u + t.Width * v)
							: (byte)0xff);

				bw.Flush();

				var flagsStart = ms.Position;
				for (var x = 0; x < totalTiles; x++)
					bw.Write((byte)0);

				bw.Flush();

				var walkStart = ms.Position;
				for (var x = 0; x < totalTiles; x++)
					bw.Write((byte)0x8);

				var bytes = ms.ToArray();
				Array.Copy(BitConverter.GetBytes((uint)bytes.Length), 0, bytes, 12, 4);
				Array.Copy(BitConverter.GetBytes(flagsStart), 0, bytes, 28, 4);
				Array.Copy(BitConverter.GetBytes(walkStart), 0, bytes, 32, 4);
				Array.Copy(BitConverter.GetBytes(indexStart), 0, bytes, 36, 4);

				File.WriteAllBytes(filename, bytes);
			}

			return filename;
		}

		void Export(string outputDir)
		{
			var dir = Platform.ResolvePath("^", outputDir);
			Directory.CreateDirectory(dir);
			var tilesetName = txtTilesetName.Text;
			var tilesetID = txtID.Text;
			var tilesetPalette = txtPal.Text;
			var tilesetExt = txtExt.Text;

			if (tilesetName.Length < 1) tilesetName = "Temperat";
			if (tilesetID.Length < 1) tilesetID = "TEMPERAT";
			if (tilesetPalette.Length < 1) tilesetPalette = "temperat";
			if (tilesetExt.Length < 1) tilesetExt = ".tem";

			// Create a Tileset definition
			// TODO: Pull this info from the GUI
			var tilesetFile = "";
			tilesetFile = tilesetName.ToLower();
			if (tilesetFile.Length < 8)
				tilesetFile = tilesetName.ToLower() + ".yaml";
			else
				tilesetFile = tilesetName.ToLower().Substring(0, 8) + ".yaml";

			var tileset = new TileSet(
				name: tilesetName,
				id: tilesetID.ToUpper(),
				palette: tilesetPalette.ToLower(),
				terrainInfo: terrainType);

			// List of files to add to the mix file
			var fileList = new List<string>();

			// Export palette (use the embedded palette)
			var p = surface1.Image.Palette.Entries.ToList();
			fileList.Add(ExportPalette(p, Path.Combine(dir, tileset.Palette)));

			// Export tile artwork
			foreach (var t in surface1.Templates)
				fileList.Add(ExportTemplate(t, surface1.Templates.IndexOf(t), tilesetExt, dir));

			// Add the templates
			ushort cur = 0;
			foreach (var tp in surface1.Templates)
			{
				var tiles = new byte[tp.Width * tp.Height];
				foreach (var t in tp.Cells)
				{
					var ttype = terrainType[surface1.TerrainTypes[t.Key.X, t.Key.Y]].Type;
					var idx = (t.Key.X - tp.Left) + tp.Width * (t.Key.Y - tp.Top);
					tiles[idx] = tileset.GetTerrainIndex(ttype);
				}

				var template = new TerrainTemplateInfo(
					id: cur,
					images: new[] { "{0}{1:00}".F(txtTilesetName.Text, cur) },
					size: new int2(tp.Width, tp.Height),
					tiles: tiles);

				tileset.Templates.Add(cur, template);
				cur++;
			}

			tileset.Save(Path.Combine(dir, tilesetFile));
			Console.WriteLine("Finished export");
		}

		void ExportTemplateToTileNumberMapping()
		{
			Console.WriteLine("# start");
			Console.WriteLine("# TemplateID CellID tilenr TemplateW TemplateH XinTilesPNG YinTilesPNG");

			ushort cur = 0;
			foreach (var tp in surface1.Templates)
			{
				foreach (var t in tp.Cells)
				{
					var idx = (t.Key.X - tp.Left) + tp.Width * (t.Key.Y - tp.Top);

					// TemplateID CellID tilenr TemplateW TemplateH XinTilesPNG YinTilesPNG
					Console.WriteLine("{0} {1} {2} {3} {4} {5} {6}",
						  cur,
						  idx,
						  t.Key.Y * surface1.TilesPerRow + t.Key.X,
						  tp.Width,
						  tp.Height,
						  t.Key.X,
						  t.Key.Y);
				}

				cur++;
			}

			Console.WriteLine("# end\n");
		}

		void TilesetNameChanged(object sender, EventArgs e)
		{
			var tilesetFile = txtTilesetName.Text;
			if (tilesetFile.Length > 8)
				tilesetFile = tilesetFile.ToLower().Substring(0, 8);

			txtID.Text = tilesetFile.ToUpper();
			txtPal.Text = tilesetFile.ToLower() + ".pal";
			if (tilesetFile.Length < 3)
				txtExt.Text = ".tem,.shp";
			else
				txtExt.Text = "." + tilesetFile.ToLower().Substring(0, 3) + ",.shp";
		}

		void NewTilesetClicked(object sender, EventArgs e)
		{
			CreateNewTileset();
		}
	}
}