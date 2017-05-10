using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using OTAPI.Tile;
using Terraria;
using TShockAPI;
using TShockAPI.DB;
using Terraria.GameContent.Tile_Entities;
using Terraria.DataStructures;
using Terraria.ID;

namespace WorldEdit
{
	public static class Tools
	{
		internal const int BUFFER_SIZE = 1048576;
		private const int MAX_UNDOS = 50;

		private static readonly char[] InvalidFileNameChars = Path.GetInvalidFileNameChars();

		public static string GetClipboardPath(int accountID)
		{
			return Path.Combine("worldedit", string.Format("clipboard-{0}.dat", accountID));
		}
		public static bool IsCorrectName(string name)
		{
			return name.All(c => !InvalidFileNameChars.Contains(c));
		}
		public static List<int> GetColorID(string color)
		{
			int ID;
			if (int.TryParse(color, out ID) && ID >= 0 && ID < Main.numTileColors)
				return new List<int> { ID };

			var list = new List<int>();
			foreach (var kvp in WorldEdit.Colors)
			{
				if (kvp.Key == color)
					return new List<int> { kvp.Value };
				if (kvp.Key.StartsWith(color))
					list.Add(kvp.Value);
			}
			return list;
		}
		public static List<int> GetTileID(string tile)
		{
			int ID;
			if (int.TryParse(tile, out ID) && ID >= 0 && ID < Main.maxTileSets)
				return new List<int> { ID };

			var list = new List<int>();
			foreach (var kvp in WorldEdit.Tiles)
			{
				if (kvp.Key == tile)
					return new List<int> { kvp.Value };
				if (kvp.Key.StartsWith(tile))
					list.Add(kvp.Value);
			}
			return list;
		}
		public static List<int> GetWallID(string wall)
		{
			int ID;
			if (int.TryParse(wall, out ID) && ID >= 0 && ID < Main.maxWallTypes)
				return new List<int> { ID };

			var list = new List<int>();
			foreach (var kvp in WorldEdit.Walls)
			{
				if (kvp.Key == wall)
					return new List<int> { kvp.Value };
				if (kvp.Key.StartsWith(wall))
					list.Add(kvp.Value);
			}
			return list;
		}
		public static int GetSlopeID(string slope)
		{
			int ID;
			if (int.TryParse(slope, out ID) && ID >= 0 && ID < 6)
				return ID;

			if (!WorldEdit.Slopes.TryGetValue(slope, out int Slope)) { return -1; }

			return Slope;
		}

		public static bool HasClipboard(int accountID)
		{
			return File.Exists(Path.Combine("worldedit", string.Format("clipboard-{0}.dat", accountID)));
		}

		#region LoadWorldSectionData

		public static WorldSectionData LoadWorldData(string path)
		{
			using (var reader =
				new BinaryReader(
					new BufferedStream(
						new GZipStream(File.Open(path, FileMode.Open), CompressionMode.Decompress), BUFFER_SIZE)))
			{
				var x = reader.ReadInt32();
				var y = reader.ReadInt32();
				var width = reader.ReadInt32();
				var height = reader.ReadInt32();
				var worldData = new WorldSectionData(width, height) { X = x, Y = y };

				for (var i = 0; i < width; i++)
				{
					for (var j = 0; j < height; j++)
						worldData.Tiles[i, j] = reader.ReadTile();
				}

				var signCount = reader.ReadInt32();
				worldData.Signs = new WorldSectionData.SignData[signCount];
				for (var i = 0; i < signCount; i++)
				{
					worldData.Signs[i] = reader.ReadSign();
				}

				var chestCount = reader.ReadInt32();
				worldData.Chests = new WorldSectionData.ChestData[chestCount];
				for (var i = 0; i < chestCount; i++)
				{
					worldData.Chests[i] = reader.ReadChest();
				}

				var itemFrameCount = reader.ReadInt32();
				worldData.ItemFrames = new WorldSectionData.ItemFrameData[itemFrameCount];
				for (var i = 0; i < itemFrameCount; i++)
				{
					worldData.ItemFrames[i] = reader.ReadItemFrame();
				}

				return worldData;
			}
		}

		private static Tile ReadTile(this BinaryReader reader)
		{
			var tile = new Tile
			{
				sTileHeader = reader.ReadInt16(),
				bTileHeader = reader.ReadByte(),
				bTileHeader2 = reader.ReadByte()
			};

			// Tile type
			if (tile.active())
			{
				tile.type = reader.ReadUInt16();
				if (Main.tileFrameImportant[tile.type])
				{
					tile.frameX = reader.ReadInt16();
					tile.frameY = reader.ReadInt16();
				}
			}
			tile.wall = reader.ReadByte();
			tile.liquid = reader.ReadByte();
			return tile;
		}

		private static WorldSectionData.SignData ReadSign(this BinaryReader reader)
		{
			return new WorldSectionData.SignData
			{
				X = reader.ReadInt32(),
				Y = reader.ReadInt32(),
				Text = reader.ReadString()
			};
		}

		private static WorldSectionData.ChestData ReadChest(this BinaryReader reader)
		{
			var x = reader.ReadInt32();
			var y = reader.ReadInt32();

			var count = reader.ReadInt32();
			var items = new NetItem[count];

			for (var i = 0; i < count; i++)
			{
				items[i] = new NetItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte());
			}

			return new WorldSectionData.ChestData
			{
				Items = items,
				X = x,
				Y = y
			};
		}

		private static WorldSectionData.ItemFrameData ReadItemFrame(this BinaryReader reader)
		{
			return new WorldSectionData.ItemFrameData
			{
				X = reader.ReadInt32(),
				Y = reader.ReadInt32(),
				Item = new NetItem(reader.ReadInt32(), reader.ReadInt32(), reader.ReadByte())
			};
		}

		#endregion

		public static Tile[,] LoadWorldDataOld(string path)
		{
			// GZipStream is already buffered, but it's much faster to have a 1 MB buffer.
			using (var reader =
				new BinaryReader(
					new BufferedStream(
						new GZipStream(File.Open(path, FileMode.Open), CompressionMode.Decompress), BUFFER_SIZE)))
			{
				reader.ReadInt32();
				reader.ReadInt32();
				var width = reader.ReadInt32();
				var height = reader.ReadInt32();
				var tile = new Tile[width, height];

				for (var i = 0; i < width; i++)
				{
					for (var j = 0; j < height; j++)
					{
						tile[i, j] = reader.ReadTile();
					}
				}

				return tile;
			}
		}

		public static void LoadWorldSection(string path)
		{
			var data = LoadWorldData(path);

			for (var i = 0; i < data.Width; i++)
			{
				for (var j = 0; j < data.Height; j++)
				{
					Main.tile[i + data.X, j + data.Y] = data.Tiles[i, j];
					Main.tile[i + data.X, j + data.Y].skipLiquid(true);
				}
			}

			foreach (var sign in data.Signs)
			{
				var id = Sign.ReadSign(sign.X + data.X, sign.Y + data.Y);
				if (id == -1)
				{
					continue;
				}

				Sign.TextSign(id, sign.Text);
			}

			foreach (var itemFrame in data.ItemFrames)
			{
				var x = itemFrame.X + data.X;
				var y = itemFrame.Y + data.Y;

				var id = TEItemFrame.Place(x, y);
				if (id == -1)
				{
					continue;
				}

				WorldGen.PlaceObject(x, y, TileID.ItemFrame);
				var frame = (TEItemFrame) TileEntity.ByID[id];

				frame.item = new Item();
				frame.item.netDefaults(itemFrame.Item.NetId);
				frame.item.stack = itemFrame.Item.Stack;
				frame.item.prefix = itemFrame.Item.PrefixId;
			}

			foreach (var chest in data.Chests)
			{
				int x = chest.X + data.X, y = chest.Y + data.Y;

				var id = Chest.CreateChest(x, y);
				if (id == -1)
				{
					continue;
				}

				WorldGen.PlaceChest(x, y);
				for (var index = 0; index < chest.Items.Length; index++)
				{
					var netItem = chest.Items[index];
					var item = new Item();
					item.netDefaults(netItem.NetId);
					item.stack = netItem.Stack;
					item.prefix = netItem.PrefixId;
					Main.chest[id].item[index] = item;

				}
			}
			ResetSection(data.X, data.Y, data.X + data.Width, data.Y + data.Height);
		}

		public static void PrepareUndo(int x, int y, int x2, int y2, TSPlayer plr)
		{
			if (WorldEdit.Database.GetSqlType() == SqlType.Mysql)
				WorldEdit.Database.Query("INSERT IGNORE INTO WorldEdit VALUES (@0, -1, -1)", plr.User.ID);
			else
				WorldEdit.Database.Query("INSERT OR IGNORE INTO WorldEdit VALUES (@0, 0, 0)", plr.User.ID);
			WorldEdit.Database.Query("UPDATE WorldEdit SET RedoLevel = -1 WHERE Account = @0", plr.User.ID);
			WorldEdit.Database.Query("UPDATE WorldEdit SET UndoLevel = UndoLevel + 1 WHERE Account = @0", plr.User.ID);

			int undoLevel = 0;
			using (var reader = WorldEdit.Database.QueryReader("SELECT UndoLevel FROM WorldEdit WHERE Account = @0", plr.User.ID))
			{
				if (reader.Read())
					undoLevel = reader.Get<int>("UndoLevel");
			}

			string path = Path.Combine("worldedit", string.Format("undo-{0}-{1}.dat", plr.User.ID, undoLevel));
			SaveWorldSection(x, y, x2, y2, path);

			foreach (string fileName in Directory.EnumerateFiles("worldedit", string.Format("redo-{0}-*.dat", plr.User.ID)))
				File.Delete(fileName);
			File.Delete(Path.Combine("worldedit", string.Format("undo-{0}-{1}.dat", plr.User.ID, undoLevel - MAX_UNDOS)));
		}

		public static bool Redo(int accountID)
		{
			int redoLevel = 0;
			int undoLevel = 0;
			using (var reader = WorldEdit.Database.QueryReader("SELECT RedoLevel, UndoLevel FROM WorldEdit WHERE Account = @0", accountID))
			{
				if (reader.Read())
				{
					redoLevel = reader.Get<int>("RedoLevel") - 1;
					undoLevel = reader.Get<int>("UndoLevel") + 1;
				}
				else
					return false;
			}

			if (redoLevel < -1)
				return false;

			string redoPath = Path.Combine("worldedit", string.Format("redo-{0}-{1}.dat", accountID, redoLevel + 1));
			WorldEdit.Database.Query("UPDATE WorldEdit SET RedoLevel = @0 WHERE Account = @1", redoLevel, accountID);

			if (!File.Exists(redoPath))
				return false;

			string undoPath = Path.Combine("worldedit", string.Format("undo-{0}-{1}.dat", accountID, undoLevel));
			WorldEdit.Database.Query("UPDATE WorldEdit SET UndoLevel = @0 WHERE Account = @1", undoLevel, accountID);

			using (var reader = new BinaryReader(new GZipStream(new FileStream(redoPath, FileMode.Open), CompressionMode.Decompress)))
			{
				int x = Math.Max(0, reader.ReadInt32());
				int y = Math.Max(0, reader.ReadInt32());
				int x2 = Math.Min(x + reader.ReadInt32() - 1, Main.maxTilesX - 1);
				int y2 = Math.Min(y + reader.ReadInt32() - 1, Main.maxTilesY - 1);
				SaveWorldSection(x, y, x2, y2, undoPath);
			}
			LoadWorldSection(redoPath);
			File.Delete(redoPath);
			return true;
		}

		public static void ResetSection(int x, int y, int x2, int y2)
		{
			int lowX = Netplay.GetSectionX(x);
			int highX = Netplay.GetSectionX(x2);
			int lowY = Netplay.GetSectionY(y);
			int highY = Netplay.GetSectionY(y2);
			foreach (RemoteClient sock in Netplay.Clients.Where(s => s.IsActive))
			{
				for (int i = lowX; i <= highX; i++)
				{
					for (int j = lowY; j <= highY; j++)
						sock.TileSections[i, j] = false;
				}
			}
		}

		public static void SaveWorldSection(int x, int y, int x2, int y2, string path)
		{
			using (var writer =
				new BinaryWriter(
					new BufferedStream(
						new GZipStream(File.Open(path, FileMode.Create), CompressionMode.Compress), BUFFER_SIZE)))
			{
				var data = SaveWorldSection(x, y, x2, y2);

				data.Write(writer);
			}
		}

		public static void Write(this BinaryWriter writer, ITile tile)
		{
			writer.Write(tile.sTileHeader);
			writer.Write(tile.bTileHeader);
			writer.Write(tile.bTileHeader2);

			if (tile.active())
			{
				writer.Write(tile.type);
				if (Main.tileFrameImportant[tile.type])
				{
					writer.Write(tile.frameX);
					writer.Write(tile.frameY);
				}
			}
			writer.Write(tile.wall);
			writer.Write(tile.liquid);
		}

		public static WorldSectionData SaveWorldSection(int x, int y, int x2, int y2)
		{
			var width = x2 - x + 1;
			var height = y2 - y + 1;

			var data = new WorldSectionData(width, height)
			{
				X = x,
				Y = y,
				Chests = new List<WorldSectionData.ChestData>(),
				Signs = new List<WorldSectionData.SignData>(),
				ItemFrames = new List<WorldSectionData.ItemFrameData>()
			};

			for (var i = x; i <= x2; i++)
			{
				for (var j = y; j <= y2; j++)
				{
					data.ProcessTile(Main.tile[i, j], i - x, j - y);
				}
			}

			return data;
		}

		public static bool Undo(int accountID)
		{
			int redoLevel, undoLevel;
			using (var reader = WorldEdit.Database.QueryReader("SELECT RedoLevel, UndoLevel FROM WorldEdit WHERE Account = @0", accountID))
			{
				if (reader.Read())
				{
					redoLevel = reader.Get<int>("RedoLevel") + 1;
					undoLevel = reader.Get<int>("UndoLevel") - 1;
				}
				else
					return false;
			}

			if (undoLevel < -1)
				return false;

			string undoPath = Path.Combine("worldedit", string.Format("undo-{0}-{1}.dat", accountID, undoLevel + 1));
			WorldEdit.Database.Query("UPDATE WorldEdit SET UndoLevel = @0 WHERE Account = @1", undoLevel, accountID);

			if (!File.Exists(undoPath))
				return false;

			string redoPath = Path.Combine("worldedit", string.Format("redo-{0}-{1}.dat", accountID, redoLevel));
			WorldEdit.Database.Query("UPDATE WorldEdit SET RedoLevel = @0 WHERE Account = @1", redoLevel, accountID);

			using (var reader = new BinaryReader(new GZipStream(new FileStream(undoPath, FileMode.Open), CompressionMode.Decompress)))
			{
				int x = Math.Max(0, reader.ReadInt32());
				int y = Math.Max(0, reader.ReadInt32());
				int x2 = Math.Min(x + reader.ReadInt32() - 1, Main.maxTilesX - 1);
				int y2 = Math.Min(y + reader.ReadInt32() - 1, Main.maxTilesY - 1);
				SaveWorldSection(x, y, x2, y2, redoPath);
			}
			LoadWorldSection(undoPath);
			File.Delete(undoPath);
			return true;
		}
	}
}