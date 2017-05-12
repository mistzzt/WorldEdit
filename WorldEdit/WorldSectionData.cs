using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OTAPI.Tile;
using Terraria;
using Terraria.DataStructures;
using Terraria.GameContent.Tile_Entities;
using Terraria.ID;
using TShockAPI;

namespace WorldEdit
{
	public sealed class WorldSectionData
	{
		public IList<SignData> Signs;

		public IList<ChestData> Chests;

		public IList<ItemFrameData> ItemFrames;

		public ITile[,] Tiles;

		public int Width;

		public int Height;

		public int X;

		public int Y;

		public WorldSectionData(int width, int height)
		{
			Width = width;
			Height = height;

			Signs = new SignData[0];
			Chests = new ChestData[0];
			ItemFrames = new ItemFrameData[0];
			Tiles = new ITile[width, height];
		}

		public void ProcessTile(ITile tile, int x, int y)
		{
			Tiles[x, y] = tile;

			if (!tile.active())
			{
				return;
			}

			var actualX = x + X;
			var actualY = y + Y;

			switch (tile.type)
			{
				case TileID.Signs:
				case TileID.AnnouncementBox:
				case TileID.Tombstones:
					if (tile.frameX % 36 == 0 && tile.frameY == 0)
					{
						var id = Sign.ReadSign(actualX, actualY, false);
						if (id != -1)
						{
							Signs.Add(new SignData
							{
								Text = Main.sign[id].text,
								X = x,
								Y = y
							});
						}
					}
					break;
				case TileID.ItemFrame:
					if (tile.frameX % 36 == 0 && tile.frameY == 0)
					{
						var id = TEItemFrame.Find(actualX, actualY);
						if (id != -1)
						{
							var frame = (TEItemFrame)TileEntity.ByID[id];
							ItemFrames.Add(new ItemFrameData
							{
								Item = new NetItem(frame.item.netID, frame.item.stack, frame.item.prefix)
							});
						}
					}
					break;
				case TileID.Containers:
				case TileID.Dressers:
					if (tile.frameX % 36 == 0 && tile.frameY == 0)
					{
						var id = Chest.FindChest(actualX, actualY);
						if (id != -1)
						{
							var chest = Main.chest[id];
							if (chest.item != null)
							{
								var items = chest.item.Select(item => new NetItem(item.netID, item.stack, item.prefix)).ToArray();
								Chests.Add(new ChestData
								{
									Items = items,
									X = x,
									Y = y
								});
							}
						}
					}
					break;
			}
		}

		public void Write(BinaryWriter writer)
		{
			writer.Write(X);
			writer.Write(Y);
			writer.Write(Width);
			writer.Write(Height);

			for (var i = 0; i < Width; i++)
			{
				for (var j = 0; j < Height; j++)
				{
					writer.Write(Tiles[i, j]);
				}
			}

			writer.Write(Signs.Count);
			foreach (var sign in Signs)
			{
				writer.Write(sign.X);
				writer.Write(sign.Y);
				writer.Write(sign.Text);
			}

			writer.Write(Chests.Count);
			foreach (var chest in Chests)
			{
				writer.Write(chest.X);
				writer.Write(chest.Y);
				writer.Write(chest.Items.Length);
				foreach (var t in chest.Items)
				{
					writer.Write(t.NetId);
					writer.Write(t.Stack);
					writer.Write(t.PrefixId);
				}
			}

			writer.Write(ItemFrames.Count);
			foreach (var itemFrame in ItemFrames)
			{
				writer.Write(itemFrame.X);
				writer.Write(itemFrame.Y);
				writer.Write(itemFrame.Item.NetId);
				writer.Write(itemFrame.Item.Stack);
				writer.Write(itemFrame.Item.PrefixId);
			}
		}

		public void Write(string filePath)
		{
			using (var writer =
				new BinaryWriter(
					new BufferedStream(
						new GZipStream(File.Open(filePath, FileMode.Create), CompressionMode.Compress), Tools.BUFFER_SIZE)))
			{
				Write(writer);
			}
		}

		public static BinaryWriter WriteHeader(string filePath, int x, int y, int width, int height)
		{
			var writer =
				new BinaryWriter(
					new BufferedStream(
						new GZipStream(File.Open(filePath, FileMode.Create), CompressionMode.Compress), Tools.BUFFER_SIZE));
			writer.Write(x);
			writer.Write(y);
			writer.Write(width);
			writer.Write(height);
			return writer;
		}

		public struct ItemFrameData
		{
			public int X;

			public int Y;

			public NetItem Item;
		}

		public struct ChestData
		{
			public int X;

			public int Y;

			public NetItem[] Items;
		}

		public struct SignData
		{
			public int X;

			public int Y;

			public string Text;
		}
	}
}
